using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.Dashboard;
using Resgrid.Web.Areas.User.Models.Dashboard;
using Resgrid.Web.Helpers;
using AspNetAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// JSON endpoints behind the rg-dashboard element (home-dashboard-redesign-plan.md §4.4) and the department template
	/// editor. Reads are GETs; writes carry the anti-forgery header. Every data call re-checks the widget's gates (§9).
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Department_View)]
	public class DashboardController : SecureBaseController
	{
		public const string ProtectedGrantHeader = "X-Resgrid-Protected-Grant";

		private static readonly IReadOnlyDictionary<DashboardWidgetCategories, string> CategoryColors = new Dictionary<DashboardWidgetCategories, string>
		{
			{ DashboardWidgetCategories.Operations, "#ED5565" },
			{ DashboardWidgetCategories.Personnel, "#228BCB" },
			{ DashboardWidgetCategories.Awareness, "#003C85" },
			{ DashboardWidgetCategories.Communications, "#1b9a9c" },
			{ DashboardWidgetCategories.Planning, "#7a6fbe" },
			{ DashboardWidgetCategories.Readiness, "#449d44" },
			{ DashboardWidgetCategories.Records, "#2f4050" },
			{ DashboardWidgetCategories.Business, "#b86e12" },
			{ DashboardWidgetCategories.IncidentBackOffice, "#a05a2c" },
			{ DashboardWidgetCategories.Intelligence, "#6a4c93" },
			{ DashboardWidgetCategories.Administration, "#676a6c" }
		};

		private static readonly IReadOnlyDictionary<PlanAddonTypes, (string TitleKey, string Url)> Addons = new Dictionary<PlanAddonTypes, (string, string)>
		{
			{ PlanAddonTypes.PTT, ("PttTitle", "/User/Subscription") },
			{ PlanAddonTypes.ADP, ("AdpTitle", "/User/Subscription") },
			{ PlanAddonTypes.ReadinessPro, ("ReadinessProTitle", "/User/ReadinessProBilling") },
			{ PlanAddonTypes.BusinessOperations, ("BusinessOpsTitle", "/User/BusinessOperationsBilling") },
			{ PlanAddonTypes.EnhancedAi, ("EnhancedAiTitle", "/User/AiBilling") },
			{ PlanAddonTypes.IncidentBackOffice, ("IncidentBackOfficeTitle", "/User/IncidentBackOfficeBilling") }
		};

		private readonly IDashboardService _dashboardService;
		private readonly IDashboardEntitlementService _entitlementService;
		private readonly IDashboardWidgetDataService _widgetDataService;
		private readonly AspNetAuthorizationService _aspNetAuthorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUserProfileService _userProfileService;
		private readonly ICallsService _callsService;
		private readonly IOutstandingRequirementsService _outstandingRequirementsService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Home.HomeDashboard> _localizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Home.DashboardWidgets> _widgetLocalizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> _addonsLocalizer;

		public DashboardController(IDashboardService dashboardService, IDashboardEntitlementService entitlementService,
			IDashboardWidgetDataService widgetDataService, AspNetAuthorizationService aspNetAuthorizationService, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IUserProfileService userProfileService,
			ICallsService callsService, IOutstandingRequirementsService outstandingRequirementsService, ISystemAuditsService systemAuditsService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Home.HomeDashboard> localizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.Home.DashboardWidgets> widgetLocalizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> addonsLocalizer)
		{
			_dashboardService = dashboardService;
			_entitlementService = entitlementService;
			_widgetDataService = widgetDataService;
			_aspNetAuthorizationService = aspNetAuthorizationService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_userProfileService = userProfileService;
			_callsService = callsService;
			_outstandingRequirementsService = outstandingRequirementsService;
			_systemAuditsService = systemAuditsService;
			_localizer = localizer;
			_widgetLocalizer = widgetLocalizer;
			_addonsLocalizer = addonsLocalizer;
		}

		#region Board
		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Bootstrap(string board = null, string templateId = null)
		{
			var viewer = await BuildViewerAsync(DashboardWidgetCatalog.ReferencedPolicies);
			await _entitlementService.CompleteViewerAsync(viewer);

			var result = new DashboardBootstrapResult { DepartmentId = DepartmentId };

			if (!string.IsNullOrWhiteSpace(templateId))
			{
				if (!viewer.IsDepartmentAdmin)
					return Forbid();

				var template = await _dashboardService.GetTemplateAsync(DepartmentId, templateId);
				var layout = template == null
					? _dashboardService.GetBuiltInLayout(viewer.Audience)
					: DashboardLayoutValidator.Normalize(DashboardLayoutValidator.Deserialize(template.LayoutJson)) ?? new DashboardLayout();

				result.Board = new DashboardBoardResult { Layout = layout, Source = DashboardLayoutSources.Template };
				result.Template = template == null
					? new DashboardTemplateResult { Name = string.Empty }
					: new DashboardTemplateResult
					{
						TemplateId = template.DepartmentDashboardTemplateId,
						Name = template.Name,
						Description = template.Description,
						Audience = template.Audience,
						TargetId = template.TargetId,
						Priority = template.Priority,
						IsDefault = template.IsDefault
					};
			}
			else
			{
				var boards = await _dashboardService.GetBoardsAsync(DepartmentId, UserId, viewer);
				var current = (board == null ? null : boards.Boards.FirstOrDefault(b => b.BoardId == board)) ?? boards.Boards[0];

				result.Board = ToBoardResult(current);
				result.UseClassic = boards.UseClassic;
				result.Boards = boards.Boards.Select(b => new DashboardBoardSummaryResult
				{
					BoardId = b.BoardId,
					Name = string.IsNullOrWhiteSpace(b.Name) ? (b.SortOrder == 0 ? _localizer["BoardHome"].Value : b.Name) : b.Name,
					IsHome = b.SortOrder == 0
				}).ToList();
			}

			var visible = _entitlementService.GetVisibleWidgets(viewer);
			result.Widgets = visible.Select(d => ToDefinitionResult(d, viewer)).ToList();
			result.Locked = _entitlementService.GetLockedWidgets(viewer).Select(ToLockedResult).ToList();
			result.Categories = Enum.GetValues(typeof(DashboardWidgetCategories)).Cast<DashboardWidgetCategories>()
				.Where(c => visible.Any(w => w.Category == c) || result.Locked.Any(l => l.Category == c.ToString()))
				.Select(c => new DashboardCategoryResult { Key = c.ToString(), Name = _localizer[$"Category.{c}"], Color = CategoryColors[c] }).ToList();

			var sources = visible.SelectMany(w => w.Settings).Where(s => s.OptionsSource != null).Select(s => s.OptionsSource).Distinct();
			var options = await _dashboardService.GetSettingOptionsAsync(DepartmentId, UserId, sources);
			result.Options = options.ToDictionary(o => o.Key, o => o.Value.Select(p => new DashboardOptionResult { Value = p.Key, Label = p.Value }).ToList());

			result.Header = await BuildHeaderAsync(viewer);
			result.Adp = new DashboardAdpResult
			{
				Enforced = viewer.AdpEnforced,
				CanRevealCall = viewer.CanRevealAdp(PermissionTypes.ViewProtectedCallData),
				CanRevealOperational = viewer.CanRevealAdp(PermissionTypes.ViewProtectedOperationalData),
				CanRevealPersonnel = viewer.CanRevealAdp(PermissionTypes.ViewProtectedPersonnelData),
				CanRevealContact = viewer.CanRevealAdp(PermissionTypes.ViewProtectedContactData)
			};

			var mapConfig = SettingsHelper.GetDepartmentMapConfig(Config.InfoConfig.WebsiteKey);
			var mapLibre = SettingsHelper.GetMapLibreWebConfig(Config.InfoConfig.WebsiteKey);
			result.Map = new DashboardMapResult
			{
				TileUrl = mapConfig?.TileUrl,
				Attribution = mapConfig?.Attribution,
				MapProvider = mapConfig?.MapProvider,
				StyleUrl = mapConfig?.StyleUrl,
				AccessToken = mapConfig?.AccessToken,
				Renderer = mapLibre.Renderer,
				MapLibreStyleUrl = mapLibre.StyleUrlDay,
				MapLibreStyleUrlNight = mapLibre.StyleUrlNight
			};

			return Json(result);
		}

		/// <summary>Data for one stored widget instance (settings come from the stored layout, never the request).</summary>
		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> WidgetData(string instance, string board = null, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(instance))
				return BadRequest();

			var viewer = await BuildViewerAsync(Array.Empty<string>());
			var resolved = await _dashboardService.GetBoardAsync(DepartmentId, UserId, viewer, board);
			var widget = resolved?.Layout?.Widgets.FirstOrDefault(w => w.Id == instance);
			if (widget == null)
				return Json(new DashboardWidgetDataResult { GeneratedUtc = DateTime.UtcNow, Error = new DashboardWidgetError { Code = DashboardWidgetErrorCodes.NotFound, MessageKey = "WidgetErrorNotFound" } });

			await AddWidgetPoliciesAsync(viewer, widget.Key);
			var result = await _widgetDataService.GetDataAsync(viewer, widget, ProtectedGrant(), cancellationToken);
			return WidgetResult(result);
		}

		/// <summary>Data for unsaved settings: edit mode, the gallery and the template editor.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> WidgetPreview([FromBody] DashboardPreviewInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Key))
				return BadRequest();

			var viewer = await BuildViewerAsync(Array.Empty<string>());
			await AddWidgetPoliciesAsync(viewer, input.Key);
			var result = await _widgetDataService.GetPreviewAsync(viewer, input.Key, input.Settings, input.Size, ProtectedGrant(), cancellationToken);
			return WidgetResult(result);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveLayout([FromBody] DashboardSaveInput input)
		{
			if (input?.Layout == null)
				return BadRequest();

			var viewer = await BuildViewerAsync(new[] { DashboardEntitlementService.DispatcherPolicy });
			await _entitlementService.CompleteViewerForWidgetAsync(viewer, new DashboardWidgetDefinition());

			var result = await _dashboardService.SaveBoardAsync(DepartmentId, UserId, viewer, input.BoardId, input.RowVersion, input.Layout, input.Name, CancellationToken.None);
			if (!result.Saved)
				return Json(new DashboardSaveResponse { Error = result.Error.ToString(), Current = result.Current == null ? null : ToBoardResult(result.Current) });

			return Json(new DashboardSaveResponse { Board = ToBoardResult(result.Board) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ResetLayout([FromBody] DashboardBoardInput input)
		{
			var viewer = await BuildViewerAsync(new[] { DashboardEntitlementService.DispatcherPolicy });
			await _entitlementService.CompleteViewerForWidgetAsync(viewer, new DashboardWidgetDefinition());

			var board = await _dashboardService.ResetBoardAsync(DepartmentId, UserId, viewer, input?.BoardId, CancellationToken.None);
			return Json(new DashboardSaveResponse { Board = ToBoardResult(board) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AddBoard([FromBody] DashboardBoardInput input)
		{
			var viewer = await BuildViewerAsync(new[] { DashboardEntitlementService.DispatcherPolicy });
			await _entitlementService.CompleteViewerForWidgetAsync(viewer, new DashboardWidgetDefinition());

			var result = await _dashboardService.AddBoardAsync(DepartmentId, UserId, viewer, input?.Name, CancellationToken.None);
			if (!result.Saved)
				return Json(new DashboardSaveResponse { Error = result.Error.ToString() });

			return Json(new DashboardSaveResponse { Board = ToBoardResult(result.Board) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteBoard([FromBody] DashboardBoardInput input)
		{
			var deleted = await _dashboardService.DeleteBoardAsync(DepartmentId, UserId, input?.BoardId, CancellationToken.None);
			return Json(new { Deleted = deleted });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SetClassic([FromBody] DashboardClassicInput input)
		{
			await _dashboardService.SetUseClassicAsync(DepartmentId, UserId, input?.UseClassic == true, CancellationToken.None);
			return Json(new { Redirect = Url.Action("Dashboard", "Home", new { area = "User" }) });
		}

		/// <summary>The header band's department counters (plan §6.3), by audience.</summary>
		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Pulse()
		{
			var viewer = await BuildViewerAsync(new[] { ResgridResources.Call_View, ResgridResources.Call_Create, ResgridResources.Messages_View });
			var items = new List<DashboardPulseItemResult>();
			var stats = await _departmentsService.GetDepartmentStatsByDepartmentUserIdAsync(DepartmentId, UserId);

			if (viewer.HasPolicy(ResgridResources.Call_View))
				items.Add(new DashboardPulseItemResult { Key = "openCalls", Icon = "fa-bullhorn", Value = stats?.OpenCallsCount ?? 0, Label = _localizer["Pulse.OpenCalls"], Url = "/User/Dispatch/Dashboard" });

			if (viewer.HasPolicy(ResgridResources.Call_Create))
			{
				var pending = await _callsService.GetPendingCallsByDepartmentIdAsync(DepartmentId);
				items.Add(new DashboardPulseItemResult { Key = "pending", Icon = "fa-clock-o", Value = pending?.Count ?? 0, Label = _localizer["Pulse.Pending"], Url = "/User/Dispatch/PendingCalls" });

				var awaiting = await _outstandingRequirementsService.GetAwaitingCallCountAsync(DepartmentId);
				if (awaiting > 0)
					items.Add(new DashboardPulseItemResult { Key = "awaiting", Icon = "fa-exclamation-triangle", Value = awaiting, Label = _localizer["Pulse.Awaiting"], Url = "/User/Dispatch/AwaitingResources", Tone = "warn" });
			}

			if (viewer.HasPolicy(ResgridResources.Messages_View))
				items.Add(new DashboardPulseItemResult { Key = "unread", Icon = "fa-envelope", Value = stats?.UnreadMessageCount ?? 0, Label = _localizer["Pulse.Unread"], Url = "/User/Messages/Inbox" });

			return Json(items);
		}
		#endregion Board

		#region Templates
		[HttpGet]
		public async Task<IActionResult> Templates()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var templates = await _dashboardService.GetTemplatesAsync(DepartmentId);
			var roles = (await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()).ToDictionary(r => r.PersonnelRoleId, r => r.Name);
			var groups = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>()).ToDictionary(g => g.DepartmentGroupId, g => g.Name);

			var model = new DashboardTemplatesIndexView
			{
				Templates = templates.Select(t => new DashboardTemplateRow
				{
					TemplateId = t.DepartmentDashboardTemplateId,
					Name = t.Name,
					Description = t.Description,
					Priority = t.Priority,
					IsDefault = t.IsDefault,
					WidgetCount = DashboardLayoutValidator.Deserialize(t.LayoutJson)?.Widgets.Count ?? 0,
					AudienceText = t.Audience == (int)DashboardTemplateAudiences.PersonnelRole && t.TargetId.HasValue
						? $"{_localizer["TemplateAudienceRole"]}: {(roles.TryGetValue(t.TargetId.Value, out var role) ? role : "?")}"
						: t.Audience == (int)DashboardTemplateAudiences.Group && t.TargetId.HasValue
							? $"{_localizer["TemplateAudienceGroup"]}: {(groups.TryGetValue(t.TargetId.Value, out var group) ? group : "?")}"
							: _localizer["TemplateAudienceEveryone"].Value
				}).ToList()
			};

			return View(model);
		}

		/// <summary>The template editor: the rg-dashboard element in template mode.</summary>
		[HttpGet]
		public async Task<IActionResult> EditTemplate(string id = null)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (!string.IsNullOrWhiteSpace(id) && await _dashboardService.GetTemplateAsync(DepartmentId, id) == null)
				return NotFound();

			var model = new DashboardModernView { Mode = "template", TemplateId = id, Strings = DashboardStrings.Collect(_localizer, _widgetLocalizer), Culture = CultureInfo.CurrentUICulture.Name };
			return View("~/Areas/User/Views/Home/DashboardModern.cshtml", model);
		}

		[HttpGet]
		public async Task<IActionResult> TemplateTargets()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>();
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();

			return Json(new
			{
				Roles = roles.OrderBy(r => r.Name).Select(r => new DashboardOptionResult { Value = r.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), Label = r.Name }),
				Groups = groups.OrderBy(g => g.Name).Select(g => new DashboardOptionResult { Value = g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), Label = g.Name })
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveTemplate([FromBody] DashboardTemplateInput input)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (input == null)
				return BadRequest();

			if (!await TargetBelongsToDepartmentAsync((DashboardTemplateAudiences)input.Audience, input.TargetId))
				return Json(new { Error = DashboardSaveErrors.Invalid.ToString() });

			var result = await _dashboardService.SaveTemplateAsync(DepartmentId, UserId, new DashboardTemplateSaveRequest
			{
				DepartmentDashboardTemplateId = input.TemplateId,
				Name = input.Name,
				Description = input.Description,
				Audience = (DashboardTemplateAudiences)input.Audience,
				TargetId = input.TargetId,
				Priority = input.Priority,
				IsDefault = input.IsDefault,
				Layout = input.Layout
			}, CancellationToken.None);

			if (result.Template == null)
				return Json(new { Error = result.Error.ToString() });

			await AuditAsync("Dashboard template saved", result.Template.DepartmentDashboardTemplateId, result.Template.Name);
			return Json(new { TemplateId = result.Template.DepartmentDashboardTemplateId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteTemplate([FromBody] DashboardBoardInput input)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var deleted = await _dashboardService.DeleteTemplateAsync(DepartmentId, UserId, input?.BoardId, CancellationToken.None);
			if (deleted)
				await AuditAsync("Dashboard template deleted", input.BoardId, null);

			return Json(new { Deleted = deleted });
		}
		#endregion Templates

		#region Helpers
		private string ProtectedGrant()
		{
			var value = Request.Headers[ProtectedGrantHeader].ToString();
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}

		private IActionResult WidgetResult(DashboardWidgetDataResult result)
		{
			if (result.Error?.Code == DashboardWidgetErrorCodes.Forbidden)
				return StatusCode(403, result);

			return Json(result);
		}

		/// <summary>The viewer facts only the web tier knows: claims-based policies, admin flags, roles and groups.</summary>
		private async Task<DashboardViewer> BuildViewerAsync(IEnumerable<string> policies)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId) ?? new List<PersonnelRole>();
			var isAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

			var viewer = new DashboardViewer
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				IsDepartmentAdmin = isAdmin,
				IsGroupAdmin = isAdmin || (group != null && group.IsUserGroupAdmin(UserId)),
				PersonnelRoleIds = roles.Select(r => r.PersonnelRoleId).ToList(),
				GroupIds = group == null ? Array.Empty<int>() : new[] { group.DepartmentGroupId },
				TimeZoneId = department?.TimeZone
			};

			foreach (var policy in policies.Append(DashboardEntitlementService.DispatcherPolicy).Distinct(StringComparer.Ordinal))
				await AddPolicyAsync(viewer, policy);

			return viewer;
		}

		private async Task AddWidgetPoliciesAsync(DashboardViewer viewer, string key)
		{
			var definition = DashboardWidgetCatalog.Find(key);
			if (definition == null)
				return;

			foreach (var policy in definition.Gates.Policies)
				await AddPolicyAsync(viewer, policy);
		}

		private async Task AddPolicyAsync(DashboardViewer viewer, string policy)
		{
			if (string.IsNullOrWhiteSpace(policy) || viewer.GrantedPolicies.Contains(policy))
				return;

			try
			{
				if ((await _aspNetAuthorizationService.AuthorizeAsync(User, policy)).Succeeded)
					viewer.GrantedPolicies.Add(policy);
			}
			catch (InvalidOperationException ex)
			{
				// A catalog policy this host never registered: fail closed for that widget, never the whole page.
				Logging.LogException(ex, $"Dashboard widget gate names an unregistered policy '{policy}'");
			}
		}

		private async Task<DashboardHeaderResult> BuildHeaderAsync(DashboardViewer viewer)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var profile = await _userProfileService.GetProfileByUserIdAsync(UserId);

			// NodaTime/TimeZoneConverter, not TimeZoneInfo: the hardened container has no ICU to map Windows ids.
			var timeZone = Resgrid.Services.Dashboard.Widgets.Awareness.ClockWidgetProvider.ToIana(department?.TimeZone);

			return new DashboardHeaderResult
			{
				FirstName = profile?.FirstName ?? ClaimsAuthorizationHelper.GetFullName(),
				DepartmentName = department?.Name,
				DepartmentCode = department?.Code,
				TimeZone = timeZone,
				Audience = viewer.Audience.ToString(),
				IsDepartmentAdmin = viewer.IsDepartmentAdmin,
				CanCreateCall = ClaimsAuthorizationHelper.CanCreateCall(),
				CanSendMessage = (await _aspNetAuthorizationService.AuthorizeAsync(User, ResgridResources.Messages_Create)).Succeeded,
				CanEditTemplates = viewer.IsDepartmentAdmin,
				NewCallUrl = Url.Action("NewCall", "Dispatch", new { area = "User" }),
				MessageUrl = Url.Action("Compose", "Messages", new { area = "User" }),
				ClassicUrl = Url.Action("Dashboard", "Home", new { area = "User", classic = true }),
				TemplatesUrl = Url.Action("Templates", "Dashboard", new { area = "User" })
			};
		}

		private DashboardWidgetDefinitionResult ToDefinitionResult(DashboardWidgetDefinition definition, DashboardViewer viewer)
		{
			return new DashboardWidgetDefinitionResult
			{
				Key = definition.Key,
				Version = definition.Version,
				Category = definition.Category.ToString(),
				Title = _widgetLocalizer[definition.TitleResourceKey],
				Description = _widgetLocalizer[definition.DescriptionResourceKey],
				Icon = definition.Icon,
				ModuleUrl = definition.ModuleUrl,
				AllowedSizes = definition.AllowedSizes.ToList(),
				DefaultSize = definition.DefaultSize,
				Settings = definition.Settings.Select(s => new DashboardSettingResult
				{
					Name = s.Name,
					Type = SettingType(s.Type),
					Label = _widgetLocalizer[$"W.{definition.Key}.Setting.{s.Name}"],
					Min = s.Min,
					Max = s.Max,
					Default = s.Default,
					OptionsSource = s.OptionsSource,
					Options = s.Options.Select(o => new DashboardOptionResult { Value = o, Label = _widgetLocalizer[$"W.{definition.Key}.Option.{o}"] }).ToList()
				}).ToList(),
				RealtimeTopics = definition.RealtimeTopics.ToList(),
				DefaultRefreshSeconds = definition.DefaultRefreshSeconds,
				MinRefreshSeconds = definition.MinRefreshSeconds,
				MultipleInstances = definition.SupportsMultipleInstances,
				HasProtectedFields = definition.ProtectedFields.Count > 0,
				CanRevealProtected = definition.ProtectedFields.Any(f => viewer.CanRevealAdp(f.ViewPermission))
			};
		}

		private DashboardLockedWidgetResult ToLockedResult(DashboardWidgetDefinition definition)
		{
			var addon = Addons[definition.Gates.Addon.Value];
			return new DashboardLockedWidgetResult
			{
				Key = definition.Key,
				Category = definition.Category.ToString(),
				Title = _widgetLocalizer[definition.TitleResourceKey],
				Description = _widgetLocalizer[definition.DescriptionResourceKey],
				Icon = definition.Icon,
				AddonName = _addonsLocalizer[addon.TitleKey],
				AddonUrl = addon.Url
			};
		}

		private static string SettingType(DashboardSettingTypes type) => type switch
		{
			DashboardSettingTypes.Number => "number",
			DashboardSettingTypes.Toggle => "toggle",
			DashboardSettingTypes.Select => "select",
			DashboardSettingTypes.MultiSelect => "multiSelect",
			_ => "text"
		};

		private static DashboardBoardResult ToBoardResult(DashboardResolvedBoard board) => board == null ? null : new DashboardBoardResult
		{
			BoardId = board.BoardId,
			Name = board.Name,
			RowVersion = board.RowVersion,
			Source = board.Source,
			Layout = board.Layout
		};

		private async Task<bool> TargetBelongsToDepartmentAsync(DashboardTemplateAudiences audience, int? targetId)
		{
			// Never trust a posted id: a role or group target must belong to this department.
			if (audience == DashboardTemplateAudiences.Everyone)
				return true;

			if (!targetId.HasValue)
				return false;

			if (audience == DashboardTemplateAudiences.PersonnelRole)
				return (await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()).Any(r => r.PersonnelRoleId == targetId.Value);

			return (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>()).Any(g => g.DepartmentGroupId == targetId.Value);
		}

		private async Task AuditAsync(string action, string templateId, string name)
		{
			try
			{
				await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Website,
					Type = (int)SystemAuditTypes.DashboardTemplateChanged,
					DepartmentId = DepartmentId,
					UserId = UserId,
					Username = UserName,
					Successful = true,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					Data = $"{action}: {templateId} {name}".Trim()
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dashboard template audit failed");
			}
		}
		#endregion Helpers
	}

	/// <summary>Collects the dashboard's resx strings for the element's strings attribute.</summary>
	public static class DashboardStrings
	{
		public static Dictionary<string, string> Collect(IStringLocalizer chrome, IStringLocalizer widgets)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var s in widgets.GetAllStrings(true))
				result[s.Name] = s.Value;
			foreach (var s in chrome.GetAllStrings(true))
				result[s.Name] = s.Value;
			return result;
		}
	}
}
