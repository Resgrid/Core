using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.LmsStandards;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Standards (Training T5 item 3, LMS add-on; dark behind Training.Lms): SCORM 1.2 / 2004 and cmi5 content packages, LTI 1.3
	/// tool registrations with the department's platform details and key rotation, and the xAPI LRS settings with the outbox. Managers (Manage
	/// Courses or department admin) only; changes need the add-on (lapse rule 3). Learners reach this controller only to launch an LTI lesson.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class LmsStandardsController : SecureBaseController
	{
		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsContentPackageService _packages;
		private readonly ILmsLtiService _lti;
		private readonly ILmsXapiService _xapi;
		private readonly IDepartmentsService _departments;
		private readonly IAuthorizationService _authorization;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.LmsStandards.LmsStandards> _localizer;

		private LmsActor _actor;
		private Department _department;

		public LmsStandardsController(ILmsGateService gate, ILmsAccessService access, ILmsContentPackageService packages, ILmsLtiService lti, ILmsXapiService xapi,
			IDepartmentsService departments, IAuthorizationService authorization, IStringLocalizer<Resgrid.Localization.Areas.User.LmsStandards.LmsStandards> localizer)
		{
			_gate = gate;
			_access = access;
			_packages = packages;
			_lti = lti;
			_xapi = xapi;
			_departments = departments;
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
			ViewData["Scope"] = await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);
			ViewData["LmsLibrary"] = (await ActorAsync()).MayManage || (await ActorAsync()).MayGrade;
			await next();
		}

		private async Task<LmsActor> ActorAsync() => _actor ??= await _gate.GetActorAsync(DepartmentId, UserId) ?? LmsActor.Nobody(DepartmentId);

		private string L(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		private string ErrorText(LmsErrors error) => L("Std.Error." + error);

		private void Flash(LmsResult result, string successKey)
		{
			if (TempData == null)
				return;
			if (result.Succeeded)
				TempData["LmsStandardsMessage"] = L(successKey);
			else
				TempData["LmsStandardsError"] = result.Value is LmsPackageInspection inspection && inspection.Error != LmsPackageErrors.None
					? L("Std.PackageError." + inspection.Error) + (string.IsNullOrWhiteSpace(inspection.Detail) ? string.Empty : " (" + inspection.Detail + ")")
					: ErrorText(result.Error);
		}

		private async Task<T> PageAsync<T>(T view, string tab) where T : LmsStandardsPageView
		{
			var actor = await ActorAsync();
			view.CanAuthor = actor.Gate.CanAuthor;
			view.IsLapsed = actor.Gate.IsLapsed;
			view.IsManager = actor.MayManage;
			view.Message ??= TempData?["LmsStandardsMessage"] as string;
			view.Error ??= TempData?["LmsStandardsError"] as string;
			var department = _department;
			view.When = d => !d.HasValue ? null : department == null ? d.Value.ToString("g") : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).TimeConverter(department).ToString("g");

			ViewData["Section"] = "Standards";
			var tabs = new ModuleTabsView { Label = L("Std.StripLabel") };
			void Add(string key, string icon, string action) => tabs.Tabs.Add(new ModuleTab(L(key), icon, "LmsStandards", action, action == tab));
			Add("Std.Tab.Packages", "fa-archive", "Index");
			Add("Std.Tab.Tools", "fa-plug", "Tools");
			Add("Std.Tab.Xapi", "fa-share-alt", "Xapi");
			ViewData["SubTabs"] = tabs;
			return view;
		}

		/// <summary>The Standards pages are for LMS managers (Manage Courses or department admin); learners only launch.</summary>
		private async Task<bool> IsManagerAsync() => (await ActorAsync()).MayManage;

		#endregion Gates and helpers

		#region Packages

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Index()
		{
			if (!await IsManagerAsync())
				return Unauthorized();
			var view = await PageAsync(new LmsStandardsPackagesView(), "Index");
			view.StoreConfigured = _packages.IsStoreConfigured;
			view.Packages = await _packages.GetPackagesAsync(DepartmentId);
			view.MaxUploadBytes = Resgrid.Config.LmsStandardsConfig.MaxPackageBytes;
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[DisableRequestSizeLimit]
		[RequestFormLimits(MultipartBodyLengthLimit = 600L * 1024 * 1024)]
		public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
		{
			if (file == null || file.Length <= 0)
			{
				Flash(LmsResult.Fail(LmsErrors.InvalidInput), null);
				return RedirectToAction("Index");
			}
			await using var stream = file.OpenReadStream();
			var result = await _packages.UploadAsync(await ActorAsync(), file.FileName, stream, file.Length, cancellationToken);
			Flash(result, "Std.PackageUploaded");
			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeletePackage(int id, CancellationToken cancellationToken)
		{
			Flash(await _packages.DeleteAsync(await ActorAsync(), id, cancellationToken), "Std.PackageRemoved");
			return RedirectToAction("Index");
		}

		#endregion Packages

		#region LTI tools

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Tools()
		{
			if (!await IsManagerAsync())
				return Unauthorized();
			var view = await PageAsync(new LmsStandardsToolsView(), "Tools");
			view.Tools = await _lti.GetToolsAsync(DepartmentId);
			view.Platform = await _lti.GetPlatformDetailsAsync(DepartmentId);
			return View(view);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Tool(int? id)
		{
			if (!await IsManagerAsync())
				return Unauthorized();
			var view = await PageAsync(new LmsStandardsToolView(), "Tools");
			view.Platform = await _lti.GetPlatformDetailsAsync(DepartmentId);
			if (id.HasValue)
			{
				view.Tool = await _lti.GetToolAsync(DepartmentId, id.Value);
				if (view.Tool == null || view.Tool.IsDeleted)
					return NotFound();
			}
			view.Tool ??= new LmsLtiTool { IsActive = true, AllowGradePassback = true };
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveTool(LmsLtiToolInput input, CancellationToken cancellationToken)
		{
			var result = await _lti.SaveToolAsync(await ActorAsync(), input, cancellationToken);
			Flash(result, "Std.ToolSaved");
			return result.Value is LmsLtiTool tool ? RedirectToAction("Tool", new { id = tool.LmsLtiToolId }) : RedirectToAction("Tool", new { id = input?.LmsLtiToolId > 0 ? input.LmsLtiToolId : (int?)null });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteTool(int id, CancellationToken cancellationToken)
		{
			Flash(await _lti.DeleteToolAsync(await ActorAsync(), id, cancellationToken), "Std.ToolRemoved");
			return RedirectToAction("Tools");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RotateKey(CancellationToken cancellationToken)
		{
			Flash(await _lti.RotateKeyAsync(await ActorAsync(), cancellationToken), "Std.KeyRotated");
			return RedirectToAction("Tools");
		}

		/// <summary>
		/// A learner opens an LTI lesson (a new window from the player): the launch is checked like every lesson (gate, lapse rules, lesson
		/// order) and the browser goes to the tool's login initiation URL with a single-use hint.
		/// </summary>
		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Launch(int id, int lesson, CancellationToken cancellationToken)
		{
			var step = await _lti.BeginLaunchAsync(DepartmentId, UserId, id, lesson, cancellationToken);
			if (!step.Succeeded)
			{
				TempData["CourseError"] = ErrorText(step.Error);
				return RedirectToAction("Play", "Courses", new { area = "User", id, lesson });
			}
			Response.Headers["Referrer-Policy"] = "no-referrer";
			return Redirect(step.Url);
		}

		#endregion LTI tools

		#region xAPI

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Xapi()
		{
			if (!await IsManagerAsync())
				return Unauthorized();
			var view = await PageAsync(new LmsStandardsXapiView(), "Xapi");
			view.Overview = await _xapi.GetOverviewAsync(DepartmentId);
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveXapi(bool xapiEnabled, string lrsEndpoint, string lrsKey, string lrsSecret, bool clearSecret, bool shareEmailInActor,
			CancellationToken cancellationToken)
		{
			var result = await _xapi.SaveSettingsAsync(await ActorAsync(), new LmsStandardsSettings
			{
				XapiEnabled = xapiEnabled,
				LrsEndpoint = lrsEndpoint,
				LrsKey = lrsKey,
				ShareEmailInActor = shareEmailInActor
			}, lrsSecret, clearSecret, cancellationToken);
			Flash(result, "Std.XapiSaved");
			return RedirectToAction("Xapi");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RetryXapi(CancellationToken cancellationToken)
		{
			Flash(await _xapi.RetryFailedAsync(await ActorAsync(), cancellationToken), "Std.XapiRequeued");
			return RedirectToAction("Xapi");
		}

		#endregion xAPI
	}
}
