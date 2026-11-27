using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.LmsExports;
using Resgrid.Web.Areas.User.Models.LmsPrograms;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Programs → Data export and CE reporting (Training T5, round 6D).
	/// <list type="bullet">
	/// <item>Data export (R-C12): department owners and admins queue a zip of everything the department put in (worker 99 builds it) and
	/// download it until the link expires. Deliberately not behind the LMS flag or the add-on: it works after a lapse (§17.3 rules 1–2).</item>
	/// <item>CE reporting: the CAPCE completion file, state CE files and the state-ready roster for officers while the LMS is on and the
	/// add-on active or lapsed; the settings and column maps need the add-on active (rule 3). Files only; nothing is submitted anywhere.</item>
	/// </list>
	/// </summary>
	[Area("User")]
	[Authorize]
	public class LmsExportsController : SecureBaseController
	{
		private const int PreviewRows = 50;
		private static readonly Regex ServiceCode = new Regex("^[a-z][a-z0-9_]{2,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsGateService _gate;
		private readonly ILmsDataExportService _exports;
		private readonly ILmsCeExportService _ce;
		private readonly ICertificationService _certifications;
		private readonly ILearningRecordService _learningRecords;
		private readonly IDepartmentsService _departments;
		private readonly IAuthorizationService _authorization;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> _trainings;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.LmsExports.LmsExports> _localizer;

		private LmsActor _actor;
		private TrainingManagementScope _scope;
		private Department _department;

		private readonly IDepartmentDataProtectionService _protection;

		public LmsExportsController(ILmsGateService gate, ILmsDataExportService exports, ILmsCeExportService ce, ICertificationService certifications,
			ILearningRecordService learningRecords, IDepartmentsService departments, IAuthorizationService authorization,
			IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> trainings, IStringLocalizer<Resgrid.Localization.Areas.User.LmsExports.LmsExports> localizer,
			IDepartmentDataProtectionService protection)
		{
			_protection = protection;
			_gate = gate;
			_exports = exports;
			_ce = ce;
			_certifications = certifications;
			_learningRecords = learningRecords;
			_departments = departments;
			_authorization = authorization;
			_trainings = trainings;
			_localizer = localizer;
		}

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			// One gate read per request (R-L9); the data export needs no flag or add-on, CE reporting checks both itself.
			_actor = await _gate.GetActorAsync(DepartmentId, UserId) ?? LmsActor.Nobody(DepartmentId);
			_scope = await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);
			_department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			ViewData["Scope"] = _scope;
			ViewData["Section"] = "Programs";
			if (_actor.Gate?.FlagOn == true)
				ViewData["LmsPrograms"] = true;
			await next();
		}

		private bool IsOfficer => _actor.IsDepartmentAdmin || _scope.CanManageRecords;

		private string T(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		private string TrainingsText(string key)
		{
			var value = _trainings?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		private void Flash(string messageKey, string error = null)
		{
			if (TempData == null)
				return;
			if (messageKey != null)
				TempData["LmsExportsMessage"] = T(messageKey);
			if (error != null)
				TempData["LmsExportsError"] = error;
		}

		private string ErrorText(Exception ex) =>
			ex is InvalidOperationException && ex.Message != null && ServiceCode.IsMatch(ex.Message) ? T(ex.Message) : T("Ce.ActionFailed");

		private T Page<T>(T view, string active) where T : LmsExportsPageView
		{
			view.Department = _department;
			view.IsDepartmentAdmin = _actor.IsDepartmentAdmin;
			view.IsOfficer = IsOfficer;
			view.CanAuthor = _actor.Gate?.CanAuthor == true;
			view.IsLapsed = _actor.Gate?.IsLapsed == true;
			view.Message ??= TempData?["LmsExportsMessage"] as string;
			view.Error ??= TempData?["LmsExportsError"] as string;
			var department = _department;
			view.Time = d => !d.HasValue ? null : department == null ? d.Value.ToString("g") : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).TimeConverter(department).ToString("g");

			var certifications = ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanManageCertifications();
			ViewData["SubTabs"] = _actor.Gate?.FlagOn == true
				? LmsProgramTabs.Build(TrainingsText, "LmsExports/" + active, IsOfficer, certifications, _actor.IsDepartmentAdmin)
				: null;
			return view;
		}

		private async Task<Dictionary<string, string>> NamesAsync() =>
			(await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		private DateTime LocalToday => _department == null ? DateTime.UtcNow.Date : DateTime.UtcNow.TimeConverter(_department).Date;

		#endregion Gates and helpers

		#region Data export (R-C12)

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Index()
		{
			if (!LmsDataExitRules.CanExport(_actor))
				return Unauthorized();

			var view = Page(new LmsDataExportsView(), "Index");
			view.Exports = await _exports.GetExportsAsync(_actor);
			view.Names = await NamesAsync();
			view.LinkDays = Math.Clamp(Config.LmsDataExitConfig.DownloadLinkDays, 1, 30);
			view.CanRequest = view.Exports.Count(LmsDataExitRules.IsActive) < LmsDataExitRules.MaxActivePerDepartment;
			view.UtcNow = DateTime.UtcNow;
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Queue(int scope, bool includeMedia, CancellationToken cancellationToken)
		{
			if (!LmsDataExitRules.CanExport(_actor))
				return Unauthorized();

			var result = await _exports.RequestAsync(_actor, Enum.IsDefined(typeof(LmsDataExportScopes), scope) ? (LmsDataExportScopes)scope : LmsDataExportScopes.Full, includeMedia,
				cancellationToken);
			if (result.Succeeded)
				Flash("Exit.Queued");
			else
				Flash(null, T("Exit.Error." + result.Error));
			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
		{
			if (!LmsDataExitRules.CanExport(_actor))
				return Unauthorized();

			var result = await _exports.GetDownloadAsync(_actor, id, DateTime.UtcNow, cancellationToken);
			if (!result.Succeeded)
			{
				Flash(null, T("Exit.Error." + result.Error));
				return RedirectToAction("Index");
			}

			if (result.Bytes != null)
				return File(result.Bytes, "application/zip", result.FileName);
			return Redirect(result.DownloadUrl);
		}

		#endregion Data export (R-C12)

		#region CE reporting

		private IActionResult CeGate()
		{
			if (_actor.Gate?.FlagOn != true)
				return NotFound();
			if (!_ce.CanBuild(_actor, _scope.CanManageRecords))
				return Unauthorized();
			return null;
		}

		private async Task<LmsCeReportingView> CeViewAsync(LmsCeExportRequest request)
		{
			var view = Page(new LmsCeReportingView(), "Ce");
			view.Exporters = _ce.GetExporters().ToList();
			view.Settings = await _ce.GetSettingsAsync(DepartmentId);
			view.CanConfigure = _ce.CanConfigure(_actor);
			view.CertificationTypes = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId) ?? new List<DepartmentCertificationType>())
				.OrderBy(t => t.Type, StringComparer.CurrentCultureIgnoreCase).ToList();
			view.Categories = (await _learningRecords.GetCategoriesAsync(DepartmentId) ?? new List<TrainingCategory>()).Where(c => !c.IsDeleted)
				.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList();
			var firstOfMonth = new DateTime(LocalToday.Year, LocalToday.Month, 1);
			view.Request = request ?? new LmsCeExportRequest { ExporterKey = LmsCeExporterCatalog.TrainingHours, FromLocal = firstOfMonth.AddMonths(-1), ToLocal = firstOfMonth.AddDays(-1) };
			try
			{
				view.ProtectionEnforced = await _protection.IsProtectionEnforcedAsync(DepartmentId);
			}
			catch (Exception ex)
			{
				// Unknown state: offer the step-up download (a plain download still withholds whatever is sealed).
				Resgrid.Framework.Logging.LogException(ex, $"The protection state of department {DepartmentId} could not be read for the CE page.");
				view.ProtectionEnforced = true;
			}
			return view;
		}

		private static LmsCeExportRequest BuildRequest(string key, DateTime? from, DateTime? to, bool former, int? categoryId, DateTime today)
		{
			var firstOfMonth = new DateTime(today.Year, today.Month, 1);
			return new LmsCeExportRequest
			{
				ExporterKey = key,
				FromLocal = (from ?? firstOfMonth.AddMonths(-1)).Date,
				ToLocal = (to ?? firstOfMonth.AddDays(-1)).Date,
				IncludeFormerMembers = former,
				CategoryId = categoryId > 0 ? categoryId : null,
				// Headings of the training hours / CPD file in the language of the person building it.
				Culture = CultureInfo.CurrentUICulture
			};
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Ce()
		{
			var refused = CeGate();
			if (refused != null)
				return refused;
			return View(await CeViewAsync(null));
		}

		/// <summary>The first rows of a file on the page (not audited: nothing leaves the page).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CePreview(string key, DateTime? from, DateTime? to, bool former, int? categoryId, CancellationToken cancellationToken)
		{
			var refused = CeGate();
			if (refused != null)
				return refused;

			var request = BuildRequest(key, from, to, former, categoryId, LocalToday);
			var view = await CeViewAsync(request);
			view.Preview = await _ce.BuildAsync(_actor, _scope.CanManageRecords, request, false, cancellationToken);
			view.PreviewRows = PreviewRows;
			if (view.Preview.Problem != null)
				view.Error = T(view.Preview.Problem);
			return View("Ce", view);
		}

		/// <summary>
		/// The file itself (audited). The numbers go through the protected read pipeline: under ADP a plain download carries them REDACTED, and the
		/// page's step-up download (the reveal module's fetch with the grant header) carries them as recorded.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CeDownload(string key, DateTime? from, DateTime? to, bool former, int? categoryId, CancellationToken cancellationToken)
		{
			var refused = CeGate();
			if (refused != null)
				return refused;

			var file = await _ce.BuildAsync(_actor, _scope.CanManageRecords, BuildRequest(key, from, to, former, categoryId, LocalToday), true, cancellationToken);
			if (file.Problem != null)
			{
				Flash(null, T(file.Problem));
				return RedirectToAction("Ce");
			}

			return File(file.Bytes, file.ContentType, file.FileName);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CeSettings(string capceProviderNumber, string capceProviderName, int licenseCertificationTypeId, int nremtCertificationTypeId,
			int nemsidCertificationTypeId, string ceBrokerProviderNumber, CancellationToken cancellationToken)
		{
			var refused = CeGate();
			if (refused != null)
				return refused;
			if (!_ce.CanConfigure(_actor))
			{
				Flash(null, T(_actor.IsDepartmentAdmin ? "Ce.AddonRequired" : "lms_ce_not_authorized"));
				return RedirectToAction("Ce");
			}

			try
			{
				await _ce.SaveSettingsAsync(_actor, new LmsCeReportingSettings
				{
					CapceProviderNumber = capceProviderNumber,
					CapceProviderName = capceProviderName,
					LicenseCertificationTypeId = licenseCertificationTypeId,
					NremtCertificationTypeId = nremtCertificationTypeId,
					NemsidCertificationTypeId = nemsidCertificationTypeId,
					CeBrokerProviderNumber = ceBrokerProviderNumber
				}, cancellationToken);
				Flash("Ce.Saved");
			}
			catch (InvalidOperationException ex)
			{
				Flash(null, ErrorText(ex));
			}

			return RedirectToAction("Ce");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CeColumns(string key)
		{
			var refused = CeGate();
			if (refused != null)
				return refused;
			var exporter = LmsCeExporterCatalog.Find(key);
			if (exporter == null)
				return NotFound();

			var settings = await _ce.GetSettingsAsync(DepartmentId);
			var view = Page(new LmsCeColumnsView(), "Ce");
			view.Exporter = exporter;
			view.Columns = settings.ColumnsFor(exporter);
			if (exporter.LocalizedHeaders)
				LmsCeExportRules.LocalizeHeaders(view.Columns, T);
			view.IsCustom = settings.HasCustomColumns(exporter.Key);
			view.CanConfigure = _ce.CanConfigure(_actor);
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CeColumns(string key, List<string> header, List<int> source, List<string> argument, bool reset, CancellationToken cancellationToken)
		{
			var refused = CeGate();
			if (refused != null)
				return refused;
			var exporter = LmsCeExporterCatalog.Find(key);
			if (exporter == null)
				return NotFound();
			if (!_ce.CanConfigure(_actor))
			{
				Flash(null, T(_actor.IsDepartmentAdmin ? "Ce.AddonRequired" : "lms_ce_not_authorized"));
				return RedirectToAction("CeColumns", new { key = exporter.Key });
			}

			var columns = new List<LmsCeColumn>();
			if (!reset)
				for (var i = 0; i < (header?.Count ?? 0); i++)
					columns.Add(new LmsCeColumn
					{
						Header = header[i],
						Source = source != null && i < source.Count ? source[i] : (int)LmsCeColumnSources.Constant,
						Argument = argument != null && i < argument.Count ? argument[i] : null
					});

			try
			{
				await _ce.SaveColumnsAsync(_actor, exporter.Key, columns, cancellationToken);
				Flash(reset ? "Ce.ColumnsReset" : "Ce.ColumnsSaved");
			}
			catch (InvalidOperationException ex)
			{
				Flash(null, ErrorText(ex));
			}

			return RedirectToAction("CeColumns", new { key = exporter.Key });
		}

		#endregion CE reporting
	}
}
