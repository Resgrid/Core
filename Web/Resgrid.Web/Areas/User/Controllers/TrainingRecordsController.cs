using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Training;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Transcript and Records (Training T1, the one transcript). Every member reads their own transcript and submits
	/// outside training; an officer who may review a member (TrainingManagementScope.CanReviewRecordsOf) reads their transcript and
	/// verifies, rejects or voids their records; the department-wide pages (categories, bulk categorize, credits, backfill, history
	/// import, hours report, settings) need CanManageRecords, and credit mappings and "apply history to credits" also the
	/// certification manage right. The LMS add-on pages (ISO FSRS, NCCP, certificate templates) are built dark: NotFound unless the
	/// operator flag is on and the department may author LMS content. The Trainings module switch closes every page.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class TrainingRecordsController : SecureBaseController
	{
		public const int MaxImportBytes = 5 * 1024 * 1024;
		private const int MaxEvidenceRequestBytes = 12 * 1024 * 1024;
		private const int MaxImportRequestBytes = 16 * 1024 * 1024;

		private static readonly Dictionary<string, string> EvidenceTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "pdf", "application/pdf" },
			{ "jpg", "image/jpeg" },
			{ "jpeg", "image/jpeg" },
			{ "png", "image/png" },
			{ "gif", "image/gif" },
			{ "doc", "application/msword" },
			{ "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" }
		};

		private static readonly Regex MemberOverrideKey = new Regex(@"^member_([0-9]{1,7})$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILearningRecordService _learningRecords;
		private readonly ITrainingHistoryImportService _historyImport;
		private readonly ILmsAccessService _lmsAccess;
		private readonly ILmsTrainingService _lmsTraining;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICertificationService _certificationService;
		private readonly IPdfProvider _pdfProvider;
		private readonly IDocumentSealingService _sealing;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> _localizer;

		private TrainingManagementScope _scope;
		private Department _department;
		private bool _departmentRead;
		private Dictionary<string, string> _names;
		private HashSet<string> _activeIds;
		private List<TrainingRecordMemberOption> _memberOptions;
		private bool? _lmsAllowed;

		public TrainingRecordsController(ILearningRecordService learningRecords, ITrainingHistoryImportService historyImport, ILmsAccessService lmsAccess,
			ILmsTrainingService lmsTraining, IAuthorizationService authorizationService, IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService, ICertificationService certificationService, IPdfProvider pdfProvider,
			IDocumentSealingService sealing, IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> localizer,
			ITrainingCompletionRecordingService recordings, ITrainingSubmissionService submissions)
		{
			_submissions = submissions;
			_recordings = recordings;
			_learningRecords = learningRecords;
			_historyImport = historyImport;
			_lmsAccess = lmsAccess;
			_lmsTraining = lmsTraining;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_certificationService = certificationService;
			_pdfProvider = pdfProvider;
			_sealing = sealing;
			_localizer = localizer;
		}

		private readonly ITrainingCompletionRecordingService _recordings;

		/// <summary>Round 9B: members' training hours submissions with evidence and their optional review (notices, history, workflow).</summary>
		private readonly ITrainingSubmissionService _submissions;

		/// <summary>Round 9B: a TrainingReviews string (provenance labels, refusal codes of the evidence checks) in the current UI language.</summary>
		private static string ReviewText(string key) =>
			Resgrid.Localization.Areas.User.TrainingReviews.TrainingReviewTexts.Get(key, CultureInfo.CurrentUICulture.Name) ?? key;

		/// <summary>Round 7B: "recorded by" text of a completion an instructor recorded (who, when, from where, the note), localized; null otherwise.</summary>
		private string RecordedByText(LearningRecord record, IReadOnlyDictionary<int, TrainingCompletionRecording> recordings, IReadOnlyDictionary<string, string> names,
			Department department)
		{
			if (record == null || !record.IsRecordedByOther)
				return null;
			TrainingCompletionRecording recording = null;
			if (record.RecordedCompletionId.HasValue && recordings != null)
				recordings.TryGetValue(record.RecordedCompletionId.Value, out recording);
			var by = record.RecordedByUserId ?? recording?.RecordedByUserId;
			var name = by != null && names != null && names.TryGetValue(by, out var n) && !string.IsNullOrWhiteSpace(n) ? n : by;
			var on = record.RecordedOn ?? recording?.RecordedOn;
			var text = string.Format(CultureInfo.CurrentCulture, L("RecordCompletions.RecordedByFormat"), name,
				on.HasValue ? DateTime.SpecifyKind(on.Value, DateTimeKind.Utc).TimeConverter(department).ToString("d", CultureInfo.CurrentCulture) : string.Empty);
			if (recording != null)
				text += " · " + L("RecordCompletions.Source." + (TrainingCompletionRecordingSources)recording.SourceKind) +
					(string.IsNullOrWhiteSpace(recording.Note) ? string.Empty : " · " + recording.Note.Trim());
			return text;
		}

		#region Gates

		/// <summary>The Trainings module switch (DepartmentModuleSettings.TrainingDisabled) closes every records page.</summary>
		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (await IsModuleDisabledAsync())
			{
				context.Result = NotFound();
				return;
			}

			// The Trainings section strip follows the member's rights (Trainings/_Shell).
			ViewData["Scope"] = await ScopeAsync();
			await next();
		}

		[NonAction]
		public async Task<bool> IsModuleDisabledAsync()
		{
			try
			{
				var modules = await _departmentSettingsService.GetDepartmentModuleSettingsAsync(DepartmentId);
				return modules != null && modules.TrainingDisabled;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		private async Task<TrainingManagementScope> ScopeAsync()
		{
			return _scope ??= await _authorizationService.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);
		}

		/// <summary>Reviews somebody's records: department-wide record managers and group admins for their group.</summary>
		private async Task<bool> CanReviewAnyAsync()
		{
			var scope = await ScopeAsync();
			return scope.CanManageRecords || (scope.AssignRight.AdministeredMemberUserIds?.Count ?? 0) > 0;
		}

		private static bool CanManageCertifications => ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanManageCertifications();

		/// <summary>
		/// LMS pages (dark, §24): the operator flag, an LMS the department may author and manage, and the records right. Asked once
		/// per request, fails closed.
		/// </summary>
		private async Task<bool> LmsAllowedAsync()
		{
			if (_lmsAllowed.HasValue)
				return _lmsAllowed.Value;

			var allowed = false;
			try
			{
				var scope = await ScopeAsync();
				allowed = scope.CanManageRecords && await _lmsAccess.IsFlagEnabledAsync(DepartmentId) && await _lmsAccess.CanAuthorAndManageAsync(DepartmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				allowed = false;
			}

			_lmsAllowed = allowed;
			return allowed;
		}

		private async Task<Department> DepartmentAsync()
		{
			if (!_departmentRead)
			{
				_department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
				_departmentRead = true;
			}

			return _department;
		}

		private async Task<Dictionary<string, string>> NamesAsync()
		{
			return _names ??= (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
		}

		private async Task<HashSet<string>> ActiveIdsAsync()
		{
			return _activeIds ??= await _departmentsService.GetActiveMemberUserIdsAsync(DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Every member the department has had, former members marked (imported history and audits cover them).</summary>
		private async Task<List<TrainingRecordMemberOption>> MemberOptionsAsync()
		{
			if (_memberOptions != null)
				return _memberOptions;

			var names = await NamesAsync();
			var active = await ActiveIdsAsync();
			var members = await _departmentsService.GetAllMembersForDepartmentIncludingDeletedAsync(DepartmentId) ?? new List<DepartmentMember>();
			var ids = members.Where(m => m != null && m.DepartmentId == DepartmentId && !string.IsNullOrWhiteSpace(m.UserId)).Select(m => m.UserId)
				.Concat(active).Distinct(StringComparer.OrdinalIgnoreCase);

			_memberOptions = ids.Select(id => new TrainingRecordMemberOption
			{
				UserId = id,
				Name = names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : id,
				IsFormer = !active.Contains(id)
			}).OrderBy(o => o.IsFormer).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

			return _memberOptions;
		}

		private string L(string key)
		{
			var value = _localizer?[key]?.Value;
			return string.IsNullOrEmpty(value) ? key : value;
		}

		/// <summary>A service refusal code (learning_record_*) as text; unknown codes read as the generic failure.</summary>
		private string ErrorText(string code)
		{
			if (string.IsNullOrWhiteSpace(code))
				return L("Rec.ActionFailed");
			var text = _localizer?[code];
			if (text != null && !text.ResourceNotFound && !string.IsNullOrEmpty(text.Value))
				return text.Value;
			// Round 9B: the evidence checks' and the review's codes live with the TrainingReviews strings.
			var review = Resgrid.Localization.Areas.User.TrainingReviews.TrainingReviewTexts.Get(code, CultureInfo.CurrentUICulture.Name);
			return string.IsNullOrEmpty(review) ? L("Rec.ActionFailed") : review;
		}

		private static bool IsServiceCode(InvalidOperationException ex) =>
			ex?.Message != null && Regex.IsMatch(ex.Message, "^[a-z][a-z0-9_]{2,80}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		private void Flash(string message, string error = null)
		{
			if (TempData == null)
				return;
			if (!string.IsNullOrWhiteSpace(message))
				TempData["TrainingRecordsMessage"] = message;
			if (!string.IsNullOrWhiteSpace(error))
				TempData["TrainingRecordsError"] = error;
		}

		private static bool SameUser(string a, string b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		/// <summary>Fills what every records page shows: department, names, categories, the one-time notice and the officer strip.</summary>
		private async Task<T> PageAsync<T>(T view, string officerTab = null) where T : TrainingRecordsPageView
		{
			view.Department = await DepartmentAsync();
			view.Scope = await ScopeAsync();
			view.Names = await NamesAsync();
			view.ActiveMemberIds = await ActiveIdsAsync();
			view.Categories = await _learningRecords.GetCategoriesAsync(DepartmentId, true) ?? new List<TrainingCategory>();
			view.Message ??= TempData?["TrainingRecordsMessage"] as string;
			view.Error ??= TempData?["TrainingRecordsError"] as string;
			if (officerTab != null)
				view.OfficerTabs = await OfficerTabsAsync(officerTab);
			return view;
		}

		/// <summary>The officer pages' strip; the LMS entries only when the LMS check passed for this request.</summary>
		private async Task<ModuleTabsView> OfficerTabsAsync(string active)
		{
			var scope = await ScopeAsync();
			var tabs = new ModuleTabsView { Label = L("Rec.OfficerStripLabel") };

			void Add(string key, string icon, string action) => tabs.Tabs.Add(new ModuleTab(L(key), icon, "TrainingRecords", action, action == active));

			if (await CanReviewAnyAsync())
				Add("Rec.QueueTab", "fa-check-square-o", "Queue");
			// Round 9B: members' training hours submissions are reviewed in Training reviews.
			if (scope.CanReviewSubmissions)
				tabs.Tabs.Add(new ModuleTab(ReviewText("Reviews.Tab"), "fa-inbox", "TrainingReviews", "Index", false));

			if (scope.CanManageRecords)
			{
				Add("Rec.CategoriesTab", "fa-tags", "Categories");
				Add("Rec.CategorizeTab", "fa-folder-open-o", "Categorize");
				Add("Rec.CreditsTab", "fa-certificate", "Credits");
				Add("Rec.BackfillTab", "fa-history", "Backfill");
				Add("Rec.ImportTab", "fa-upload", "Import");
				Add("Rec.HoursTab", "fa-bar-chart", "Hours");
				Add("Rec.SettingsTab", "fa-cog", "Settings");

				if (await LmsAllowedAsync())
				{
					Add("Rec.IsoReportTab", "fa-shield", "IsoReport");
					Add("Rec.NccpReportTab", "fa-medkit", "NccpReport");
					Add("Rec.CertificateTemplatesTab", "fa-id-badge", "CertificateTemplates");
				}
			}

			// Training T5 data exit (round 6D): department owners and admins export every record at any time (free, §17.2), with the LMS
			// content when the department has any; the page works without the LMS flag or add-on.
			if (User?.HasClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update) == true)
				tabs.Tabs.Add(new ModuleTab(Resgrid.Localization.Areas.User.LmsExports.LmsExportTexts.Get("Exit.Tab", CultureInfo.CurrentUICulture.Name), "fa-archive", "LmsExports", "Index", false));

			return tabs;
		}

		#endregion Gates

		#region Dates and documents

		/// <summary>A department-local date's start as UTC.</summary>
		private static DateTime? StartUtc(DateTime? localDate, Department department) =>
			localDate.HasValue
				? DateTime.SpecifyKind(DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc)
				: (DateTime?)null;

		/// <summary>The end (exclusive) of a department-local date as UTC.</summary>
		private static DateTime? EndUtc(DateTime? localDate, Department department) =>
			localDate.HasValue ? StartUtc(localDate.Value.Date.AddDays(1), department) : null;

		private static DateTime LocalToday(Department department) => DateTime.UtcNow.TimeConverter(department).Date;

		private static void Order(ref DateTime? from, ref DateTime? to)
		{
			if (from.HasValue && to.HasValue && from.Value > to.Value)
				(from, to) = (to, from);
		}

		/// <summary>The documents print what they are given in department time.</summary>
		private static DateTime DocumentTime(DateTime utc, Department department) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).TimeConverter(department);

		private static string PageSize() =>
			string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase) ? "Letter" : "A4";

		private async Task<TrainingDocumentContext> DocumentContextAsync()
		{
			var department = await DepartmentAsync();
			var names = await NamesAsync();
			var culture = CultureInfo.CurrentUICulture;
			return new TrainingDocumentContext
			{
				DepartmentName = department?.Name,
				Labels = ((IEnumerable<string>)Resgrid.Services.TrainingDocuments.LabelKeys ?? Array.Empty<string>())
					.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).ToDictionary(k => k, k => L(k), StringComparer.Ordinal),
				TimeZone = department?.TimeZone,
				Culture = culture.Name,
				RightToLeft = culture.TextInfo.IsRightToLeft,
				GeneratedOnUtc = DateTime.UtcNow,
				GeneratedBy = UserId != null && names.TryGetValue(UserId, out var me) && !string.IsNullOrWhiteSpace(me) ? me : UserName
			};
		}

		private IActionResult PdfFile(string html, string fileName)
		{
			var bytes = _pdfProvider.ConvertHtmlToPdf(html, PageSize());
			return File(bytes, "application/pdf", fileName);
		}

		/// <summary>
		/// e-Signature S1 (R-S3): the document sealed at L3 when sealing is configured. A sealed copy of the same content is served
		/// again on re-download (certificates and transcripts issued before sealing existed are sealed the first time they are
		/// downloaded again); otherwise the unsealed PDF is returned and the document stays "sealing pending" for worker 90.
		/// </summary>
		private async Task<IActionResult> SealedPdfFileAsync(SignatureSubjectKinds kind, string subjectId, string revisionSha256, string title, string html, string fileName,
			int? retentionYears)
		{
			var department = await DepartmentAsync();
			var outcome = await _sealing.SealHtmlAsync(new SealHtmlRequest
			{
				DepartmentId = DepartmentId,
				SubjectKind = kind,
				SubjectId = subjectId,
				RevisionSha256 = revisionSha256,
				Title = title,
				FileName = fileName,
				Html = html,
				PageSize = PageSize(),
				RetentionYears = retentionYears,
				LongTermSubject = true,
				DepartmentName = department?.Name,
				Culture = CultureInfo.CurrentUICulture.Name,
				RequestedByUserId = UserId
			});
			Response.Headers["X-Resgrid-Seal"] = outcome.Sealed ? "sealed" : "pending";
			return File(outcome.Bytes, "application/pdf", fileName);
		}

		/// <summary>The content a certificate seal binds to: the verified record as printed (never the print time or the viewer).</summary>
		/// <remarks>Round 10: shared with the v4 download (TrainingDocuments), so either one's sealed copy is the one the other serves again.</remarks>
		public static string CertificateRevisionSha256(LearningRecord record, string verificationCode) =>
			Resgrid.Services.TrainingDocuments.CertificateRevisionSha256(record, verificationCode);

		/// <summary>The content a transcript seal binds to: its member, period, filters and lines (never the print time or the viewer).</summary>
		public static string TranscriptRevisionSha256(string userId, TranscriptDocument document, int? categoryId, int? source) =>
			Resgrid.Services.TrainingDocuments.TranscriptRevisionSha256(userId, document, categoryId, source);

		private IActionResult CsvFile(string csv, string fileName)
		{
			var body = Encoding.UTF8.GetBytes(csv ?? string.Empty);
			var preamble = Encoding.UTF8.GetPreamble();
			var bytes = new byte[preamble.Length + body.Length];
			Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
			Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
			return File(bytes, "text/csv", fileName);
		}

		private static string Stamp() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		private string CategoryLabel(int? categoryId, IReadOnlyCollection<TrainingCategory> categories)
		{
			if (!categoryId.HasValue)
				return L("Doc.Uncategorized");
			var category = categories.FirstOrDefault(c => c.TrainingCategoryId == categoryId.Value);
			if (category == null)
				return L("Doc.Uncategorized");
			return category.IsDeleted ? string.Format(L("Rec.RemovedCategory"), category.Name) : category.Name;
		}

		private string InstructorsOf(LearningRecord record, Dictionary<string, string> names)
		{
			if (!string.IsNullOrWhiteSpace(record.InstructorNames))
				return record.InstructorNames;
			if (string.IsNullOrWhiteSpace(record.InstructorUserIds))
				return null;
			return string.Join(", ", record.InstructorUserIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(id => id.Trim()).Where(id => id.Length > 0).Select(id => names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : id));
		}

		#endregion Dates and documents

		#region Transcript

		/// <summary>Who the transcript is for and whether the member may read it (own, or reviewable; former members stay readable).</summary>
		private async Task<(string Target, bool Self, IActionResult Refusal)> TranscriptAccessAsync(string userId)
		{
			var target = string.IsNullOrWhiteSpace(userId) ? UserId : userId.Trim();
			var self = SameUser(target, UserId);
			if (self)
				return (UserId, true, null);

			var scope = await ScopeAsync();
			if (!scope.CanReviewRecordsOf(target))
				return (target, false, Unauthorized());

			var member = (await MemberOptionsAsync()).FirstOrDefault(m => SameUser(m.UserId, target));
			if (member == null)
				return (target, false, NotFound());

			return (member.UserId, false, null);
		}

		private async Task<List<LearningRecord>> TranscriptRecordsAsync(string target, DateTime? from, DateTime? to, int? categoryId, int? source, bool includeVoided)
		{
			var department = await DepartmentAsync();
			Order(ref from, ref to);
			var categories = categoryId.HasValue ? new[] { categoryId.Value } : null;
			var sources = source.HasValue && Enum.IsDefined(typeof(LearningRecordSourceKinds), source.Value) ? new[] { source.Value } : null;
			return (await _learningRecords.GetTranscriptAsync(DepartmentId, target, StartUtc(from, department), EndUtc(to, department), categories, sources, includeVoided)
					?? new List<LearningRecord>())
				.Where(r => r != null && r.DepartmentId == DepartmentId && SameUser(r.UserId, target))
				.OrderByDescending(r => r.CompletedOn).ThenByDescending(r => r.LearningRecordId).ToList();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Transcript(string userId, DateTime? from, DateTime? to, int? categoryId, int? source, bool audit = false)
		{
			var access = await TranscriptAccessAsync(userId);
			if (access.Refusal != null)
				return access.Refusal;

			var scope = await ScopeAsync();
			var canAudit = scope.CanReviewRecordsOf(access.Target);
			audit = audit && canAudit;
			Order(ref from, ref to);

			var model = await PageAsync(new TrainingTranscriptView
			{
				UserId = access.Target,
				IsSelf = access.Self,
				From = LearningRecordPresentation.IsoDate(from),
				To = LearningRecordPresentation.IsoDate(to),
				CategoryId = categoryId,
				Source = source.HasValue && Enum.IsDefined(typeof(LearningRecordSourceKinds), source.Value) ? source : null,
				Audit = audit,
				CanAudit = canAudit,
				CanReview = !access.Self && canAudit
			});

			model.MemberName = model.NameOf(access.Target);
			model.IsFormerMember = model.IsFormer(access.Target);
			model.Records = await TranscriptRecordsAsync(access.Target, from, to, categoryId, model.Source, audit);
			// Round 7B: completions recorded by an instructor show who recorded them (never as the member's own).
			if (model.Records.Any(r => r.IsRecordedByOther))
			{
				model.Recordings = await _recordings.GetForRecordsAsync(DepartmentId, model.Records) ?? new Dictionary<int, TrainingCompletionRecording>();
				model.RecorderNames = await NamesAsync();
			}

			if (access.Self)
			{
				var settings = await _learningRecords.GetSettingsAsync(DepartmentId) ?? new TrainingRecordSettings();
				model.CanSubmit = !settings.MemberSubmissionsDisabled;
			}

			if (canAudit)
				model.Members = (await MemberOptionsAsync()).Where(m => scope.CanReviewRecordsOf(m.UserId)).ToList();

			if (audit)
				model.Chains = await ChainsAsync(model.Records);

			var counted = model.Records.Where(r => r.Counts).ToList();
			model.TotalHours = counted.Sum(r => r.Hours);
			model.PendingCount = model.Records.Count(r => r.Status == (int)LearningRecordStatuses.Pending);
			model.Totals = counted.GroupBy(r => r.CategoryId).Select(g =>
			{
				var category = model.CategoryOf(g.Key);
				return new TranscriptCategoryTotal
				{
					CategoryId = g.Key,
					Name = category?.Name,
					Hours = g.Sum(r => r.Hours),
					Records = g.Count(),
					TargetHours = category?.TargetHours,
					TargetPeriod = category?.TargetPeriod ?? 0,
					IsRemoved = category?.IsDeleted == true
				};
			}).OrderBy(t => t.Name == null).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

			return View(model);
		}

		/// <summary>The audit view's supersede chains: asked once per chain, at most 50 chains a page.</summary>
		private async Task<Dictionary<int, List<LearningRecord>>> ChainsAsync(List<LearningRecord> records)
		{
			var chains = new Dictionary<int, List<LearningRecord>>();
			var asked = 0;
			foreach (var record in records.Where(r => r.SupersedesId.HasValue || r.IsVoided))
			{
				if (chains.ContainsKey(record.LearningRecordId))
					continue;
				if (++asked > 50)
					break;

				var chain = (await _learningRecords.GetHistoryChainAsync(DepartmentId, record.LearningRecordId) ?? new List<LearningRecord>())
					.Where(c => c != null && c.DepartmentId == DepartmentId).GroupBy(c => c.LearningRecordId).Select(g => g.First())
					.OrderBy(c => c.CreatedOn).ThenBy(c => c.LearningRecordId).ToList();
				if (chain.Count < 2)
					continue;

				foreach (var link in chain)
					chains[link.LearningRecordId] = chain;
			}

			return chains;
		}

		private async Task<TranscriptDocument> TranscriptDocumentAsync(string target, DateTime? from, DateTime? to, int? categoryId, int? source)
		{
			Order(ref from, ref to);
			var department = await DepartmentAsync();
			var names = await NamesAsync();
			var categories = await _learningRecords.GetCategoriesAsync(DepartmentId, true) ?? new List<TrainingCategory>();
			var records = await TranscriptRecordsAsync(target, from, to, categoryId, source, false);
			var recordings = records.Any(r => r.IsRecordedByOther) ? await _recordings.GetForRecordsAsync(DepartmentId, records) ?? new Dictionary<int, TrainingCompletionRecording>()
				: new Dictionary<int, TrainingCompletionRecording>();

			var document = new TranscriptDocument
			{
				Context = await DocumentContextAsync(),
				MemberName = names.TryGetValue(target, out var name) && !string.IsNullOrWhiteSpace(name) ? name : target,
				FromUtc = StartUtc(from, department),
				ToUtc = EndUtc(to, department)
			};

			foreach (var record in records)
			{
				var methodKey = LearningRecordPresentation.MethodKey(record.MethodOfTraining);
				document.Lines.Add(new TranscriptLine
				{
					CompletedOn = DocumentTime(record.CompletedOn, department),
					Title = record.Title,
					CourseCode = record.CourseCode,
					Category = CategoryLabel(record.CategoryId, categories),
					Source = L(LearningRecordPresentation.SourceKey(record.SourceKind)),
					Status = L(LearningRecordPresentation.StatusKey(record.Status)),
					Hours = record.Hours,
					CeHours = record.CeHours,
					CeProvider = record.CeProvider,
					CeCourseNumber = record.CeCourseNumber,
					Method = methodKey == null ? null : L(methodKey),
					Instructors = InstructorsOf(record, names),
					Counts = record.Counts,
					RecordedBy = RecordedByText(record, recordings, names, department),
					// Round 9B: self-reported vs reviewed vs recorded by an instructor (or by the system, or imported).
					Provenance = ReviewText("Provenance." + TrainingSubmissionRules.ProvenanceOf(record))
				});
			}

			foreach (var group in records.Where(r => r.Counts).GroupBy(r => CategoryLabel(r.CategoryId, categories)))
				document.HoursByCategory[group.Key] = group.Sum(r => r.Hours);
			document.TotalHours = records.Where(r => r.Counts).Sum(r => r.Hours);
			return document;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> TranscriptPdf(string userId, DateTime? from, DateTime? to, int? categoryId, int? source)
		{
			var access = await TranscriptAccessAsync(userId);
			if (access.Refusal != null)
				return access.Refusal;

			try
			{
				var document = await TranscriptDocumentAsync(access.Target, from, to, categoryId, source);
				return await SealedPdfFileAsync(SignatureSubjectKinds.TrainingTranscript, access.Target, TranscriptRevisionSha256(access.Target, document, categoryId, source),
					document.MemberName, Resgrid.Services.TrainingDocuments.TranscriptHtml(document), "training-transcript-" + Stamp() + ".pdf", null);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Training transcript PDF could not be generated.");
				Flash(null, L("Rec.ExportFailed"));
				return RedirectToAction("Transcript", new { userId = access.Self ? null : access.Target, from = LearningRecordPresentation.IsoDate(from), to = LearningRecordPresentation.IsoDate(to), categoryId, source });
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> TranscriptCsv(string userId, DateTime? from, DateTime? to, int? categoryId, int? source)
		{
			var access = await TranscriptAccessAsync(userId);
			if (access.Refusal != null)
				return access.Refusal;

			var document = await TranscriptDocumentAsync(access.Target, from, to, categoryId, source);
			return CsvFile(Resgrid.Services.TrainingDocuments.TranscriptCsv(document), "training-transcript-" + Stamp() + ".csv");
		}

		#endregion Transcript

		#region Certificate and evidence

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Certificate(int id)
		{
			var record = await _learningRecords.GetRecordAsync(DepartmentId, id);
			if (record == null || record.DepartmentId != DepartmentId || record.Status != (int)LearningRecordStatuses.Verified)
				return NotFound();

			var scope = await ScopeAsync();
			if (!SameUser(record.UserId, UserId) && !scope.CanReviewRecordsOf(record.UserId))
				return NotFound();

			var department = await DepartmentAsync();
			var names = await NamesAsync();
			var categories = await _learningRecords.GetCategoriesAsync(DepartmentId, true) ?? new List<TrainingCategory>();
			var verifier = record.VerifiedByUserId != null && names.TryGetValue(record.VerifiedByUserId, out var verifierName) && !string.IsNullOrWhiteSpace(verifierName) ? verifierName : null;

			var document = new CertificateDocument
			{
				Context = await DocumentContextAsync(),
				MemberName = names.TryGetValue(record.UserId, out var holder) && !string.IsNullOrWhiteSpace(holder) ? holder : record.UserId,
				Course = record.Title,
				CourseCode = record.CourseCode,
				CompletedOn = DocumentTime(record.CompletedOn, department),
				Hours = record.Hours,
				CeHours = record.CeHours,
				CeProvider = record.CeProvider,
				CeCourseNumber = record.CeCourseNumber,
				Category = record.CategoryId.HasValue ? CategoryLabel(record.CategoryId, categories) : null,
				Instructors = InstructorsOf(record, names),
				IssuedBy = verifier == null ? department?.Name : (department?.Name + " · " + verifier)
			};

			string html = null;
			var lms = false;
			try { lms = await _lmsAccess.CanAuthorAndManageAsync(DepartmentId); }
			catch (Exception ex) { Logging.LogException(ex); }

			// Lapse rule 9 (R-L7): a certificate whose verification code was issued while the add-on was active keeps printing it, with
			// its QR, after the add-on lapses; the public page keeps verifying it. No new code is issued and no custom template is used.
			if (!lms && !string.IsNullOrWhiteSpace(record.VerificationCode))
			{
				try
				{
					if (await _lmsAccess.IsFlagEnabledAsync(DepartmentId))
					{
						document.VerificationCode = record.VerificationCode;
						document.VerificationUrl = (Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/verify/certificate/" + Uri.EscapeDataString(record.VerificationCode);
						try { document.QrImageDataUrl = WebQrCode.DataUrl(document.VerificationUrl); }
						catch (Exception ex) { Logging.LogException(ex); }
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (lms)
			{
				// LMS (dark, §24): a verification code, the public verify URL and its QR, and the department's default template.
				// Any LMS failure prints the standard certificate instead of failing the member's download.
				try
				{
					var code = await _lmsTraining.EnsureVerificationCodeAsync(DepartmentId, record.LearningRecordId);
					if (!string.IsNullOrWhiteSpace(code))
					{
						document.VerificationCode = code;
						document.VerificationUrl = (Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/verify/certificate/" + Uri.EscapeDataString(code);
						try { document.QrImageDataUrl = WebQrCode.DataUrl(document.VerificationUrl); }
						catch (Exception ex) { Logging.LogException(ex); }
					}

					var template = (await _lmsTraining.GetCertificateTemplatesAsync(DepartmentId) ?? new List<LmsCertificateTemplate>())
						.FirstOrDefault(t => t != null && t.IsDefault && t.DepartmentId == DepartmentId && !string.IsNullOrWhiteSpace(t.Body));
					if (template != null)
						html = Resgrid.Services.TrainingDocuments.CustomCertificateHtml(StringHelpers.SanitizeHtmlForStorage(template.Body), document);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "LMS certificate details could not be added; printing the standard certificate.");
					document.VerificationCode = null;
					document.VerificationUrl = null;
					document.QrImageDataUrl = null;
				}
			}

			html ??= Resgrid.Services.TrainingDocuments.CertificateHtml(document);

			try
			{
				return await SealedPdfFileAsync(SignatureSubjectKinds.TrainingCertificate, record.LearningRecordId.ToString(CultureInfo.InvariantCulture),
					CertificateRevisionSha256(record, document.VerificationCode), document.Course, html,
					"certificate-" + record.LearningRecordId.ToString(CultureInfo.InvariantCulture) + ".pdf", null);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Training certificate PDF could not be generated.");
				Flash(null, L("Rec.ExportFailed"));
				return RedirectToAction("Transcript", new { userId = SameUser(record.UserId, UserId) ? null : record.UserId });
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Evidence(int fileId)
		{
			var file = await _learningRecords.GetEvidenceFileAsync(DepartmentId, fileId);
			if (file == null || file.DepartmentId != DepartmentId || file.Data == null)
				return NotFound();

			var scope = await ScopeAsync();
			if (!SameUser(file.UserId, UserId) && !scope.CanReviewRecordsOf(file.UserId))
				return Unauthorized();

			var name = System.IO.Path.GetFileName(file.FileName ?? string.Empty);
			var extension = System.IO.Path.GetExtension(name).TrimStart('.');
			var contentType = EvidenceTypes.TryGetValue(extension, out var type) ? type : "application/octet-stream";
			return File(file.Data, contentType, string.IsNullOrWhiteSpace(name) ? "evidence" : name);
		}

		#endregion Certificate and evidence

		#region Submit outside training

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Submit()
		{
			var department = await DepartmentAsync();
			var settings = await _learningRecords.GetSettingsAsync(DepartmentId) ?? new TrainingRecordSettings();
			var model = await PageAsync(new TrainingRecordSubmitView
			{
				SubmissionsDisabled = settings.MemberSubmissionsDisabled,
				ReviewMode = settings.SubmissionReviewMode,
				Today = LocalToday(department).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			});
			model.Input.CompletedOn = model.Today;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxEvidenceRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxEvidenceRequestBytes)]
		public async Task<IActionResult> Submit(LearningRecordSubmitInput input, List<IFormFile> evidence, int evidenceKind, CancellationToken cancellationToken)
		{
			input ??= new LearningRecordSubmitInput();
			var reviewMode = ((await _learningRecords.GetSettingsAsync(DepartmentId)) ?? new TrainingRecordSettings()).SubmissionReviewMode;
			var department = await DepartmentAsync();
			var today = LocalToday(department);
			var categories = await _learningRecords.GetCategoriesAsync(DepartmentId, false) ?? new List<TrainingCategory>();

			var title = (input.Title ?? string.Empty).Trim();
			if (title.Length == 0)
				ModelState.AddModelError(nameof(input.Title), L("Rec.TitleRequired"));
			else if (title.Length > LearningRecord.MaxTitleLength)
				ModelState.AddModelError(nameof(input.Title), string.Format(L("TextTooLong"), LearningRecord.MaxTitleLength));

			DateTime completed = default;
			if (string.IsNullOrWhiteSpace(input.CompletedOn) || !DateTime.TryParseExact(input.CompletedOn.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out completed))
				ModelState.AddModelError(nameof(input.CompletedOn), L("Rec.DateRequired"));
			else if (completed.Date > today)
				ModelState.AddModelError(nameof(input.CompletedOn), L("Rec.DateInFuture"));

			if (!input.Hours.HasValue || input.Hours.Value <= 0 || input.Hours.Value > 9999)
				ModelState.AddModelError(nameof(input.Hours), L("HoursRange"));
			if (input.CeHours.HasValue && (input.CeHours.Value < 0 || input.CeHours.Value > 9999))
				ModelState.AddModelError(nameof(input.CeHours), L("HoursRange"));
			if (input.CategoryId.HasValue && input.CategoryId.Value > 0 && categories.All(c => c.TrainingCategoryId != input.CategoryId.Value || c.IsDeleted))
				ModelState.AddModelError(nameof(input.CategoryId), L("Rec.CategoryInvalid"));
			if (input.MethodOfTraining.HasValue && !Enum.IsDefined(typeof(MethodsOfTraining), input.MethodOfTraining.Value))
				ModelState.AddModelError(nameof(input.MethodOfTraining), L("Rec.MethodInvalid"));

			var ceProvider = Bounded(input.CeProvider, 250, nameof(input.CeProvider));
			var ceNumber = Bounded(input.CeCourseNumber, 100, nameof(input.CeCourseNumber));
			var notes = Bounded(input.Notes, LearningRecord.MaxNotesLength, nameof(input.Notes));

			// Round 9B: several evidence files (a certificate, photos, a document, a receipt), the kind the member chose for them.
			var (files, fileRefusal) = await TrainingReviewsController.ReadEvidenceAsync(evidence, evidenceKind, cancellationToken);
			if (fileRefusal != null)
				ModelState.AddModelError("evidence", ErrorText(fileRefusal));

			if (ModelState.IsValid)
			{
				var draft = new LearningRecordDraft
				{
					UserId = UserId,
					Title = title,
					CompletedOn = DateTime.SpecifyKind(completed.Date.AddHours(12).DepartmentLocalToUtc(department), DateTimeKind.Utc),
					Hours = Math.Round(input.Hours.Value, 2),
					CategoryId = input.CategoryId.HasValue && input.CategoryId.Value > 0 ? input.CategoryId : null,
					MethodOfTraining = input.MethodOfTraining,
					CeProvider = ceProvider,
					CeCourseNumber = ceNumber,
					CeHours = input.CeHours.HasValue ? Math.Round(input.CeHours.Value, 2) : (decimal?)null,
					Notes = notes,
					Status = LearningRecordStatuses.Pending
				};

				// Round 9B: through the submission service (the review decision, the history, the notices and the workflow triggers).
				var outcome = await _submissions.SubmitAsync(DepartmentId, UserId, draft, files, cancellationToken);
				if (outcome?.Record != null && outcome.Refusal == null)
				{
					TempData["TrainingReviewsMessage"] = ReviewText(outcome.ReviewRequired ? "Message.Submitted" : "Message.Accepted");
					return RedirectToAction("Submission", "TrainingReviews", new { id = outcome.Record.LearningRecordId });
				}

				if (outcome?.Refusal == "learning_record_submissions_disabled")
				{
					// The department switched member submissions off.
					var disabled = await PageAsync(new TrainingRecordSubmitView { Input = input, SubmissionsDisabled = true, ReviewMode = reviewMode, Today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
					return View(disabled);
				}

				ModelState.AddModelError(string.Empty, ErrorText(outcome?.Refusal));
			}

			var model = await PageAsync(new TrainingRecordSubmitView { Input = input, ReviewMode = reviewMode, Today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
			return View(model);
		}

		private string Bounded(string value, int max, string field)
		{
			var trimmed = value?.Trim();
			if (string.IsNullOrEmpty(trimmed))
				return null;
			if (trimmed.Length > max)
			{
				ModelState.AddModelError(field, string.Format(L("TextTooLong"), max));
				return trimmed.Substring(0, max);
			}

			return trimmed;
		}

		#endregion Submit outside training

		#region Queue and officer actions

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Queue()
		{
			if (!await CanReviewAnyAsync())
				return Unauthorized();

			var scope = await ScopeAsync();
			var model = await PageAsync(new TrainingRecordQueueView { CurrentUserId = UserId }, "Queue");

			// Round 9B: members' hours submissions are reviewed in Training reviews (evidence inline, more information, bulk approve); the
			// queue keeps open check-ins and the other pending records, and says how many submissions wait there.
			model.Pending = (await _learningRecords.GetPendingAsync(DepartmentId) ?? new List<LearningRecord>())
				.Where(r => r != null && r.DepartmentId == DepartmentId && r.Status == (int)LearningRecordStatuses.Pending && scope.CanReviewRecordsOf(r.UserId) &&
					!TrainingSubmissionRules.IsSubmission(r))
				.OrderBy(r => r.CreatedOn).ToList();
			model.SubmissionsAwaiting = await _submissions.CountAwaitingAsync(DepartmentId, UserId);

			var duplicates = (await _learningRecords.QueryAsync(new LearningRecordQuery { DepartmentId = DepartmentId, UnresolvedDuplicatesOnly = true, Take = 500 }) ?? new List<LearningRecord>())
				.Where(r => r != null && r.DepartmentId == DepartmentId && r.HasUnresolvedDuplicate && scope.CanReviewRecordsOf(r.UserId))
				.OrderByDescending(r => r.CompletedOn).ToList();

			var others = new Dictionary<int, LearningRecord>();
			foreach (var otherId in duplicates.Select(r => r.PossibleDuplicateOfId.Value).Distinct().Take(200))
			{
				var other = await _learningRecords.GetRecordAsync(DepartmentId, otherId);
				if (other != null && other.DepartmentId == DepartmentId)
					others[otherId] = other;
			}

			model.Duplicates = duplicates.Select(r => new TrainingRecordDuplicateRow
			{
				Record = r,
				Other = others.TryGetValue(r.PossibleDuplicateOfId.Value, out var other) ? other : null
			}).ToList();

			return View(model);
		}

		/// <summary>The record an officer acts on: in the department, and its member reviewable by them.</summary>
		private async Task<(LearningRecord Record, IActionResult Refusal)> ReviewableAsync(int id)
		{
			var record = await _learningRecords.GetRecordAsync(DepartmentId, id);
			if (record == null || record.DepartmentId != DepartmentId)
				return (null, NotFound());

			var scope = await ScopeAsync();
			if (!scope.CanReviewRecordsOf(record.UserId))
				return (record, Unauthorized());

			return (record, null);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Verify(int id, string note, decimal? hours, CancellationToken cancellationToken)
		{
			var (record, refusal) = await ReviewableAsync(id);
			if (refusal != null)
				return refusal;

			if (hours.HasValue && (hours.Value < 0 || hours.Value > 9999))
			{
				Flash(null, L("HoursRange"));
				return RedirectToAction("Queue");
			}

			if (TrainingSubmissionRules.IsSubmission(record))
			{
				// Round 9B: a member submission is decided through the review service (the member is told, the history is kept).
				var approved = await _submissions.ApproveAsync(DepartmentId, record.LearningRecordId, UserId, Note(note), hours.HasValue ? Math.Round(hours.Value, 2) : (decimal?)null, cancellationToken);
				Flash(approved?.Refusal == null ? L("Rec.VerifiedMessage") : null, approved?.Refusal != null ? ErrorText(approved.Refusal) : null);
				return RedirectToAction("Queue");
			}

			try
			{
				var verified = await _learningRecords.VerifyAsync(DepartmentId, record.LearningRecordId, UserId, Note(note), hours.HasValue ? Math.Round(hours.Value, 2) : (decimal?)null, cancellationToken);
				Flash(verified != null ? L("Rec.VerifiedMessage") : null, verified == null ? L("Rec.ActionFailed") : null);
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Queue");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Reject(int id, string note, CancellationToken cancellationToken)
		{
			var (record, refusal) = await ReviewableAsync(id);
			if (refusal != null)
				return refusal;

			if (string.IsNullOrWhiteSpace(note))
			{
				Flash(null, L("Rec.RejectNoteRequired"));
				return RedirectToAction("Queue");
			}

			if (TrainingSubmissionRules.IsSubmission(record))
			{
				var denied = await _submissions.DenyAsync(DepartmentId, record.LearningRecordId, UserId, Note(note), cancellationToken);
				Flash(denied?.Refusal == null ? L("Rec.RejectedMessage") : null, denied?.Refusal != null ? ErrorText(denied.Refusal) : null);
				return RedirectToAction("Queue");
			}

			try
			{
				var rejected = await _learningRecords.RejectAsync(DepartmentId, record.LearningRecordId, UserId, Note(note), cancellationToken);
				Flash(rejected != null ? L("Rec.RejectedMessage") : null, rejected == null ? L("Rec.ActionFailed") : null);
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Queue");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Void(int id, string reason, bool fromTranscript = false, CancellationToken cancellationToken = default)
		{
			var (record, refusal) = await ReviewableAsync(id);
			if (refusal != null)
				return refusal;

			IActionResult Back() => fromTranscript
				? RedirectToAction("Transcript", new { userId = record.UserId, audit = true })
				: RedirectToAction("Queue");

			if (SameUser(record.UserId, UserId))
			{
				Flash(null, ErrorText("learning_record_self_review"));
				return Back();
			}

			if (string.IsNullOrWhiteSpace(reason))
			{
				Flash(null, L("Rec.VoidReasonRequired"));
				return Back();
			}

			try
			{
				var voided = await _learningRecords.VoidAsync(DepartmentId, record.LearningRecordId, UserId, Note(reason), cancellationToken);
				Flash(voided ? L("Rec.VoidedMessage") : null, voided ? null : L("Rec.ActionFailed"));
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return Back();
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ResolveDuplicate(int id, int resolution, CancellationToken cancellationToken)
		{
			var (record, refusal) = await ReviewableAsync(id);
			if (refusal != null)
				return refusal;

			if (!Enum.IsDefined(typeof(LearningRecordDuplicateResolutions), resolution))
			{
				Flash(null, L("Rec.ActionFailed"));
				return RedirectToAction("Queue");
			}

			try
			{
				var resolved = await _learningRecords.ResolveDuplicateAsync(DepartmentId, record.LearningRecordId, (LearningRecordDuplicateResolutions)resolution, UserId, cancellationToken);
				Flash(resolved != null ? L("Rec.DuplicateResolvedMessage") : null, resolved == null ? L("Rec.ActionFailed") : null);
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Queue");
		}

		private static string Note(string value)
		{
			var trimmed = value?.Trim();
			if (string.IsNullOrEmpty(trimmed))
				return null;
			return trimmed.Length > 1000 ? trimmed.Substring(0, 1000) : trimmed;
		}

		#endregion Queue and officer actions

		#region Categories

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Categories(int? id)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var model = await CategoriesPageAsync(null);
			if (id.HasValue && id.Value > 0)
			{
				var category = model.LiveCategories.FirstOrDefault(c => c.TrainingCategoryId == id.Value);
				if (category == null)
					return NotFound();
				model.Input = new TrainingCategoryInput
				{
					TrainingCategoryId = category.TrainingCategoryId,
					Name = category.Name,
					Code = category.Code,
					Kind = category.Kind,
					TargetHours = category.TargetHours,
					TargetPeriod = category.TargetPeriod,
					TargetAppliesTo = category.TargetAppliesTo,
					ExternalCode = category.ExternalCode,
					RetentionYears = category.RetentionYears,
					Description = category.Description
				};
			}

			return View("Categories", model);
		}

		private async Task<TrainingCategoriesView> CategoriesPageAsync(TrainingCategoryInput input)
		{
			// Round 9C: templates in the reader's language; the ones the operating profile suggests come first (nothing is hidden).
			var profile = await _departmentSettingsService.GetOperatingProfileAsync(DepartmentId);
			var templates = TrainingCategoryTemplateCatalog.AllFor(CultureInfo.CurrentUICulture.Name);
			var model = await PageAsync(new TrainingCategoriesView
			{
				Templates = SeedSectorRecommendations.SuggestedFirst(templates, SeedContentLocalization.CategoryTemplates, t => t.Key, profile),
				Suggested = SeedSectorRecommendations.Suggested(SeedContentLocalization.CategoryTemplates, templates.Select(t => t.Key), profile)
			}, "Categories");
			model.Input = input ?? new TrainingCategoryInput();
			model.Codes = new HashSet<string>(model.LiveCategories.Where(c => !string.IsNullOrWhiteSpace(c.Code)).Select(c => c.Code), StringComparer.OrdinalIgnoreCase);
			return model;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveCategory(TrainingCategoryInput input, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			input ??= new TrainingCategoryInput();
			var categories = await _learningRecords.GetCategoriesAsync(DepartmentId, false) ?? new List<TrainingCategory>();
			TrainingCategory category;
			if (input.TrainingCategoryId > 0)
			{
				category = categories.FirstOrDefault(c => c.TrainingCategoryId == input.TrainingCategoryId && c.DepartmentId == DepartmentId && !c.IsDeleted);
				if (category == null)
					return NotFound();
			}
			else
			{
				category = new TrainingCategory { DepartmentId = DepartmentId, Kind = (int)TrainingCategoryKinds.Custom };
			}

			var name = (input.Name ?? string.Empty).Trim();
			if (name.Length == 0)
				ModelState.AddModelError(nameof(input.Name), L("NameRequired"));
			name = Bounded(name, 200, nameof(input.Name));
			var code = Bounded(input.Code, 50, nameof(input.Code));
			if (code != null && categories.Any(c => c.TrainingCategoryId != category.TrainingCategoryId && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
				ModelState.AddModelError(nameof(input.Code), L("Rec.CategoryCodeTaken"));
			if (!Enum.IsDefined(typeof(TrainingCategoryKinds), input.Kind))
				ModelState.AddModelError(nameof(input.Kind), L("Rec.ActionFailed"));
			if (!Enum.IsDefined(typeof(TrainingTargetPeriods), input.TargetPeriod))
				ModelState.AddModelError(nameof(input.TargetPeriod), L("Rec.ActionFailed"));
			if (input.TargetHours.HasValue && (input.TargetHours.Value < 0 || input.TargetHours.Value > 9999))
				ModelState.AddModelError(nameof(input.TargetHours), L("HoursRange"));
			if (input.RetentionYears.HasValue && (input.RetentionYears.Value < 0 || input.RetentionYears.Value > 75))
				ModelState.AddModelError(nameof(input.RetentionYears), L("Rec.RetentionRange"));
			var appliesTo = Bounded(input.TargetAppliesTo, 200, nameof(input.TargetAppliesTo));
			var externalCode = Bounded(input.ExternalCode, 64, nameof(input.ExternalCode));
			var description = Bounded(input.Description, 1000, nameof(input.Description));

			if (!ModelState.IsValid)
				return View("Categories", await CategoriesPageAsync(input));

			category.Name = name;
			category.Code = code;
			category.Kind = input.Kind;
			category.TargetHours = input.TargetHours.HasValue ? Math.Round(input.TargetHours.Value, 2) : (decimal?)null;
			category.TargetPeriod = input.TargetHours.HasValue ? input.TargetPeriod : (int)TrainingTargetPeriods.None;
			category.TargetAppliesTo = appliesTo;
			category.ExternalCode = externalCode;
			category.RetentionYears = input.RetentionYears;
			category.Description = description;

			try
			{
				var saved = await _learningRecords.SaveCategoryAsync(category, UserId, cancellationToken);
				Flash(saved != null ? L("Rec.CategorySavedMessage") : null, saved == null ? L("Rec.ActionFailed") : null);
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				ModelState.AddModelError(string.Empty, ErrorText(ex.Message));
				return View("Categories", await CategoriesPageAsync(input));
			}

			return RedirectToAction("Categories");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteCategory(int id, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			try
			{
				var deleted = await _learningRecords.DeleteCategoryAsync(DepartmentId, id, UserId, cancellationToken);
				Flash(deleted ? L("Rec.CategoryDeletedMessage") : null, deleted ? null : L("Rec.ActionFailed"));
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Categories");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ApplyTemplate(string key, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var template = TrainingCategoryTemplateCatalog.Find(key);
			if (template == null)
				return NotFound();

			try
			{
				var added = await _learningRecords.ApplyCategoryTemplateAsync(DepartmentId, template.Key, UserId, CultureInfo.CurrentUICulture.Name, cancellationToken);
				Flash(string.Format(L("Rec.TemplateAppliedMessage"), added));
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Categories");
		}

		#endregion Categories

		#region Bulk categorize

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Categorize(DateTime? from, DateTime? to)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			Order(ref from, ref to);
			var department = await DepartmentAsync();
			var model = await PageAsync(new TrainingCategorizeView
			{
				From = LearningRecordPresentation.IsoDate(from),
				To = LearningRecordPresentation.IsoDate(to)
			}, "Categorize");
			model.Groups = (await _learningRecords.GetUncategorizedGroupsAsync(DepartmentId, StartUtc(from, department), EndUtc(to, department)) ?? new List<LearningRecordGroup>())
				.OrderByDescending(g => g.Count).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
			return View(model);
		}

		/// <summary>Bulk categorize one group (R-T3). The window comes in the query string so dates bind culture-free.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Categorize(int categoryId, string title, string courseCode, DateTime? from, DateTime? to, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			Order(ref from, ref to);
			var back = new { from = LearningRecordPresentation.IsoDate(from), to = LearningRecordPresentation.IsoDate(to) };
			var categories = await _learningRecords.GetCategoriesAsync(DepartmentId, false) ?? new List<TrainingCategory>();
			if (categories.All(c => c.TrainingCategoryId != categoryId || c.IsDeleted))
			{
				Flash(null, L("Rec.CategoryInvalid"));
				return RedirectToAction("Categorize", back);
			}

			title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
			courseCode = string.IsNullOrWhiteSpace(courseCode) ? null : courseCode.Trim();
			if (title == null && courseCode == null)
			{
				Flash(null, L("Rec.CategorizeNeedsGroup"));
				return RedirectToAction("Categorize", back);
			}

			try
			{
				var department = await DepartmentAsync();
				var count = await _learningRecords.CategorizeAsync(DepartmentId, categoryId, title, courseCode, StartUtc(from, department), EndUtc(to, department), null, UserId, cancellationToken);
				Flash(string.Format(L("Rec.CategorizedMessage"), count));
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Categorize", back);
		}

		#endregion Bulk categorize

		#region Credit mappings and history

		private async Task<List<DepartmentCertificationType>> PersonCertificationTypesAsync()
		{
			return (await _certificationService.GetAllCertificationTypesByDepartmentAsync(DepartmentId) ?? new List<DepartmentCertificationType>())
				.Where(t => t != null && t.DepartmentId == DepartmentId && !t.IsDeleted && !t.IsUnitScoped)
				.OrderBy(t => t.Type, StringComparer.CurrentCultureIgnoreCase).ToList();
		}

		private async Task<TrainingCreditsView> CreditsPageAsync(CategoryCreditMappingInput input)
		{
			var model = await PageAsync(new TrainingCreditsView { CanEdit = CanManageCertifications }, "Credits");
			model.Types = await PersonCertificationTypesAsync();
			model.Mappings = (await _learningRecords.GetCreditMappingsAsync(DepartmentId) ?? new List<CategoryCreditMapping>())
				.Where(m => m != null && m.DepartmentId == DepartmentId).ToList();
			model.Input = input ?? new CategoryCreditMappingInput();
			return model;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Credits(int? id)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var model = await CreditsPageAsync(null);
			if (id.HasValue && id.Value > 0 && model.CanEdit)
			{
				var mapping = model.Mappings.FirstOrDefault(m => m.CategoryCreditMappingId == id.Value);
				if (mapping == null)
					return NotFound();
				model.Input = new CategoryCreditMappingInput
				{
					CategoryCreditMappingId = mapping.CategoryCreditMappingId,
					DepartmentCertificationTypeId = mapping.DepartmentCertificationTypeId,
					CategoryId = mapping.CategoryId,
					ComponentKey = mapping.ComponentKey,
					IsEnabled = mapping.IsEnabled
				};
			}

			return View("Credits", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveMapping(CategoryCreditMappingInput input, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords || !CanManageCertifications)
				return Unauthorized();

			input ??= new CategoryCreditMappingInput();
			var types = await PersonCertificationTypesAsync();
			var categories = await _learningRecords.GetCategoriesAsync(DepartmentId, false) ?? new List<TrainingCategory>();
			var mappings = (await _learningRecords.GetCreditMappingsAsync(DepartmentId) ?? new List<CategoryCreditMapping>()).Where(m => m != null && m.DepartmentId == DepartmentId).ToList();

			CategoryCreditMapping mapping;
			if (input.CategoryCreditMappingId > 0)
			{
				mapping = mappings.FirstOrDefault(m => m.CategoryCreditMappingId == input.CategoryCreditMappingId);
				if (mapping == null)
					return NotFound();
			}
			else
			{
				mapping = new CategoryCreditMapping { DepartmentId = DepartmentId };
			}

			if (types.All(t => t.DepartmentCertificationTypeId != input.DepartmentCertificationTypeId))
				ModelState.AddModelError(nameof(input.DepartmentCertificationTypeId), L("Rec.CertificationTypeInvalid"));
			if (categories.All(c => c.TrainingCategoryId != input.CategoryId || c.IsDeleted))
				ModelState.AddModelError(nameof(input.CategoryId), L("Rec.CategoryInvalid"));
			var component = Bounded(input.ComponentKey, 100, nameof(input.ComponentKey));
			if (mappings.Any(m => m.CategoryCreditMappingId != mapping.CategoryCreditMappingId && m.DepartmentCertificationTypeId == input.DepartmentCertificationTypeId
				&& m.CategoryId == input.CategoryId && string.Equals(m.ComponentKey ?? string.Empty, component ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
				ModelState.AddModelError(nameof(input.CategoryId), L("Rec.MappingExists"));

			if (!ModelState.IsValid)
				return View("Credits", await CreditsPageAsync(input));

			mapping.DepartmentCertificationTypeId = input.DepartmentCertificationTypeId;
			mapping.CategoryId = input.CategoryId;
			mapping.ComponentKey = component;
			mapping.IsEnabled = input.IsEnabled;

			try
			{
				var saved = await _learningRecords.SaveCreditMappingAsync(mapping, UserId, cancellationToken);
				Flash(saved != null ? L("Rec.MappingSavedMessage") : null, saved == null ? L("Rec.ActionFailed") : null);
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				ModelState.AddModelError(string.Empty, ErrorText(ex.Message));
				return View("Credits", await CreditsPageAsync(input));
			}

			return RedirectToAction("Credits");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteMapping(int id, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords || !CanManageCertifications)
				return Unauthorized();

			try
			{
				var deleted = await _learningRecords.DeleteCreditMappingAsync(DepartmentId, id, UserId, cancellationToken);
				Flash(deleted ? L("Rec.MappingDeletedMessage") : null, deleted ? null : L("Rec.ActionFailed"));
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Credits");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ApplyHistory(int typeId)
		{
			if (!(await ScopeAsync()).CanManageRecords || !CanManageCertifications)
				return Unauthorized();

			var type = (await PersonCertificationTypesAsync()).FirstOrDefault(t => t.DepartmentCertificationTypeId == typeId);
			if (type == null)
				return NotFound();

			var model = await PageAsync(new TrainingApplyHistoryView { Type = type }, "Credits");
			model.Preview = await _learningRecords.PreviewHistoryCreditsAsync(DepartmentId, typeId) ?? new HistoryCreditPreview { DepartmentCertificationTypeId = typeId };
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ApplyHistory(int typeId, int[] recordIds, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords || !CanManageCertifications)
				return Unauthorized();

			var type = (await PersonCertificationTypesAsync()).FirstOrDefault(t => t.DepartmentCertificationTypeId == typeId);
			if (type == null)
				return NotFound();

			// Nothing ticked writes nothing (the service reads null as "every proposal").
			var ids = (recordIds ?? Array.Empty<int>()).Where(i => i > 0).Distinct().Take(10000).ToList();
			if (ids.Count == 0)
			{
				Flash(null, L("Rec.ApplyHistoryNoneSelected"));
				return RedirectToAction("ApplyHistory", new { typeId });
			}

			try
			{
				var written = await _learningRecords.ApplyHistoryCreditsAsync(DepartmentId, typeId, ids, UserId, cancellationToken);
				Flash(string.Format(L("Rec.ApplyHistoryAppliedMessage"), written));
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
				return RedirectToAction("ApplyHistory", new { typeId });
			}

			return RedirectToAction("Credits");
		}

		#endregion Credit mappings and history

		#region Backfill

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Backfill()
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var model = await PageAsync(new TrainingBackfillView(), "Backfill");
			model.Latest = await _learningRecords.GetLatestBackfillAsync(DepartmentId);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RequestBackfill(bool dryRun, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			try
			{
				var job = await _learningRecords.RequestBackfillAsync(DepartmentId, dryRun, UserId, cancellationToken);
				Flash(job != null ? L(dryRun ? "Rec.BackfillDryRunQueuedMessage" : "Rec.BackfillQueuedMessage") : null, job == null ? L("Rec.ActionFailed") : null);
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Backfill");
		}

		#endregion Backfill

		#region History import

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Import()
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			return View("Import", await ImportPageAsync("upload", null, null, null));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ImportTemplate()
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			return File(_historyImport.Template() ?? Array.Empty<byte>(), "text/csv", "training-history-template.csv");
		}

		/// <summary>The dry run: a new upload, or the carried file re-checked with the officer's member picks.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxImportRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxImportRequestBytes, ValueLengthLimit = MaxImportRequestBytes)]
		public async Task<IActionResult> ImportPreview(IFormFile file, string fileData, string fileName, IFormCollection form)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var (bytes, name, error) = await ImportFileAsync(file, fileData, fileName);
			if (error != null)
				return View("Import", await ImportPageAsync("upload", null, null, null, error));

			var overrides = await OverridesAsync(form);
			var result = await _historyImport.DryRunAsync(DepartmentId, bytes, overrides) ?? new TrainingHistoryImportResult { DryRun = true };
			if (!string.IsNullOrWhiteSpace(result.FileError))
				return View("Import", await ImportPageAsync("upload", null, null, null, ErrorText(result.FileError)));

			return View("Import", await ImportPageAsync("preview", result, Convert.ToBase64String(bytes), name));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxImportRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxImportRequestBytes, ValueLengthLimit = MaxImportRequestBytes)]
		public async Task<IActionResult> ImportCommit(string fileData, string fileName, IFormCollection form, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var (bytes, name, error) = await ImportFileAsync(null, fileData, fileName);
			if (error != null)
				return View("Import", await ImportPageAsync("upload", null, null, null, error));

			try
			{
				var overrides = await OverridesAsync(form);
				var result = await _historyImport.ImportAsync(DepartmentId, bytes, overrides, UserId, cancellationToken) ?? new TrainingHistoryImportResult();
				if (!string.IsNullOrWhiteSpace(result.FileError))
					return View("Import", await ImportPageAsync("upload", null, null, null, ErrorText(result.FileError)));

				var model = await ImportPageAsync("result", result, null, name);
				model.Message = string.Format(L("Rec.ImportDoneMessage"), result.RecordsWritten);
				return View("Import", model);
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				return View("Import", await ImportPageAsync("upload", null, null, null, ErrorText(ex.Message)));
			}
		}

		private async Task<TrainingImportView> ImportPageAsync(string stage, TrainingHistoryImportResult result, string fileData, string fileName, string error = null)
		{
			var model = await PageAsync(new TrainingImportView
			{
				Stage = stage,
				Result = result,
				FileData = fileData,
				FileName = fileName,
				MaxMegabytes = MaxImportBytes / (1024 * 1024),
				Error = error
			}, "Import");
			if (stage == "preview")
				model.Members = await MemberOptionsAsync();
			return model;
		}

		private async Task<(byte[] Bytes, string Name, string Error)> ImportFileAsync(IFormFile file, string fileData, string fileName)
		{
			if (file != null && file.Length > 0)
			{
				if (file.Length > MaxImportBytes)
					return (null, null, string.Format(L("Rec.ImportFileTooLarge"), MaxImportBytes / (1024 * 1024)));
				var extension = System.IO.Path.GetExtension(file.FileName ?? string.Empty).TrimStart('.');
				if (!string.Equals(extension, "csv", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, "txt", StringComparison.OrdinalIgnoreCase))
					return (null, null, L("Rec.ImportFileType"));

				using var stream = file.OpenReadStream();
				using var memory = new System.IO.MemoryStream();
				await stream.CopyToAsync(memory);
				return (memory.ToArray(), System.IO.Path.GetFileName(file.FileName), null);
			}

			if (string.IsNullOrWhiteSpace(fileData))
				return (null, null, L("Rec.ImportFileRequired"));

			// Base64 is 4/3 of the file: refuse a carried value larger than the file limit allows before decoding it.
			if (fileData.Length > (MaxImportBytes / 3 + 1) * 4)
				return (null, null, string.Format(L("Rec.ImportFileTooLarge"), MaxImportBytes / (1024 * 1024)));

			try
			{
				var bytes = Convert.FromBase64String(fileData.Trim());
				if (bytes.Length == 0)
					return (null, null, L("Rec.ImportFileRequired"));
				if (bytes.Length > MaxImportBytes)
					return (null, null, string.Format(L("Rec.ImportFileTooLarge"), MaxImportBytes / (1024 * 1024)));
				var name = string.IsNullOrWhiteSpace(fileName) ? null : System.IO.Path.GetFileName(fileName.Trim());
				return (bytes, name != null && name.Length > 200 ? name.Substring(0, 200) : name, null);
			}
			catch (FormatException)
			{
				return (null, null, L("Rec.ImportFileRequired"));
			}
		}

		/// <summary>member_&lt;rowNumber&gt; picks from the review screen, kept only for members of the department (former ones included).</summary>
		private async Task<Dictionary<int, string>> OverridesAsync(IFormCollection form)
		{
			var overrides = new Dictionary<int, string>();
			if (form == null)
				return overrides;

			var members = await MemberOptionsAsync();
			foreach (var key in form.Keys)
			{
				var match = MemberOverrideKey.Match(key ?? string.Empty);
				if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var row))
					continue;

				var value = form[key].ToString().Trim();
				var member = value.Length == 0 ? null : members.FirstOrDefault(m => SameUser(m.UserId, value));
				if (member != null)
					overrides[row] = member.UserId;
			}

			return overrides;
		}

		#endregion History import

		#region Hours report

		private async Task<TrainingHoursReportQuery> HoursQueryAsync(DateTime? from, DateTime? to, int groupBy, int? categoryId, bool former, bool onDuty)
		{
			var department = await DepartmentAsync();
			var today = LocalToday(department);
			from ??= new DateTime(today.Year, 1, 1);
			to ??= today;
			Order(ref from, ref to);
			return new TrainingHoursReportQuery
			{
				DepartmentId = DepartmentId,
				From = StartUtc(from, department).Value,
				To = EndUtc(to, department).Value,
				GroupBy = Enum.IsDefined(typeof(TrainingHoursGroupings), groupBy) ? (TrainingHoursGroupings)groupBy : TrainingHoursGroupings.Category,
				CategoryIds = categoryId.HasValue ? new List<int> { categoryId.Value } : null,
				IncludeFormerMembers = former,
				OnDutyNow = onDuty
			};
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Hours(DateTime? from, DateTime? to, int groupBy = 0, int? categoryId = null, bool former = false, bool onDuty = false)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var department = await DepartmentAsync();
			var today = LocalToday(department);
			from ??= new DateTime(today.Year, 1, 1);
			to ??= today;
			Order(ref from, ref to);
			var query = await HoursQueryAsync(from, to, groupBy, categoryId, former, onDuty);

			var model = await PageAsync(new TrainingHoursView
			{
				From = LearningRecordPresentation.IsoDate(from),
				To = LearningRecordPresentation.IsoDate(to),
				GroupBy = (int)query.GroupBy,
				CategoryId = categoryId,
				Former = former,
				OnDuty = onDuty
			}, "Hours");
			model.Report = await _learningRecords.GetHoursReportAsync(query) ?? new TrainingHoursReport { Query = query };
			return View(model);
		}

		private async Task<HoursReportDocument> HoursDocumentAsync(DateTime? from, DateTime? to, int groupBy, int? categoryId, bool former, bool onDuty)
		{
			var query = await HoursQueryAsync(from, to, groupBy, categoryId, former, onDuty);
			var report = await _learningRecords.GetHoursReportAsync(query) ?? new TrainingHoursReport { Query = query };
			report.Query ??= query;
			return new HoursReportDocument { Context = await DocumentContextAsync(), Report = report, GroupTitle = L("Grouping." + query.GroupBy) };
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> HoursCsv(DateTime? from, DateTime? to, int groupBy = 0, int? categoryId = null, bool former = false, bool onDuty = false)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var document = await HoursDocumentAsync(from, to, groupBy, categoryId, former, onDuty);
			return CsvFile(Resgrid.Services.TrainingDocuments.HoursReportCsv(document), "training-hours-" + Stamp() + ".csv");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> HoursPdf(DateTime? from, DateTime? to, int groupBy = 0, int? categoryId = null, bool former = false, bool onDuty = false)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			try
			{
				var document = await HoursDocumentAsync(from, to, groupBy, categoryId, former, onDuty);
				return PdfFile(Resgrid.Services.TrainingDocuments.HoursReportHtml(document), "training-hours-" + Stamp() + ".pdf");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Training hours PDF could not be generated.");
				Flash(null, L("Rec.ExportFailed"));
				return RedirectToAction("Hours", new { from = LearningRecordPresentation.IsoDate(from), to = LearningRecordPresentation.IsoDate(to), groupBy, categoryId, former, onDuty });
			}
		}

		#endregion Hours report

		#region Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Settings()
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var model = await PageAsync(new TrainingRecordSettingsView(), "Settings");
			model.Settings = (await _learningRecords.GetSettingsAsync(DepartmentId) ?? new TrainingRecordSettings()).Normalized();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Settings(int hoursRoundingMinutes, int hoursRoundingMode, bool memberSubmissionsDisabled, int defaultRetentionYears,
			int submissionReviewMode, int[] reviewCategoryIds, decimal? reviewAboveHours, bool roleReviewersOwnGroupOnly, int reviewerNotices, CancellationToken cancellationToken)
		{
			if (!(await ScopeAsync()).CanManageRecords)
				return Unauthorized();

			var settings = new TrainingRecordSettings
			{
				HoursRoundingMinutes = hoursRoundingMinutes,
				HoursRoundingMode = hoursRoundingMode,
				MemberSubmissionsDisabled = memberSubmissionsDisabled,
				DefaultRetentionYears = defaultRetentionYears,
				// Round 9B: the optional review of members' hours submissions
				SubmissionReviewMode = submissionReviewMode,
				ReviewCategoryIds = (reviewCategoryIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList(),
				ReviewAboveHours = reviewAboveHours ?? 0m,
				RoleReviewersOwnGroupOnly = roleReviewersOwnGroupOnly,
				ReviewerNotices = reviewerNotices
			};

			if (!Enum.IsDefined(typeof(TrainingSubmissionReviewModes), submissionReviewMode))
				ModelState.AddModelError("SubmissionReviewMode", ReviewText("Settings.ModeInvalid"));
			if (!Enum.IsDefined(typeof(TrainingReviewerNoticeModes), reviewerNotices))
				ModelState.AddModelError("ReviewerNotices", ReviewText("Settings.ModeInvalid"));
			if (reviewAboveHours.HasValue && (reviewAboveHours.Value < 0 || reviewAboveHours.Value > TrainingRecordSettings.MaxReviewAboveHours))
				ModelState.AddModelError("ReviewAboveHours", L("HoursRange"));
			var liveCategories = (await _learningRecords.GetCategoriesAsync(DepartmentId, false) ?? new List<TrainingCategory>()).Select(c => c.TrainingCategoryId).ToHashSet();
			settings.ReviewCategoryIds = settings.ReviewCategoryIds.Where(liveCategories.Contains).ToList();
			if (submissionReviewMode == (int)TrainingSubmissionReviewModes.Selected && settings.ReviewCategoryIds.Count == 0 && settings.ReviewAboveHours <= 0)
				ModelState.AddModelError("ReviewCategoryIds", ReviewText("Settings.SelectedRequired"));

			if (!TrainingRecordSettings.AllowedRoundingMinutes.Contains(hoursRoundingMinutes))
				ModelState.AddModelError("HoursRoundingMinutes", L("Rec.RoundingInvalid"));
			if (!Enum.IsDefined(typeof(TrainingHoursRoundingModes), hoursRoundingMode))
				ModelState.AddModelError("HoursRoundingMode", L("Rec.RoundingInvalid"));
			if (defaultRetentionYears < 0 || defaultRetentionYears > 75)
				ModelState.AddModelError("DefaultRetentionYears", L("Rec.RetentionRange"));

			if (!ModelState.IsValid)
			{
				var invalid = await PageAsync(new TrainingRecordSettingsView { Settings = settings }, "Settings");
				return View(invalid);
			}

			try
			{
				await _learningRecords.SaveSettingsAsync(DepartmentId, settings.Normalized(), UserId, cancellationToken);
				Flash(L("Rec.SettingsSavedMessage"));
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("Settings");
		}

		#endregion Settings

		#region LMS (dark, §24)

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> IsoReport(DateTime? from, DateTime? to, bool former = false, string format = null)
		{
			if (!await LmsAllowedAsync())
				return NotFound();

			var department = await DepartmentAsync();
			var today = LocalToday(department);
			from ??= new DateTime(today.Year, 1, 1);
			to ??= today;
			Order(ref from, ref to);

			var report = await _lmsTraining.GetIsoFsrsReportAsync(DepartmentId, StartUtc(from, department).Value, EndUtc(to, department).Value, former) ?? new IsoFsrsReport();

			if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
				return CsvFile(Resgrid.Services.TrainingDocuments.IsoFsrsReportCsv(report, await DocumentContextAsync()), "iso-fsrs-training-" + Stamp() + ".csv");
			if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
				return PdfFile(Resgrid.Services.TrainingDocuments.IsoFsrsReportHtml(report, await DocumentContextAsync()), "iso-fsrs-training-" + Stamp() + ".pdf");

			var model = await PageAsync(new TrainingIsoReportView
			{
				From = LearningRecordPresentation.IsoDate(from),
				To = LearningRecordPresentation.IsoDate(to),
				Former = former,
				Report = report
			}, "IsoReport");
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> NccpReport(bool former = false, string format = null)
		{
			if (!await LmsAllowedAsync())
				return NotFound();

			var report = await _lmsTraining.GetNccpProgressAsync(DepartmentId, former, DateTime.UtcNow) ?? new NccpProgressReport();

			if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
				return CsvFile(Resgrid.Services.TrainingDocuments.NccpProgressCsv(report, await DocumentContextAsync()), "nccp-progress-" + Stamp() + ".csv");
			if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
				return PdfFile(Resgrid.Services.TrainingDocuments.NccpProgressHtml(report, await DocumentContextAsync()), "nccp-progress-" + Stamp() + ".pdf");

			var model = await PageAsync(new TrainingNccpReportView { Former = former, Report = report }, "NccpReport");
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CertificateTemplates(int? id)
		{
			if (!await LmsAllowedAsync())
				return NotFound();

			var model = await TemplatesPageAsync(null);
			if (id.HasValue && id.Value > 0)
			{
				var template = model.Templates.FirstOrDefault(t => t.LmsCertificateTemplateId == id.Value);
				if (template == null)
					return NotFound();
				model.Input = new CertificateTemplateInput
				{
					LmsCertificateTemplateId = template.LmsCertificateTemplateId,
					Name = template.Name,
					Body = template.Body,
					IsDefault = template.IsDefault
				};
			}

			return View("CertificateTemplates", model);
		}

		private async Task<TrainingCertificateTemplatesView> TemplatesPageAsync(CertificateTemplateInput input)
		{
			var model = await PageAsync(new TrainingCertificateTemplatesView(), "CertificateTemplates");
			model.Templates = (await _lmsTraining.GetCertificateTemplatesAsync(DepartmentId) ?? new List<LmsCertificateTemplate>())
				.Where(t => t != null && t.DepartmentId == DepartmentId).OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			model.Input = input ?? new CertificateTemplateInput();
			return model;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveCertificateTemplate(CertificateTemplateInput input, CancellationToken cancellationToken)
		{
			if (!await LmsAllowedAsync())
				return NotFound();

			input ??= new CertificateTemplateInput();
			var existing = (await _lmsTraining.GetCertificateTemplatesAsync(DepartmentId) ?? new List<LmsCertificateTemplate>()).Where(t => t != null && t.DepartmentId == DepartmentId).ToList();
			LmsCertificateTemplate template;
			if (input.LmsCertificateTemplateId > 0)
			{
				template = existing.FirstOrDefault(t => t.LmsCertificateTemplateId == input.LmsCertificateTemplateId);
				if (template == null)
					return NotFound();
			}
			else
			{
				template = new LmsCertificateTemplate { DepartmentId = DepartmentId };
			}

			var name = (input.Name ?? string.Empty).Trim();
			if (name.Length == 0)
				ModelState.AddModelError(nameof(input.Name), L("NameRequired"));
			name = Bounded(name, 200, nameof(input.Name));
			var body = StringHelpers.SanitizeHtmlForStorage(input.Body ?? string.Empty);
			if (string.IsNullOrWhiteSpace(body))
				ModelState.AddModelError(nameof(input.Body), L("Rec.TemplateBodyRequired"));
			else if (body.Length > 50000)
				ModelState.AddModelError(nameof(input.Body), string.Format(L("TextTooLong"), 50000));

			if (!ModelState.IsValid)
				return View("CertificateTemplates", await TemplatesPageAsync(input));

			template.Name = name;
			template.Body = body;
			template.IsDefault = input.IsDefault;

			try
			{
				var saved = await _lmsTraining.SaveCertificateTemplateAsync(template, UserId, cancellationToken);
				Flash(saved != null ? L("Rec.TemplateSavedMessage") : null, saved == null ? L("Rec.ActionFailed") : null);
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				ModelState.AddModelError(string.Empty, ErrorText(ex.Message));
				return View("CertificateTemplates", await TemplatesPageAsync(input));
			}

			return RedirectToAction("CertificateTemplates");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteCertificateTemplate(int id, CancellationToken cancellationToken)
		{
			if (!await LmsAllowedAsync())
				return NotFound();

			try
			{
				var deleted = await _lmsTraining.DeleteCertificateTemplateAsync(DepartmentId, id, cancellationToken);
				Flash(deleted ? L("Rec.TemplateDeletedMessage") : null, deleted ? null : L("Rec.ActionFailed"));
			}
			catch (InvalidOperationException ex) when (IsServiceCode(ex))
			{
				Flash(null, ErrorText(ex.Message));
			}

			return RedirectToAction("CertificateTemplates");
		}

		#endregion LMS (dark, §24)
	}
}
