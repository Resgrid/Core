using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Wellness;
using Resgrid.Web.Areas.User.Models.Wellness;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Personnel → My exposures (member wellness WL0, protocols-training-knowledge-plan §13b). Every member logs, reviews and exports their
	/// own exposure and critical-incident entries and confirms or dismisses the suggestions built after a flagged call. A wellness
	/// officer (the role named in the wellness settings) opens any member's log, keeps screening flags and follow-up tasks and reads the
	/// member exposure report. Post-call flags are set by wellness officers, department administrators and those who may edit the call.
	/// Department administration alone opens no member's entries; it edits the settings. Every rule lives in IMemberWellnessService.
	/// Workforce Pro (2026-11-26): the pages 404 while the Workforce.Pro flag is off; offered but not licensed they are read-only with the
	/// upsell banner; the service enforces both, this controller only maps its refusals to the flashed message.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class WellnessController : SecureBaseController
	{
		private const string MessageKey = "WellnessMessage";
		private const string ErrorKey = "WellnessError";

		private readonly IMemberWellnessService _wellness;
		private readonly IMemberHealthService _health;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallsService _callsService;
		private readonly IPersonnelRolesService _rolesService;
		private readonly ICertificationService _certificationService;
		private readonly IPdfProvider _pdfProvider;
		private readonly ILmsAccessService _lmsAccess;
		private readonly IWorkforceProAccessService _workforcePro;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Wellness.Wellness> _localizer;
		private WorkforceProAccess _workforceProAccess;

		private Department _department;
		private Dictionary<string, string> _names;

		public WellnessController(IMemberWellnessService wellness, IMemberHealthService health, IDepartmentsService departmentsService, ICallsService callsService,
			IPersonnelRolesService rolesService, ICertificationService certificationService, IPdfProvider pdfProvider, ILmsAccessService lmsAccess,
			IWorkforceProAccessService workforcePro, IStringLocalizer<Resgrid.Localization.Areas.User.Wellness.Wellness> localizer)
		{
			_workforcePro = workforcePro;
			_wellness = wellness;
			_health = health;
			_departmentsService = departmentsService;
			_callsService = callsService;
			_rolesService = rolesService;
			_certificationService = certificationService;
			_pdfProvider = pdfProvider;
			_lmsAccess = lmsAccess;
			_localizer = localizer;
		}

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			// Workforce Pro: invisible while the flag is off (data kept); read-only with the upsell banner while not licensed.
			_workforceProAccess = await WorkforceProRequestGate.GetAccessAsync(HttpContext, _workforcePro, DepartmentId);
			if (!_workforceProAccess.Offered)
			{
				context.Result = NotFound();
				return;
			}

			// The section strip follows the member's rights (Wellness/_Shell); WL1 adds the designated and health officer sections.
			var health = await _health.GetAccessAsync(DepartmentId, UserId);
			ViewData["WellnessAccess"] = new WellnessAccessView
			{
				IsOfficer = health.IsWellnessOfficer,
				IsAdmin = health.IsAdmin,
				IsDesignatedOfficer = health.IsDesignatedOfficer,
				IsHealthOfficer = health.IsHealthOfficer,
				WorkforceProReadOnly = _workforceProAccess.IsReadOnly,
				WorkforceProLicensed = _workforceProAccess.Licensed,
				ShowWorkforceProLinks = health.IsAdmin,
				WorkforceProGraceEndsUtc = health.IsAdmin && _workforceProAccess.InDunningGrace ? _workforceProAccess.GraceEndsUtc : null
			};
			await next();
		}

		private WellnessAccessView Access => ViewData["WellnessAccess"] as WellnessAccessView ?? new WellnessAccessView();

		/// <summary>A page about one member: when Workforce Pro is licensed but the member holds no seat, their records are read-only (the notice).</summary>
		private void MarkSeat(string memberUserId, string memberName)
		{
			if (_workforceProAccess == null || !_workforceProAccess.Licensed || _workforceProAccess.HoldsSeat(memberUserId))
				return;
			var access = Access;
			access.SeatMissingMemberName = memberName;
			access.SeatMissingIsSelf = string.Equals(memberUserId, UserId, StringComparison.OrdinalIgnoreCase);
			ViewData["WellnessAccess"] = access;
		}

		private string L(string key) => _localizer[key].Value;

		private void Flash(string message, string error = null)
		{
			if (TempData == null)
				return;
			if (!string.IsNullOrWhiteSpace(message))
				TempData[MessageKey] = message;
			if (!string.IsNullOrWhiteSpace(error))
				TempData[ErrorKey] = error;
		}

		private string TempMessage() => TempData?[MessageKey] as string;
		private string TempError() => TempData?[ErrorKey] as string;

		/// <summary>A rule refusal's localized message ("Error.{code}"), or the generic one.</summary>
		private string ErrorText(Exception ex)
		{
			if (ex is MemberWellnessException rule)
			{
				var text = _localizer["Error." + rule.Code];
				if (!text.ResourceNotFound)
					return text.Value;
			}
			return L("Error.Generic");
		}

		private async Task<Department> DepartmentAsync() => _department ??= await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

		private async Task<Dictionary<string, string>> NamesAsync()
		{
			if (_names != null)
				return _names;
			_names = (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name?.Trim(), StringComparer.OrdinalIgnoreCase);
			return _names;
		}

		private async Task<string> NameOfAsync(string userId)
		{
			var names = await NamesAsync();
			return !string.IsNullOrWhiteSpace(userId) && names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : UserName;
		}

		private async Task<Dictionary<int, string>> CallNamesAsync(IEnumerable<int?> callIds)
		{
			var names = new Dictionary<int, string>();
			foreach (var callId in (callIds ?? Enumerable.Empty<int?>()).Where(c => c.HasValue).Select(c => c.Value).Distinct())
			{
				var call = await _callsService.GetCallByIdAsync(callId, false);
				if (call != null && call.DepartmentId == DepartmentId)
					names[callId] = CallLabel(call, await DepartmentAsync());
			}
			return names;
		}

		private static string CallLabel(Call call, Department department)
		{
			var number = string.IsNullOrWhiteSpace(call.Number) ? "#" + call.CallId.ToString(CultureInfo.InvariantCulture) : call.Number;
			return number + " · " + call.Name + " · " + WellnessPresentation.LocalDate(call.LoggedOn, department);
		}

		private async Task<List<WellnessCallOption>> RecentCallsAsync(int days)
		{
			var department = await DepartmentAsync();
			var end = DateTime.UtcNow.AddDays(1);
			var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, DateTime.UtcNow.AddDays(-days), end) ?? new List<Call>();
			return calls.Where(c => !c.IsDeleted)
				.OrderByDescending(c => c.LoggedOn)
				.Take(100)
				.Select(c => new WellnessCallOption { CallId = c.CallId, Label = CallLabel(c, department) })
				.ToList();
		}

		#region My exposures

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			return View("Index", await OverviewViewAsync(UserId));
		}

		[HttpGet]
		public async Task<IActionResult> Member(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.Equals(userId, UserId, StringComparison.OrdinalIgnoreCase))
				return RedirectToAction("Index");
			if (!Access.IsOfficer)
				return Unauthorized();
			return View("Index", await OverviewViewAsync(userId));
		}

		private async Task<WellnessOverviewView> OverviewViewAsync(string memberUserId)
		{
			var model = new WellnessOverviewView
			{
				MemberUserId = memberUserId,
				ViewerUserId = UserId,
				IsSelf = string.Equals(memberUserId, UserId, StringComparison.OrdinalIgnoreCase),
				IsOfficer = Access.IsOfficer,
				Department = await DepartmentAsync(),
				Names = await NamesAsync(),
				Message = TempMessage(),
				Error = TempError()
			};
			model.MemberName = model.NameOf(memberUserId);
			MarkSeat(memberUserId, model.MemberName);
			model.Overview = await _wellness.GetOverviewAsync(DepartmentId, UserId, memberUserId);
			model.CallNames = await CallNamesAsync(model.Overview.Entries.Concat(model.Overview.Suggestions).Select(e => e.CallId));
			model.PeerTeamConfigured = WellnessRoleRules.PeerTeamConfigured(await _wellness.GetSettingsAsync(DepartmentId));

			if (model.IsOfficer)
			{
				// The member and the officers who act today (with the certification switch on, only those holding a current certification).
				var officerIds = (await _wellness.GetActingMemberIdsAsync(DepartmentId, WellnessRoleKinds.WellnessOfficer) ?? new List<string>())
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				officerIds.Add(memberUserId);
				model.FollowUpAssignees = (await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>())
					.Where(p => officerIds.Contains(p.UserId))
					.ToList();
			}

			return model;
		}

		[HttpGet]
		public async Task<IActionResult> New(string userId, int? callId)
		{
			var memberUserId = string.IsNullOrWhiteSpace(userId) ? UserId : userId;
			var isSelf = string.Equals(memberUserId, UserId, StringComparison.OrdinalIgnoreCase);
			if (!isSelf && !Access.IsOfficer)
				return Unauthorized();

			var department = await DepartmentAsync();
			var model = new WellnessEntryView
			{
				MemberUserId = memberUserId,
				MemberName = await NameOfAsync(memberUserId),
				IsSelf = isSelf,
				IsOfficer = Access.IsOfficer,
				Category = (int)ResponderExposureCategories.Carcinogen,
				OccurredOnLocal = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc).TimeConverter(department).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
				CallId = callId,
				Error = TempError()
			};
			await FillEntryChoicesAsync(model);
			return View("Entry", model);
		}

		[HttpGet]
		public async Task<IActionResult> Entry(string id)
		{
			var entry = await _wellness.GetEntryAsync(DepartmentId, UserId, id);
			if (entry == null)
				return NotFound();
			var department = await DepartmentAsync();
			var model = new WellnessEntryView
			{
				ExposureId = entry.RmsResponderExposureId,
				MemberUserId = entry.UserId,
				MemberName = await NameOfAsync(entry.UserId),
				IsSelf = string.Equals(entry.UserId, UserId, StringComparison.OrdinalIgnoreCase),
				IsOfficer = Access.IsOfficer,
				Status = entry.Status,
				Source = entry.Source,
				SuggestionBasis = entry.SuggestionBasis,
				RowVersion = entry.RowVersion,
				Category = entry.Category,
				OccurredOnLocal = DateTime.SpecifyKind(entry.OccurredOn, DateTimeKind.Utc).TimeConverter(department).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
				DurationMinutes = entry.DurationMinutes,
				CallId = entry.CallId,
				Substance = entry.Substance,
				Narrative = entry.Narrative,
				PpeWorn = entry.PpeWorn,
				Decontamination = entry.Decontamination,
				HealthDetails = entry.HealthDetails,
				RemindMember = entry.RemindMember,
				RemindPeerTeam = entry.RemindPeerTeam,
				Error = TempError()
			};
			model.ProtectedWithheld = WellnessProtectedFields.Exposures.Values.Any(a => WellnessPresentation.IsWithheld(a.Get(entry)));
			await FillEntryChoicesAsync(model);
			return View("Entry", model);
		}

		private async Task FillEntryChoicesAsync(WellnessEntryView model)
		{
			model.PeerTeamConfigured = WellnessRoleRules.PeerTeamConfigured(await _wellness.GetSettingsAsync(DepartmentId));
			if (model.CallId.HasValue)
				model.CallName = (await CallNamesAsync(new int?[] { model.CallId })).TryGetValue(model.CallId.Value, out var name) ? name : null;
			if (model.IsNew || !model.IsSuggestion)
				model.Calls = await RecentCallsAsync(90);
			if (model.IsNew && model.IsOfficer)
				model.Members = await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>();
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Save(WellnessEntryView model, string intent, CancellationToken cancellationToken)
		{
			model ??= new WellnessEntryView();
			var department = await DepartmentAsync();
			var input = new ResponderExposureInput
			{
				UserId = string.IsNullOrWhiteSpace(model.MemberUserId) ? UserId : model.MemberUserId,
				CallId = model.CallId > 0 ? model.CallId : null,
				Category = model.Category,
				DurationMinutes = model.DurationMinutes,
				Substance = model.Substance,
				Narrative = model.Narrative,
				PpeWorn = model.PpeWorn,
				Decontamination = model.Decontamination,
				HealthDetails = model.HealthDetails,
				RemindMember = model.RemindMember,
				RemindPeerTeam = model.RemindPeerTeam
			};
			if (DateTime.TryParseExact(model.OccurredOnLocal, new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
				input.OccurredOn = DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);

			try
			{
				RmsResponderExposure saved;
				if (model.IsNew)
				{
					saved = await _wellness.RecordAsync(DepartmentId, UserId, input, cancellationToken);
					Flash(L("Msg.Recorded"));
				}
				else if (string.Equals(intent, "confirm", StringComparison.OrdinalIgnoreCase))
				{
					saved = await _wellness.ConfirmAsync(DepartmentId, UserId, model.ExposureId, input, cancellationToken);
					Flash(L("Msg.Confirmed"));
				}
				else
				{
					saved = await _wellness.UpdateAsync(DepartmentId, UserId, model.ExposureId, input, model.RowVersion > 0 ? model.RowVersion : (long?)null, cancellationToken);
					Flash(L("Msg.Saved"));
				}

				return string.Equals(saved.UserId, UserId, StringComparison.OrdinalIgnoreCase)
					? RedirectToAction("Index")
					: RedirectToAction("Member", new { userId = saved.UserId });
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (Exception ex) when (ex is MemberWellnessException || ex is ArgumentException)
			{
				model.Error = ErrorText(ex);
				model.IsOfficer = Access.IsOfficer;
				model.IsSelf = string.Equals(input.UserId, UserId, StringComparison.OrdinalIgnoreCase);
				model.MemberUserId = input.UserId;
				model.MemberName = await NameOfAsync(input.UserId);
				if (!model.IsNew)
				{
					var current = await _wellness.GetEntryAsync(DepartmentId, UserId, model.ExposureId);
					if (current == null)
						return NotFound();
					model.Status = current.Status;
					model.Source = current.Source;
					model.SuggestionBasis = current.SuggestionBasis;
					model.RowVersion = current.RowVersion;
				}
				await FillEntryChoicesAsync(model);
				return View("Entry", model);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Dismiss(string id, CancellationToken cancellationToken)
		{
			return await ActOnEntryAsync(id, () => _wellness.DismissAsync(DepartmentId, UserId, id, cancellationToken), "Msg.Dismissed");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Void(string id, CancellationToken cancellationToken)
		{
			return await ActOnEntryAsync(id, () => _wellness.VoidAsync(DepartmentId, UserId, id, cancellationToken), "Msg.Voided");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Confirm(string id, CancellationToken cancellationToken)
		{
			return await ActOnEntryAsync(id, () => _wellness.ConfirmAsync(DepartmentId, UserId, id, null, cancellationToken), "Msg.Confirmed");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Reminders(string id, bool remindMember, bool remindPeerTeam, CancellationToken cancellationToken)
		{
			return await ActOnEntryAsync(id, () => _wellness.SetRemindersAsync(DepartmentId, UserId, id, remindMember, remindPeerTeam, cancellationToken), "Msg.RemindersSaved");
		}

		private async Task<IActionResult> ActOnEntryAsync(string id, Func<Task<RmsResponderExposure>> act, string messageKey)
		{
			var before = await _wellness.GetEntryAsync(DepartmentId, UserId, id);
			if (before == null)
				return NotFound();
			try
			{
				await act();
				Flash(L(messageKey));
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (Exception ex) when (ex is MemberWellnessException || ex is ArgumentException)
			{
				Flash(null, ErrorText(ex));
			}
			return string.Equals(before.UserId, UserId, StringComparison.OrdinalIgnoreCase) ? RedirectToAction("Index") : RedirectToAction("Member", new { userId = before.UserId });
		}

		#endregion My exposures

		#region Export

		[HttpGet]
		public async Task<IActionResult> ExportPdf()
		{
			try
			{
				var document = await ExportDocumentAsync();
				var bytes = _pdfProvider.ConvertHtmlToPdf(WellnessDocuments.ExposureLogHtml(document), PageSize());
				return File(bytes, "application/pdf", "exposure-log-" + Stamp() + ".pdf");
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Exposure log PDF could not be generated.");
				Flash(null, L("Error.ExportFailed"));
				return RedirectToAction("Index");
			}
		}

		[HttpGet]
		public async Task<IActionResult> ExportCsv()
		{
			try
			{
				var document = await ExportDocumentAsync();
				return CsvFile(WellnessDocuments.ExposureLogCsv(document), "exposure-log-" + Stamp() + ".csv");
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
		}

		/// <summary>The signed-in member's own entries; there is no parameter that could name anyone else.</summary>
		private async Task<ExposureLogDocument> ExportDocumentAsync()
		{
			var department = await DepartmentAsync();
			var entries = await _wellness.GetOwnEntriesForExportAsync(DepartmentId, UserId);
			var culture = CultureInfo.CurrentUICulture;
			return new ExposureLogDocument
			{
				DepartmentName = department?.Name,
				MemberName = await NameOfAsync(UserId),
				TimeZone = department?.TimeZone,
				Culture = culture.Name,
				RightToLeft = culture.TextInfo.IsRightToLeft,
				GeneratedOnUtc = DateTime.UtcNow,
				Labels = DocumentLabels(),
				Entries = entries,
				CallNames = await CallNamesAsync(entries.Select(e => e.CallId))
			};
		}

		private Dictionary<string, string> DocumentLabels() =>
			WellnessDocuments.LabelKeys.Distinct(StringComparer.Ordinal).ToDictionary(k => k, k => L(k), StringComparer.Ordinal);

		private IActionResult CsvFile(string csv, string fileName)
		{
			var body = Encoding.UTF8.GetBytes(csv ?? string.Empty);
			var preamble = Encoding.UTF8.GetPreamble();
			var bytes = new byte[preamble.Length + body.Length];
			Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
			Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
			return File(bytes, "text/csv", fileName);
		}

		private static string PageSize() =>
			string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase) ? "Letter" : "A4";

		private static string Stamp() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		#endregion Export

		#region Officer report

		[HttpGet]
		public async Task<IActionResult> Report(string from, string to)
		{
			if (!Access.IsOfficer)
				return Unauthorized();
			var (fromUtc, toUtc) = await WindowAsync(from, to);
			var model = new WellnessReportView
			{
				From = from,
				To = to,
				Department = await DepartmentAsync(),
				Names = await NamesAsync(),
				Summaries = await _wellness.GetDepartmentSummaryAsync(DepartmentId, UserId, fromUtc, toUtc),
				RecentFlags = await _wellness.GetRecentFlagsAsync(DepartmentId, UserId, 20),
				Roster = await RosterAsync(),
				Message = TempMessage(),
				Error = TempError()
			};
			model.CallNames = await CallNamesAsync(model.RecentFlags.Select(f => (int?)f.CallId));
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> ReportCsv(string from, string to)
		{
			if (!Access.IsOfficer)
				return Unauthorized();
			var (fromUtc, toUtc) = await WindowAsync(from, to);
			var summaries = await _wellness.GetDepartmentSummaryAsync(DepartmentId, UserId, fromUtc, toUtc);
			var names = await NamesAsync();
			var csv = WellnessDocuments.ReportCsv(summaries, id => names.TryGetValue(id ?? string.Empty, out var n) && !string.IsNullOrWhiteSpace(n) ? n : id, DocumentLabels());
			return CsvFile(csv, "member-exposure-report-" + Stamp() + ".csv");
		}

		private async Task<(DateTime? From, DateTime? To)> WindowAsync(string from, string to)
		{
			var department = await DepartmentAsync();
			DateTime? fromUtc = null, toUtc = null;
			if (DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
				fromUtc = DateTime.SpecifyKind(f.DepartmentLocalToUtc(department), DateTimeKind.Utc);
			if (DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
				toUtc = DateTime.SpecifyKind(t.AddDays(1).DepartmentLocalToUtc(department), DateTimeKind.Utc);
			return (fromUtc, toUtc);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SetScreening(string userId, int? category, string reviewOn, string planNotes, CancellationToken cancellationToken)
		{
			if (!Access.IsOfficer)
				return Unauthorized();
			DateTime? review = DateTime.TryParseExact(reviewOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var r) ? r : (DateTime?)null;
			await RunAsync(() => _wellness.SetScreeningFlagAsync(DepartmentId, UserId, userId, category > 0 ? category : null, review, planNotes, cancellationToken), "Msg.ScreeningSaved");
			return RedirectToAction("Member", new { userId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ClearScreening(string userId, string flagId, CancellationToken cancellationToken)
		{
			if (!Access.IsOfficer)
				return Unauthorized();
			await RunAsync(() => _wellness.ClearScreeningFlagAsync(DepartmentId, UserId, flagId, cancellationToken), "Msg.ScreeningCleared");
			return RedirectToAction("Member", new { userId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AddFollowUp(string userId, string flagId, string kind, string assigneeUserId, string dueOn, CancellationToken cancellationToken)
		{
			if (!Access.IsOfficer)
				return Unauthorized();
			DateTime? due = DateTime.TryParseExact(dueOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;
			await RunAsync(() => _wellness.AddFollowUpAsync(DepartmentId, UserId, flagId, kind, assigneeUserId, due, cancellationToken), "Msg.FollowUpAdded");
			return RedirectToAction("Member", new { userId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CompleteFollowUp(string userId, string assignmentId, CancellationToken cancellationToken)
		{
			await RunAsync(() => _wellness.CompleteFollowUpAsync(DepartmentId, UserId, assignmentId, cancellationToken), "Msg.FollowUpCompleted");
			return string.IsNullOrWhiteSpace(userId) || string.Equals(userId, UserId, StringComparison.OrdinalIgnoreCase) ? RedirectToAction("Index") : RedirectToAction("Member", new { userId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelFollowUp(string userId, string assignmentId, CancellationToken cancellationToken)
		{
			if (!Access.IsOfficer)
				return Unauthorized();
			await RunAsync(() => _wellness.CancelFollowUpAsync(DepartmentId, UserId, assignmentId, cancellationToken), "Msg.FollowUpCancelled");
			return RedirectToAction("Member", new { userId });
		}

		private async Task RunAsync(Func<Task> act, string messageKey)
		{
			try
			{
				await act();
				Flash(L(messageKey));
			}
			catch (UnauthorizedAccessException)
			{
				Flash(null, L("Error.NotAllowed"));
			}
			catch (Exception ex) when (ex is MemberWellnessException || ex is ArgumentException)
			{
				Flash(null, ErrorText(ex));
			}
		}

		#endregion Officer report

		#region Post-call flags

		[HttpGet]
		public async Task<IActionResult> FlagCall(int? callId)
		{
			var model = new WellnessFlagCallView { CallId = callId, Message = TempMessage(), Error = TempError() };
			if (callId.HasValue)
			{
				var call = await _callsService.GetCallByIdAsync(callId.Value, false);
				if (call == null || call.DepartmentId != DepartmentId)
					return NotFound();
				if (!await _wellness.CanFlagCallAsync(DepartmentId, UserId, callId.Value))
					return Unauthorized();
				var department = await DepartmentAsync();
				model.CallName = call.Name;
				model.CallNumber = string.IsNullOrWhiteSpace(call.Number) ? "#" + call.CallId.ToString(CultureInfo.InvariantCulture) : call.Number;
				model.CallDate = WellnessPresentation.LocalDateTime(call.LoggedOn, department);
				model.CallActive = call.State == (int)CallStates.Active;
				model.Flags = await _wellness.GetCallFlagsAsync(DepartmentId, UserId, callId.Value);
			}
			else
			{
				if (!Access.IsOfficer && !Access.IsAdmin)
					return Unauthorized();
				model.RecentCalls = await RecentCallsAsync(30);
			}
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[ActionName("FlagCall")]
		public async Task<IActionResult> FlagCallPost(int callId, int category, CancellationToken cancellationToken)
		{
			try
			{
				await _wellness.FlagCallAsync(DepartmentId, UserId, callId, category, cancellationToken);
				Flash(L("Msg.CallFlagged"));
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (Exception ex) when (ex is MemberWellnessException || ex is ArgumentException)
			{
				Flash(null, ErrorText(ex));
			}
			return RedirectToAction("FlagCall", new { callId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ClearCallFlag(int callId, string flagId, CancellationToken cancellationToken)
		{
			try
			{
				await _wellness.ClearCallFlagAsync(DepartmentId, UserId, flagId, cancellationToken);
				Flash(L("Msg.CallFlagCleared"));
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (Exception ex) when (ex is MemberWellnessException || ex is ArgumentException)
			{
				Flash(null, ErrorText(ex));
			}
			return RedirectToAction("FlagCall", new { callId });
		}

		#endregion Post-call flags

		#region Settings

		[HttpGet]
		public async Task<IActionResult> Settings()
		{
			if (!Access.IsAdmin)
				return Unauthorized();
			var settings = await _wellness.GetSettingsAsync(DepartmentId);
			var model = new WellnessSettingsView
			{
				WellnessOfficerRoleId = settings.WellnessOfficerRoleId,
				PeerTeamCertificationTypeId = settings.PeerTeamCertificationTypeId,
				RetentionYears = settings.RetentionYears,
				DesignatedOfficerRoleId = settings.DesignatedOfficerRoleId,
				BbpTrainingCertificationTypeId = settings.BbpTrainingCertificationTypeId,
				MedicalClearanceCertificationTypeId = settings.MedicalClearanceCertificationTypeId,
				InjuryRetentionYears = settings.InjuryRetentionYears,
				WellnessOfficerCertificationTypeId = settings.WellnessOfficerCertificationTypeId,
				DesignatedOfficerCertificationTypeId = settings.DesignatedOfficerCertificationTypeId,
				PeerTeamRoleId = settings.PeerTeamRoleId,
				RequireCurrentCertification = settings.RequireCurrentCertification,
				Message = TempMessage(),
				Error = TempError()
			};
			await FillSettingsChoicesAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Settings(WellnessSettingsView model, CancellationToken cancellationToken)
		{
			if (!Access.IsAdmin)
				return Unauthorized();
			model ??= new WellnessSettingsView();
			try
			{
				await _wellness.SaveSettingsAsync(DepartmentId, UserId, new WellnessSettings
				{
					WellnessOfficerRoleId = model.WellnessOfficerRoleId,
					PeerTeamCertificationTypeId = model.PeerTeamCertificationTypeId,
					RetentionYears = model.RetentionYears,
					DesignatedOfficerRoleId = model.DesignatedOfficerRoleId,
					BbpTrainingCertificationTypeId = model.BbpTrainingCertificationTypeId,
					MedicalClearanceCertificationTypeId = model.MedicalClearanceCertificationTypeId,
					InjuryRetentionYears = model.InjuryRetentionYears,
					WellnessOfficerCertificationTypeId = model.WellnessOfficerCertificationTypeId,
					DesignatedOfficerCertificationTypeId = model.DesignatedOfficerCertificationTypeId,
					PeerTeamRoleId = model.PeerTeamRoleId,
					RequireCurrentCertification = model.RequireCurrentCertification
				}, cancellationToken);
				Flash(L("Msg.SettingsSaved"));
				return RedirectToAction("Settings");
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (MemberWellnessException ex)
			{
				model.Error = ErrorText(ex);
				await FillSettingsChoicesAsync(model);
				return View(model);
			}
		}

		private async Task FillSettingsChoicesAsync(WellnessSettingsView model)
		{
			model.Roles = (await _rolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()).OrderBy(r => r.Name).ToList();
			model.CertificationTypes = (await _certificationService.GetAllCertificationTypesByDepartmentAsync(DepartmentId) ?? new List<DepartmentCertificationType>())
				.OrderBy(t => t.Type).ToList();
			// A wellness role's own certification: the department's person-scoped types in use, plus whichever is saved now.
			var linked = new[] { model.WellnessOfficerCertificationTypeId, model.DesignatedOfficerCertificationTypeId };
			model.PersonCertificationTypes = model.CertificationTypes
				.Where(t => !t.IsDeleted && t.AppliesTo == (int)CertificationAppliesTo.Person && (t.IsActive || linked.Contains(t.DepartmentCertificationTypeId)))
				.ToList();
			model.RequirementSetsAvailable = await _lmsAccess.IsFlagEnabledAsync(DepartmentId);
			model.Department = await DepartmentAsync();
			model.Names = await NamesAsync();
			model.Roster = await RosterAsync();
		}

		/// <summary>The wellness roles with their members' certification status; empty (never an error page) when it cannot be read.</summary>
		private async Task<List<WellnessRoleRoster>> RosterAsync()
		{
			try
			{
				return await _wellness.GetRosterAsync(DepartmentId, UserId) ?? new List<WellnessRoleRoster>();
			}
			catch (UnauthorizedAccessException)
			{
				return new List<WellnessRoleRoster>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The wellness roster could not be built for department {DepartmentId}.");
				return new List<WellnessRoleRoster>();
			}
		}

		#endregion Settings
	}
}
