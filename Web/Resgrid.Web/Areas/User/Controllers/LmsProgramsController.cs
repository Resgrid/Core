using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.LmsPrograms;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Programs (Training T3 part 2, LMS add-on, dark behind Training.Lms): the annual training plan (R-C5), competency
	/// frameworks and the gap report (R-C7), training requests with cost and approval, the report catalog with folders and scheduled
	/// reports, certification cycle rules and the program settings. NotFound unless the Trainings module and the operator flag are on.
	/// Every member may read their own requests and gaps; officers (department-wide records managers, department admins) the rest. While
	/// the add-on is lapsed everything stays readable and open requests can still be decided, withdrawn or completed; nothing new is
	/// made (lapse rule 3).
	/// </summary>
	[Area("User")]
	[Authorize]
	public class LmsProgramsController : SecureBaseController
	{
		private const long MaxUploadRequestBytes = 12 * 1024 * 1024;
		private static readonly Regex ServiceCode = new Regex("^[a-z][a-z0-9_]{2,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsTrainingPlanService _plans;
		private readonly ILmsCompetencyService _competencies;
		private readonly ILmsTrainingRequestService _requests;
		private readonly ILmsReportCatalogService _reports;
		private readonly ILmsCertificationCycleService _cycles;
		private readonly ILmsProgramSettingsService _settings;
		private readonly ILearningRecordService _learningRecords;
		private readonly ICertificationService _certifications;
		private readonly ITrainingService _trainings;
		private readonly ILmsCourseService _courses;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IShiftsService _shifts;
		private readonly IAuthorizationService _authorization;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> _localizer;
		private readonly ILmsSkillSheetService _skillSheets;
		private readonly ILmsTaskBookService _taskBooks;

		private LmsActor _actor;
		private TrainingManagementScope _scope;
		private Department _department;
		private Dictionary<string, string> _names;

		public LmsProgramsController(ILmsGateService gate, ILmsAccessService access, ILmsTrainingPlanService plans, ILmsCompetencyService competencies,
			ILmsTrainingRequestService requests, ILmsReportCatalogService reports, ILmsCertificationCycleService cycles, ILmsProgramSettingsService settings,
			ILearningRecordService learningRecords, ICertificationService certifications, ITrainingService trainings, ILmsCourseService courses,
			IDepartmentsService departments, IDepartmentGroupsService groups, IShiftsService shifts, IAuthorizationService authorization,
			IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> localizer, ILmsSkillSheetService skillSheets, ILmsTaskBookService taskBooks)
		{
			_skillSheets = skillSheets;
			_taskBooks = taskBooks;
			_gate = gate;
			_access = access;
			_plans = plans;
			_competencies = competencies;
			_requests = requests;
			_reports = reports;
			_cycles = cycles;
			_settings = settings;
			_learningRecords = learningRecords;
			_certifications = certifications;
			_trainings = trainings;
			_courses = courses;
			_departments = departments;
			_groups = groups;
			_shifts = shifts;
			_authorization = authorization;
			_localizer = localizer;
		}

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			// Dark until the operator flag is on (and closed with the Trainings module): one gate read per request (R-L9).
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId) || !(await ActorAsync()).Gate.FlagOn)
			{
				context.Result = NotFound();
				return;
			}

			_department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			ViewData["Scope"] = await ScopeAsync();
			ViewData["LmsPrograms"] = true;
			await next();
		}

		private async Task<LmsActor> ActorAsync() => _actor ??= await _gate.GetActorAsync(DepartmentId, UserId) ?? LmsActor.Nobody(DepartmentId);

		private async Task<TrainingManagementScope> ScopeAsync() =>
			_scope ??= await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);

		private async Task<bool> IsOfficerAsync() => ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || (await ScopeAsync()).CanManageRecords;

		private static bool CanManageCertifications => ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanManageCertifications();

		private string L(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		private string ErrorText(Exception ex)
		{
			if (ex is InvalidOperationException && ex.Message != null && ServiceCode.IsMatch(ex.Message))
			{
				var text = _localizer?[ex.Message];
				if (text != null && !text.ResourceNotFound && !string.IsNullOrEmpty(text.Value))
					return text.Value;
			}
			return L("Prg.ActionFailed");
		}

		private void Flash(string messageKey, string error = null)
		{
			if (TempData == null)
				return;
			if (messageKey != null)
				TempData["LmsProgramsMessage"] = L(messageKey);
			if (error != null)
				TempData["LmsProgramsError"] = error;
		}

		/// <summary>Runs a write; a refusal becomes the page's error notice.</summary>
		private async Task<bool> TryAsync(Func<Task> work, string successKey)
		{
			try
			{
				await work();
				Flash(successKey);
				return true;
			}
			catch (InvalidOperationException ex)
			{
				Flash(null, ErrorText(ex));
				return false;
			}
		}

		private async Task<Dictionary<string, string>> NamesAsync() =>
			_names ??= (await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		private async Task<List<KeyValuePair<string, string>>> SelectableMembersAsync() =>
			(await _departments.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(n => new KeyValuePair<string, string>(n.UserId, n.Name)).ToList();

		private async Task<T> PageAsync<T>(T view, string tab) where T : LmsProgramPageView
		{
			var actor = await ActorAsync();
			view.CanAuthor = actor.Gate.CanAuthor;
			view.IsLapsed = actor.Gate.IsLapsed;
			view.IsOfficer = await IsOfficerAsync();
			view.CanManageCertifications = CanManageCertifications;
			view.Department = _department;
			view.Names = await NamesAsync();
			view.Categories = await _learningRecords.GetCategoriesAsync(DepartmentId, true) ?? new List<TrainingCategory>();
			view.Message ??= TempData?["LmsProgramsMessage"] as string;
			view.Error ??= TempData?["LmsProgramsError"] as string;
			var department = _department;
			view.Date = d => !d.HasValue ? null : department == null ? d.Value.ToShortDateString() : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).TimeConverter(department).ToShortDateString();

			ViewData["Section"] = "Programs";
			ViewData["SubTabs"] = Tabs(tab, view.IsOfficer, view.CanManageCertifications);
			return view;
		}

		private ModuleTabsView Tabs(string active, bool officer, bool certifications)
		{
			var tabs = new ModuleTabsView { Label = L("Prg.StripLabel") };
			void Add(string key, string icon, string action) => tabs.Tabs.Add(new ModuleTab(L(key), icon, "LmsPrograms", action, action == active));
			Add("Prg.Tab.Home", "fa-th-large", "Index");
			if (officer)
				Add("Prg.Tab.Plans", "fa-calendar", "Plans");
			Add("Prg.Tab.Frameworks", "fa-sitemap", "Frameworks");
			Add("Prg.Tab.Requests", "fa-paper-plane-o", "Requests");
			if (officer)
				Add("Prg.Tab.Reports", "fa-table", "Reports");
			if (certifications)
				Add("Prg.Tab.Cycles", "fa-refresh", "Cycles");
			if (officer)
				Add("Prg.Tab.Settings", "fa-cog", "Settings");
			return tabs;
		}

		private static DateTime? LocalDateToUtc(DateTime? localDate, Department department) =>
			localDate.HasValue ? DateTime.SpecifyKind(DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc) : null;

		private async Task<byte[]> ReadAsync(IFormFile file)
		{
			if (file == null || file.Length <= 0)
				return null;
			using var stream = new MemoryStream();
			await file.CopyToAsync(stream);
			return stream.ToArray();
		}

		#endregion Gates and helpers

		#region Home and settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Index()
		{
			var officer = await IsOfficerAsync();
			var view = await PageAsync(new LmsProgramsHomeView(), "Index");
			view.Frameworks = (await _competencies.GetFrameworksAsync(DepartmentId)).Count;
			view.MyOpenRequests = (await _requests.GetMineAsync(DepartmentId, UserId)).Count(r => r.IsOpen || r.Status == (int)LmsTrainingRequestStatuses.Approved);
			view.AwaitingMe = (await _requests.GetAwaitingDecisionAsync(DepartmentId, UserId)).Count;
			if (officer)
			{
				view.Plans = (await _plans.GetPlansAsync(DepartmentId)).Count;
				view.SavedReports = (await _reports.GetDefinitionsAsync(DepartmentId)).Count;
			}
			if (CanManageCertifications)
				view.CycleRules = (await _cycles.GetRulesAsync(DepartmentId)).Count;
			return View(view);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Settings()
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			return View(await PageAsync(new LmsProgramSettingsView { Settings = await _settings.GetAsync(DepartmentId) }, "Settings"));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Settings(bool requestSupervisorApproval, bool requestTrainingOfficerApproval, string requestCurrency, bool familiarizationEnabled,
			int familiarizationCategoryId, int familiarizationMinutes, bool familiarizationFromInspections, bool familiarizationFromPreplanReviews, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Settings");
			}

			await TryAsync(() => _settings.SaveAsync(DepartmentId, new LmsProgramSettings
			{
				RequestSupervisorApproval = requestSupervisorApproval,
				RequestTrainingOfficerApproval = requestTrainingOfficerApproval,
				RequestCurrency = requestCurrency,
				FamiliarizationEnabled = familiarizationEnabled,
				FamiliarizationCategoryId = familiarizationCategoryId,
				FamiliarizationMinutes = familiarizationMinutes,
				FamiliarizationFromInspections = familiarizationFromInspections,
				FamiliarizationFromPreplanReviews = familiarizationFromPreplanReviews
			}, UserId, cancellationToken), "Prg.Saved");
			return RedirectToAction("Settings");
		}

		#endregion Home and settings

		#region Annual training plan

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Plans()
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			var view = await PageAsync(new LmsPlansView(), "Plans");
			view.Plans = await _plans.GetPlansAsync(DepartmentId);
			view.Groups = await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			view.Shifts = await _shifts.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>();
			view.DefaultYear = DateTime.UtcNow.TimeConverter(_department).Year;
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SavePlan(int id, string name, int year, int scopeKind, int? groupId, int? shiftId, string notes, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Plans");
			}

			LmsTrainingPlan saved = null;
			var ok = await TryAsync(async () => saved = await _plans.SavePlanAsync(new LmsTrainingPlan
			{
				LmsTrainingPlanId = id,
				DepartmentId = DepartmentId,
				Name = name,
				Year = year,
				ScopeKind = scopeKind,
				ScopeId = scopeKind == (int)LmsTrainingPlanScopes.Group ? groupId : scopeKind == (int)LmsTrainingPlanScopes.Shift ? shiftId : null,
				Notes = notes
			}, UserId, cancellationToken), "Prg.Saved");
			return ok ? RedirectToAction("Plan", new { id = saved.LmsTrainingPlanId }) : id > 0 ? RedirectToAction("Plan", new { id }) : RedirectToAction("Plans");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Plan(int id, int? editItemId)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			var plan = await _plans.GetPlanAsync(DepartmentId, id);
			if (plan == null)
				return NotFound();

			var view = await PageAsync(new LmsPlanView(), "Plans");
			view.Dashboard = await _plans.GetDashboardAsync(DepartmentId, id, DateTime.UtcNow);
			view.EditItem = editItemId.HasValue ? view.Dashboard.Items.FirstOrDefault(i => i.LmsTrainingPlanItemId == editItemId.Value) : null;
			view.ScopeName = await ScopeNameAsync(plan);
			return View(view);
		}

		private async Task<string> ScopeNameAsync(LmsTrainingPlan plan)
		{
			switch ((LmsTrainingPlanScopes)plan.ScopeKind)
			{
				case LmsTrainingPlanScopes.Group when plan.ScopeId.HasValue:
					var group = await _groups.GetGroupByIdAsync(plan.ScopeId.Value, false);
					return group?.Name;
				case LmsTrainingPlanScopes.Shift when plan.ScopeId.HasValue:
					var shift = await _shifts.GetShiftByIdAsync(plan.ScopeId.Value);
					return shift?.Name;
				default:
					return _department?.Name;
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SetPlanStatus(int id, int status, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
				Flash(null, L("Prg.AddonRequired"));
			else
				await TryAsync(() => _plans.SetStatusAsync(DepartmentId, id, (LmsTrainingPlanStatuses)status, UserId, cancellationToken), "Prg.Saved");
			return RedirectToAction("Plan", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CopyPlan(int id, int year, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Plan", new { id });
			}
			LmsTrainingPlan copy = null;
			var ok = await TryAsync(async () => copy = await _plans.CopyPlanAsync(DepartmentId, id, year, UserId, cancellationToken), "Prg.PlanCopied");
			return RedirectToAction("Plan", new { id = ok ? copy.LmsTrainingPlanId : id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeletePlan(int id, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			await TryAsync(() => _plans.DeletePlanAsync(DepartmentId, id, UserId, cancellationToken), "Prg.Deleted");
			return RedirectToAction("Plans");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SavePlanItem(LmsPlanItemInput input, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (input == null)
				return BadRequest();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Plan", new { id = input.PlanId });
			}
			await TryAsync(() => _plans.SaveItemAsync(new LmsTrainingPlanItem
			{
				LmsTrainingPlanItemId = input.Id,
				LmsTrainingPlanId = input.PlanId,
				DepartmentId = DepartmentId,
				Month = input.Month,
				CategoryId = input.CategoryId > 0 ? input.CategoryId : null,
				Title = input.Title,
				PlannedHours = input.PlannedHours,
				PlannedSessions = input.PlannedSessions,
				PlannedOn = input.PlannedOn,
				Notes = input.Notes
			}, UserId, cancellationToken), "Prg.Saved");
			return RedirectToAction("Plan", new { id = input.PlanId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeletePlanItem(int id, int planId, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			await TryAsync(() => _plans.DeleteItemAsync(DepartmentId, id, UserId, cancellationToken), "Prg.Deleted");
			return RedirectToAction("Plan", new { id = planId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> MakeCalendarItem(int id, int planId, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
				Flash(null, L("Prg.AddonRequired"));
			else
				await TryAsync(() => _plans.CreateCalendarItemAsync(DepartmentId, id, UserId, cancellationToken), "Prg.CalendarCreated");
			return RedirectToAction("Plan", new { id = planId });
		}

		#endregion Annual training plan

		#region Competency frameworks

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Frameworks()
		{
			var view = await PageAsync(new LmsFrameworksView(), "Frameworks");
			view.Frameworks = await _competencies.GetFrameworksAsync(DepartmentId);
			view.Catalog = _competencies.GetCatalog();
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ApplyCatalog(string key, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Frameworks");
			}
			LmsCompetencyFramework framework = null;
			var ok = await TryAsync(async () => framework = await _competencies.ApplyCatalogAsync(DepartmentId, key, UserId, cancellationToken), "Prg.FrameworkApplied");
			return ok ? RedirectToAction("Framework", new { id = framework.LmsCompetencyFrameworkId }) : RedirectToAction("Frameworks");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxUploadRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
		public async Task<IActionResult> ImportCase(IFormFile file, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Frameworks");
			}

			var result = await _competencies.ImportCaseAsync(DepartmentId, await ReadAsync(file), UserId, cancellationToken);
			if (!string.IsNullOrEmpty(result.Error))
			{
				Flash(null, L(result.Error));
				return RedirectToAction("Frameworks");
			}

			TempData["LmsProgramsMessage"] = string.Format(CultureInfo.CurrentCulture, L("Prg.CaseImported"), result.ItemsAdded, result.ItemsUpdated, result.ItemsRemoved);
			return RedirectToAction("Framework", new { id = result.Framework.LmsCompetencyFrameworkId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveFramework(int id, string name, string publisher, string edition, string description, int evidenceMaxAgeMonths, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return id > 0 ? RedirectToAction("Framework", new { id }) : RedirectToAction("Frameworks");
			}
			LmsCompetencyFramework saved = null;
			var ok = await TryAsync(async () => saved = await _competencies.SaveFrameworkAsync(new LmsCompetencyFramework
			{
				LmsCompetencyFrameworkId = id,
				DepartmentId = DepartmentId,
				Name = name,
				Publisher = publisher,
				Edition = edition,
				Description = description,
				EvidenceMaxAgeMonths = evidenceMaxAgeMonths
			}, UserId, cancellationToken), "Prg.Saved");
			return ok ? RedirectToAction("Framework", new { id = saved.LmsCompetencyFrameworkId }) : id > 0 ? RedirectToAction("Framework", new { id }) : RedirectToAction("Frameworks");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteFramework(int id, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			await TryAsync(() => _competencies.DeleteFrameworkAsync(DepartmentId, id, UserId, cancellationToken), "Prg.Deleted");
			return RedirectToAction("Frameworks");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Framework(int id)
		{
			var framework = await _competencies.GetFrameworkAsync(DepartmentId, id);
			if (framework == null)
				return NotFound();

			var view = await PageAsync(new LmsFrameworkView { Framework = framework }, "Frameworks");
			var items = await _competencies.GetItemsAsync(DepartmentId, id);
			view.Items = LmsCompetencyGapMath.Ordered(items);
			view.Tags = (await _competencies.GetAlignmentsAsync(DepartmentId, id)).GroupBy(a => a.LmsCompetencyItemId).ToDictionary(g => g.Key, g => g.ToList());
			view.Subjects = await SubjectsAsync();
			return View(view);
		}

		/// <summary>
		/// What items can be tagged on: the department's LMS courses, trainings, live categories, person certification types, skill sheets and
		/// task-book tasks (T4: "{definitionId}:{taskKey}").
		/// </summary>
		private async Task<List<LmsSubjectOption>> SubjectsAsync()
		{
			var subjects = new List<LmsSubjectOption>();
			try
			{
				foreach (var row in await _courses.GetCourseRowsAsync(await ActorAsync()) ?? new List<LmsCourseRow>())
					if (row?.Course != null && !row.Course.IsSystem)
						subjects.Add(new LmsSubjectOption { Kind = LmsCompetencySubjectKinds.Course, Id = row.Course.LmsCourseId.ToString(CultureInfo.InvariantCulture), Name = row.Course.Title });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
			foreach (var training in (await _trainings.GetAllTrainingsForDepartmentAsync(DepartmentId) ?? new List<Training>()).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
				subjects.Add(new LmsSubjectOption { Kind = LmsCompetencySubjectKinds.Training, Id = training.TrainingId.ToString(CultureInfo.InvariantCulture), Name = training.Name });
			foreach (var category in (await _learningRecords.GetCategoriesAsync(DepartmentId) ?? new List<TrainingCategory>()).Where(c => !c.IsDeleted))
				subjects.Add(new LmsSubjectOption { Kind = LmsCompetencySubjectKinds.Category, Id = category.TrainingCategoryId.ToString(CultureInfo.InvariantCulture), Name = category.Name });
			foreach (var type in await _certifications.GetActiveCertificationTypesAsync(DepartmentId, CertificationAppliesTo.Person) ?? new List<DepartmentCertificationType>())
				subjects.Add(new LmsSubjectOption { Kind = LmsCompetencySubjectKinds.CertificationType, Id = type.DepartmentCertificationTypeId.ToString(CultureInfo.InvariantCulture), Name = type.Type });
			foreach (var sheet in await _skillSheets.GetSheetsAsync(DepartmentId) ?? new List<LmsSkillSheet>())
				subjects.Add(new LmsSubjectOption { Kind = LmsCompetencySubjectKinds.SkillSheet, Id = sheet.LmsSkillSheetId.ToString(CultureInfo.InvariantCulture), Name = sheet.Title });
			foreach (var definition in await _taskBooks.GetDefinitionsAsync(DepartmentId) ?? new List<LmsTaskBookDefinition>())
				foreach (var task in LmsSkillRules.ParseTasks(definition.TasksJson))
					subjects.Add(new LmsSubjectOption
					{
						Kind = LmsCompetencySubjectKinds.TaskBookTask, Id = LmsSkillRules.TaskSubjectId(definition.LmsTaskBookDefinitionId, task.Key),
						Name = definition.PositionCode + " " + (task.Number ?? task.Key) + " " + task.Title
					});
			return subjects;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveCompetency(int frameworkId, int id, int? parentId, string code, string label, string itemType, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
				Flash(null, L("Prg.AddonRequired"));
			else
				await TryAsync(() => _competencies.SaveItemAsync(new LmsCompetencyItem
				{
					LmsCompetencyFrameworkId = frameworkId,
					LmsCompetencyItemId = id,
					DepartmentId = DepartmentId,
					ParentItemId = parentId > 0 ? parentId : null,
					HumanCodingScheme = code,
					Label = label,
					ItemType = itemType
				}, UserId, cancellationToken), "Prg.Saved");
			return RedirectToAction("Framework", new { id = frameworkId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteCompetency(int frameworkId, int id, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			await TryAsync(() => _competencies.DeleteItemAsync(DepartmentId, id, UserId, cancellationToken), "Prg.Deleted");
			return RedirectToAction("Framework", new { id = frameworkId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Tag(int frameworkId, int itemId, string subject, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			// The picker posts "kind:id".
			var parts = (subject ?? string.Empty).Split(':');
			if (!(await ActorAsync()).Gate.CanAuthor)
				Flash(null, L("Prg.AddonRequired"));
			else if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind) || !Enum.IsDefined(typeof(LmsCompetencySubjectKinds), kind))
				Flash(null, L("lms_program_subject_invalid"));
			else
				await TryAsync(() => _competencies.TagAsync(DepartmentId, itemId, (LmsCompetencySubjectKinds)kind, parts[1], UserId, cancellationToken), "Prg.Tagged");
			return RedirectToAction("Framework", new { id = frameworkId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Untag(int frameworkId, int id, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			await TryAsync(() => _competencies.UntagAsync(DepartmentId, id, UserId, cancellationToken), "Prg.Untagged");
			return RedirectToAction("Framework", new { id = frameworkId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Gaps(int? frameworkId, string userId)
		{
			var scope = await ScopeAsync();
			var target = string.IsNullOrWhiteSpace(userId) ? UserId : userId.Trim();
			var self = string.Equals(target, UserId, StringComparison.OrdinalIgnoreCase);
			if (!self && !(await IsOfficerAsync()) && !scope.CanReviewRecordsOf(target))
				return Unauthorized();

			var view = await PageAsync(new LmsGapsView { UserId = target, FrameworkId = frameworkId }, "Frameworks");
			view.Frameworks = await _competencies.GetFrameworksAsync(DepartmentId);
			if (await IsOfficerAsync() || (scope.AssignRight.AdministeredMemberUserIds?.Count ?? 0) > 0)
				view.Members = (await SelectableMembersAsync()).Where(m => string.Equals(m.Key, UserId, StringComparison.OrdinalIgnoreCase) || scope.CanReviewRecordsOf(m.Key) || ClaimsAuthorizationHelper.IsUserDepartmentAdmin()).ToList();
			var chosen = frameworkId ?? view.Frameworks.FirstOrDefault()?.LmsCompetencyFrameworkId;
			if (chosen.HasValue && view.Frameworks.Any(f => f.LmsCompetencyFrameworkId == chosen.Value))
			{
				view.FrameworkId = chosen;
				view.Report = await _competencies.GetMemberGapsAsync(DepartmentId, chosen.Value, target, DateTime.UtcNow);
			}
			return View(view);
		}

		#endregion Competency frameworks

		#region Training requests

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Requests(int? year)
		{
			var view = await PageAsync(new LmsRequestsView(), "Requests");
			var localYear = DateTime.UtcNow.TimeConverter(_department).Year;
			view.Year = year is >= 2000 and <= 2100 ? year.Value : localYear;
			view.Mine = await _requests.GetMineAsync(DepartmentId, UserId);
			view.AwaitingMe = await _requests.GetAwaitingDecisionAsync(DepartmentId, UserId);
			view.Currency = (await _settings.GetAsync(DepartmentId)).RequestCurrency;
			if (view.IsOfficer)
			{
				var from = LocalDateToUtc(new DateTime(view.Year, 1, 1), _department);
				var to = LocalDateToUtc(new DateTime(view.Year + 1, 1, 1), _department);
				view.All = await _requests.GetForDepartmentAsync(DepartmentId, from, to);
				view.Costs = await _requests.GetCostSummaryAsync(DepartmentId, from, to, LmsTrainingRequestCostGroupings.Member);
			}
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SubmitRequest(LmsTrainingRequestInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Requests");
			}
			LmsTrainingRequest saved = null;
			var ok = await TryAsync(async () => saved = await _requests.SubmitAsync(DepartmentId, UserId, new LmsTrainingRequest
			{
				Title = input.Title,
				Provider = input.Provider,
				Location = input.Location,
				CourseCode = input.CourseCode,
				StartsOn = input.StartsOn,
				EndsOn = input.EndsOn,
				Hours = input.Hours,
				CategoryId = input.CategoryId > 0 ? input.CategoryId : null,
				CostTuition = input.CostTuition,
				CostTravel = input.CostTravel,
				CostOther = input.CostOther,
				Justification = input.Justification
			}, cancellationToken), "Prg.RequestSubmitted");
			return ok ? RedirectToAction("ViewRequest", new { id = saved.LmsTrainingRequestId }) : RedirectToAction("Requests");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ViewRequest(int id)
		{
			var request = await _requests.GetAsync(DepartmentId, id, UserId);
			if (request == null)
				return NotFound();
			return View("ViewRequest", await PageAsync(new LmsRequestView { Request = request }, "Requests"));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Decide(int id, bool approve, string note, CancellationToken cancellationToken)
		{
			await TryAsync(() => _requests.DecideAsync(DepartmentId, id, UserId, approve, note, cancellationToken), approve ? "Prg.RequestApproved" : "Prg.RequestRejected");
			return RedirectToAction("ViewRequest", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Withdraw(int id, CancellationToken cancellationToken)
		{
			await TryAsync(() => _requests.WithdrawAsync(DepartmentId, id, UserId, cancellationToken), "Prg.RequestWithdrawn");
			return RedirectToAction("ViewRequest", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CancelRequest(int id, string note, CancellationToken cancellationToken)
		{
			await TryAsync(() => _requests.CancelAsync(DepartmentId, id, UserId, note, cancellationToken), "Prg.RequestCancelled");
			return RedirectToAction("ViewRequest", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxUploadRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
		public async Task<IActionResult> CompleteRequest(int id, DateTime completedOn, decimal hours, decimal? actualCost, IFormFile evidence, CancellationToken cancellationToken)
		{
			var completedUtc = LocalDateToUtc(completedOn, _department) ?? DateTime.UtcNow;
			var bytes = await ReadAsync(evidence);
			await TryAsync(() => _requests.CompleteAsync(DepartmentId, id, UserId, completedUtc, hours, actualCost, evidence?.FileName, evidence?.ContentType, bytes, cancellationToken),
				"Prg.RequestCompleted");
			return RedirectToAction("ViewRequest", new { id });
		}

		#endregion Training requests

		#region Reports

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Reports()
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			var view = await PageAsync(new LmsReportsView(), "Reports");
			view.Catalog = _reports.GetCatalog();
			view.Folders = await _reports.GetFoldersAsync(DepartmentId);
			view.Definitions = await _reports.GetDefinitionsAsync(DepartmentId);
			return View(view);
		}

		private static LmsReportParameters Parameters(int period, DateTime? from, DateTime? to, int? categoryId, int groupBy, string userId, int? planId, int? frameworkId, bool former, int withinDays) =>
			new LmsReportParameters
			{
				Period = Enum.IsDefined(typeof(LmsReportPeriods), period) ? (LmsReportPeriods)period : LmsReportPeriods.YearToDate,
				From = from,
				To = to,
				CategoryId = categoryId > 0 ? categoryId : null,
				GroupBy = groupBy,
				UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
				PlanId = planId > 0 ? planId : null,
				FrameworkId = frameworkId > 0 ? frameworkId : null,
				IncludeFormerMembers = former,
				WithinDays = withinDays <= 0 ? 60 : Math.Min(withinDays, 365)
			};

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Report(string key, int? definitionId, int period = (int)LmsReportPeriods.YearToDate, DateTime? from = null, DateTime? to = null,
			int? categoryId = null, int groupBy = 0, string userId = null, int? planId = null, int? frameworkId = null, bool former = false, int withinDays = 60, bool run = false)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();

			var view = await PageAsync(new LmsReportRunView(), "Reports");
			if (definitionId.HasValue)
			{
				view.Definition = await _reports.GetDefinitionAsync(DepartmentId, definitionId.Value);
				if (view.Definition == null)
					return NotFound();
				key = view.Definition.ReportKey;
				view.Parameters = Resgrid.Services.LmsReportCatalogService.ParametersOf(view.Definition);
				view.Runs = await _reports.GetRunsAsync(DepartmentId, definitionId.Value);
			}
			else
			{
				view.Parameters = Parameters(period, from, to, categoryId, groupBy, userId, planId, frameworkId, former, withinDays);
			}

			view.Descriptor = _reports.Find(key);
			if (view.Descriptor == null)
				return NotFound();
			view.Folders = await _reports.GetFoldersAsync(DepartmentId);
			view.Plans = await _plans.GetPlansAsync(DepartmentId);
			view.Frameworks = await _competencies.GetFrameworksAsync(DepartmentId);
			view.Members = await SelectableMembersAsync();
			view.Recipients = await RecipientOptionsAsync(view.Members);
			if (run || definitionId.HasValue)
				view.Table = await _reports.RunAsync(DepartmentId, UserId, key, view.Parameters, CultureInfo.CurrentUICulture, DateTime.UtcNow);
			return View(view);
		}

		/// <summary>Members who may receive scheduled training reports (records managers and department admins).</summary>
		private async Task<List<KeyValuePair<string, string>>> RecipientOptionsAsync(List<KeyValuePair<string, string>> members)
		{
			var admins = new HashSet<string>((await _departments.GetActiveAdminsForDepartmentAsync(DepartmentId) ?? new List<Model.Identity.IdentityUser>())
				.Where(a => a?.Id != null).Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
			var result = new List<KeyValuePair<string, string>>();
			foreach (var member in members.Take(500))
			{
				if (admins.Contains(member.Key))
				{
					result.Add(member);
					continue;
				}
				var scope = await _authorization.GetTrainingManagementScopeAsync(member.Key, DepartmentId);
				if (scope != null && scope.CanManageRecords)
					result.Add(member);
			}
			return result;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ReportFile(string key, int? definitionId, string format, int period = (int)LmsReportPeriods.YearToDate, DateTime? from = null, DateTime? to = null,
			int? categoryId = null, int groupBy = 0, string userId = null, int? planId = null, int? frameworkId = null, bool former = false, int withinDays = 60)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			var parameters = Parameters(period, from, to, categoryId, groupBy, userId, planId, frameworkId, former, withinDays);
			if (definitionId.HasValue)
			{
				var definition = await _reports.GetDefinitionAsync(DepartmentId, definitionId.Value);
				if (definition == null)
					return NotFound();
				key = definition.ReportKey;
				parameters = Resgrid.Services.LmsReportCatalogService.ParametersOf(definition);
			}
			if (_reports.Find(key) == null)
				return NotFound();
			return await FileOfAsync(key, parameters, format);
		}

		private async Task<IActionResult> FileOfAsync(string key, LmsReportParameters parameters, string format)
		{
			var table = await _reports.RunAsync(DepartmentId, UserId, key, parameters, CultureInfo.CurrentUICulture, DateTime.UtcNow);
			var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
				return File(_reports.ToPdfBytes(table, _department?.Name, CultureInfo.CurrentUICulture), "application/pdf", key + "-" + stamp + ".pdf");
			return File(_reports.ToCsvBytes(table), "text/csv", key + "-" + stamp + ".csv");
		}

		/// <summary>The member's own NCCP export (§13a.1): any member for themselves, a reviewer for the members they may review.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> NccpExport(string userId, string format)
		{
			var target = string.IsNullOrWhiteSpace(userId) ? UserId : userId.Trim();
			if (!string.Equals(target, UserId, StringComparison.OrdinalIgnoreCase) && !await IsOfficerAsync() && !(await ScopeAsync()).CanReviewRecordsOf(target))
				return Unauthorized();
			return await FileOfAsync("nccp-member", new LmsReportParameters { UserId = target, Period = LmsReportPeriods.Last12Months }, format);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveReport(LmsReportDefinitionInput input, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (input == null)
				return BadRequest();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Reports");
			}

			LmsReportDefinition saved = null;
			var ok = await TryAsync(async () => saved = await _reports.SaveDefinitionAsync(new LmsReportDefinition
			{
				LmsReportDefinitionId = input.Id,
				DepartmentId = DepartmentId,
				ReportKey = input.ReportKey,
				Name = input.Name,
				LmsReportFolderId = input.FolderId > 0 ? input.FolderId : null,
				Format = input.Format,
				Frequency = input.Frequency,
				DayOfWeek = input.DayOfWeek,
				DayOfMonth = input.DayOfMonth,
				HourLocal = input.HourLocal,
				RecipientUserIds = string.Join(",", input.Recipients ?? Array.Empty<string>()),
				ParametersJson = Newtonsoft.Json.JsonConvert.SerializeObject(input.Parameters ?? new LmsReportParameters())
			}, UserId, cancellationToken), "Prg.ReportSaved");
			return ok ? RedirectToAction("Report", new { definitionId = saved.LmsReportDefinitionId }) : RedirectToAction("Reports");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteReport(int id, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			await TryAsync(() => _reports.DeleteDefinitionAsync(DepartmentId, id, UserId, cancellationToken), "Prg.Deleted");
			return RedirectToAction("Reports");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveFolder(int id, string name, int sortOrder, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			if (!(await ActorAsync()).Gate.CanAuthor)
				Flash(null, L("Prg.AddonRequired"));
			else
				await TryAsync(() => _reports.SaveFolderAsync(new LmsReportFolder { LmsReportFolderId = id, DepartmentId = DepartmentId, Name = name, SortOrder = sortOrder }, UserId, cancellationToken), "Prg.Saved");
			return RedirectToAction("Reports");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteFolder(int id, CancellationToken cancellationToken)
		{
			if (!await IsOfficerAsync())
				return Unauthorized();
			await TryAsync(() => _reports.DeleteFolderAsync(DepartmentId, id, UserId, cancellationToken), "Prg.Deleted");
			return RedirectToAction("Reports");
		}

		#endregion Reports

		#region Certification cycles

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Cycles()
		{
			if (!CanManageCertifications)
				return Unauthorized();
			var view = await PageAsync(new LmsCyclesView(), "Cycles");
			view.Types = await _certifications.GetActiveCertificationTypesAsync(DepartmentId, CertificationAppliesTo.Person) ?? new List<DepartmentCertificationType>();
			view.Rules = (await _cycles.GetRulesAsync(DepartmentId)).GroupBy(r => r.DepartmentCertificationTypeId).ToDictionary(g => g.Key, g => g.First());
			view.Members = await SelectableMembersAsync();
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveCycleRule(LmsCycleRuleInput input, CancellationToken cancellationToken)
		{
			if (!CanManageCertifications)
				return Unauthorized();
			if (input == null)
				return BadRequest();
			if (!(await ActorAsync()).Gate.CanAuthor)
			{
				Flash(null, L("Prg.AddonRequired"));
				return RedirectToAction("Cycles");
			}
			await TryAsync(() => _cycles.SaveRuleAsync(new LmsCertificationCycleRule
			{
				DepartmentId = DepartmentId,
				DepartmentCertificationTypeId = input.TypeId,
				AutoRenewMode = input.AutoRenewMode,
				AlertOnCompletion = input.AlertOnCompletion,
				AlertOnRenewal = input.AlertOnRenewal,
				AlertOnExpired = input.AlertOnExpired,
				AlertLeadDaysCsv = input.AlertLeadDays,
				NotifySupervisors = input.NotifySupervisors,
				NotifyAdmins = input.NotifyAdmins,
				NamedRecipientUserIds = string.Join(",", input.Named ?? Array.Empty<string>())
			}, UserId, cancellationToken), "Prg.Saved");
			return RedirectToAction("Cycles");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteCycleRule(int id, CancellationToken cancellationToken)
		{
			if (!CanManageCertifications)
				return Unauthorized();
			await TryAsync(() => _cycles.DeleteRuleAsync(DepartmentId, id, UserId, cancellationToken), "Prg.Deleted");
			return RedirectToAction("Cycles");
		}

		#endregion Certification cycles
	}
}
