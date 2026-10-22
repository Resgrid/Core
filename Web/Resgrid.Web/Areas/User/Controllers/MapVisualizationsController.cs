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
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Filters;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Mapping → Visualizations: call heat maps, hex grids, response/coverage overlays, unit and personnel footprints and
	/// tracks, from a built-in library or the department's saved visualizations. Every action needs call view; the data is
	/// always read under the caller's own visibility (see <see cref="IMapVisualizationService"/>). Sharing a visualization
	/// with the department is a department admin's call.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Call_View)]
	public class MapVisualizationsController : SecureBaseController
	{
		private readonly IMapVisualizationService _mapVisualizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.MapVisualizations> _localizer;

		public MapVisualizationsController(IMapVisualizationService mapVisualizationService, IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService, ICallsService callsService, IUnitsService unitsService, IDepartmentGroupsService departmentGroupsService,
			IUserProfileService userProfileService, IDispatchScopeService dispatchScopeService, Model.Services.IAuthorizationService authorizationService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.MapVisualizations> localizer)
		{
			_mapVisualizationService = mapVisualizationService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_callsService = callsService;
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
			_userProfileService = userProfileService;
			_dispatchScopeService = dispatchScopeService;
			_authorizationService = authorizationService;
			_localizer = localizer;
		}

		private static bool IsDepartmentAdmin() => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

		[HttpGet]
		public IActionResult Index(string id = null, string template = null, int callId = 0)
		{
			var mapConfig = SettingsHelper.GetDepartmentMapConfig(Config.InfoConfig.WebsiteKey);

			var model = new MapVisualizationsIndexView
			{
				TileUrl = mapConfig?.TileUrl,
				Attribution = mapConfig?.Attribution,
				InitialId = string.IsNullOrWhiteSpace(id) ? null : id.Trim(),
				InitialTemplate = MapVisualizationLibrary.Find(template)?.Key,
				InitialCallId = callId > 0 ? callId : 0,
				Strings = _localizer.GetAllStrings(true).GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First().Value)
			};

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Bootstrap()
		{
			// The operating profile orders the library; every template stays available to every department.
			var operatingProfile = await _departmentSettingsService.GetOperatingProfileAsync(DepartmentId);
			var isAdmin = IsDepartmentAdmin();
			var profiles = await _userProfileService.GetAllProfilesForDepartmentIncDisabledDeletedAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();

			var result = new MapVisualizationBootstrapResult { CanShare = isAdmin };

			foreach (var template in MapVisualizationLibrary.ForOperatingProfile(operatingProfile))
			{
				var resourceKey = template.Key.Replace('-', '_');
				result.Templates.Add(new MapVisualizationTemplateResult
				{
					Key = template.Key,
					Name = _localizer[$"Library_{resourceKey}_Name"],
					Description = _localizer[$"Library_{resourceKey}_Description"],
					Category = template.Category.ToString(),
					CategoryName = _localizer[$"Category_{template.Category}"],
					Recommended = template.IsRecommendedFor(operatingProfile),
					NeedsCall = template.NeedsCall,
					Definition = template.CreateDefinition()
				});
			}

			foreach (var saved in await _mapVisualizationService.GetVisibleForUserAsync(DepartmentId, UserId))
				result.Saved.Add(ToSavedResult(saved, profiles, isAdmin));

			foreach (var source in MapVisualizationKeys.Sources)
				result.SourceOptions[source] = new MapVisualizationSourceOptions
				{
					Displays = MapVisualizationKeys.DisplaysFor(source),
					ColorBys = MapVisualizationKeys.ColorBysFor(source),
					Metrics = MapVisualizationKeys.MetricsFor(source)
				};

			var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			result.CallTypes = callTypes.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Type))
				.Select(t => t.Type.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
				.Select(t => new MapVisualizationOption { Value = t, Label = t })
				.ToList();

			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			result.Priorities = priorities.Where(p => p != null)
				.OrderBy(p => p.Sort)
				.Select(p => new MapVisualizationOption { Value = p.DepartmentCallPriorityId.ToString(CultureInfo.InvariantCulture), Label = p.Name })
				.ToList();

			result.Outcomes = Enum.GetValues(typeof(CallStates)).Cast<CallStates>()
				.Where(s => s != CallStates.Pending)
				.Select(s => new MapVisualizationOption { Value = ((int)s).ToString(CultureInfo.InvariantCulture), Label = _localizer[$"Outcome_{(int)s}"] })
				.ToList();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			result.Groups = groups.Where(g => g != null)
				.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(g => new MapVisualizationOption { Value = g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), Label = g.Name })
				.ToList();

			// Only units the viewer may see (visibility matrix) can be picked as a filter.
			var units = await _unitsService.GetUnitsForDepartmentUnlimitedAsync(DepartmentId) ?? new List<Unit>();
			var visibleUnits = new List<Unit>();
			foreach (var unit in units.Where(u => u != null))
				if (await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
					visibleUnits.Add(unit);

			result.Units = visibleUnits
				.OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(u => new MapVisualizationOption { Value = u.UnitId.ToString(CultureInfo.InvariantCulture), Label = u.Name })
				.ToList();

			return Json(result);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[CancelOnClientDisconnect]
		public async Task<IActionResult> Data([FromBody] MapVisualizationDefinition definition, CancellationToken cancellationToken)
		{
			// A body that does not bind must not silently run the default definition in its place.
			if (definition == null || !ModelState.IsValid)
				return BadRequest();

			try
			{
				var result = await _mapVisualizationService.RunAsync(DepartmentId, UserId, definition, cancellationToken);
				return Json(result);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return new EmptyResult();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Map visualization run failed");
				return StatusCode(500);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Save([FromBody] MapVisualizationSaveInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var isAdmin = IsDepartmentAdmin();
			var saveResult = await _mapVisualizationService.SaveAsync(DepartmentId, UserId, isAdmin, new MapVisualizationSaveRequest
			{
				MapVisualizationId = input.MapVisualizationId,
				Name = input.Name,
				Description = input.Description,
				IsShared = input.IsShared,
				TemplateKey = input.TemplateKey,
				Definition = input.Definition
			}, cancellationToken);

			if (saveResult.Saved == null)
				return Json(new MapVisualizationSaveResponse { Error = saveResult.Error.ToString() });

			var profiles = await _userProfileService.GetAllProfilesForDepartmentIncDisabledDeletedAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
			return Json(new MapVisualizationSaveResponse { Saved = ToSavedResult(saveResult.Saved, profiles, isAdmin) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete([FromBody] MapVisualizationDeleteInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.MapVisualizationId))
				return BadRequest();

			var deleted = await _mapVisualizationService.DeleteAsync(DepartmentId, UserId, IsDepartmentAdmin(), input.MapVisualizationId, cancellationToken);
			return Json(new { Deleted = deleted });
		}

		[HttpGet]
		public async Task<IActionResult> CallDetail(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != DepartmentId || call.IsDeleted)
				return NotFound();

			if (!await _dispatchScopeService.CanUserAccessCallAsync(DepartmentId, UserId, call))
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var priority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, call.Priority);

			return Json(new MapVisualizationCallDetail
			{
				CallId = call.CallId,
				Number = call.Number,
				Name = Visible(call.Name),
				Type = call.Type,
				Priority = priority?.Name,
				State = _localizer[$"Outcome_{call.State}"],
				Address = Visible(call.Address),
				LoggedOn = call.LoggedOn.TimeConverter(department).ToString("g", CultureInfo.CurrentCulture),
				Url = Url.Action("ViewCall", "Dispatch", new { area = "User", callId = call.CallId })
			});
		}

		[HttpGet]
		public async Task<IActionResult> SearchCalls(string term, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var calls = await _mapVisualizationService.SearchCallsAsync(DepartmentId, UserId, term, 15, cancellationToken);

			return Json(calls.Select(c => new MapVisualizationCallHit
			{
				CallId = c.CallId,
				Label = string.Join(" · ", new[]
				{
					c.Number, Visible(c.Name), c.Type, c.LoggedOn.TimeConverter(department).ToString("g", CultureInfo.CurrentCulture)
				}.Where(part => !string.IsNullOrWhiteSpace(part)))
			}).ToList());
		}

		private MapVisualizationSavedResult ToSavedResult(MapVisualization saved, Dictionary<string, UserProfile> profiles, bool isAdmin)
		{
			UserProfile creator = null;
			if (!string.IsNullOrWhiteSpace(saved.CreatedByUserId))
				profiles.TryGetValue(saved.CreatedByUserId, out creator);

			return new MapVisualizationSavedResult
			{
				MapVisualizationId = saved.MapVisualizationId,
				Name = saved.Name,
				Description = saved.Description,
				IsShared = saved.IsShared,
				IsOwner = saved.IsOwnedBy(UserId),
				CanEdit = saved.CanBeEditedBy(UserId, isAdmin),
				CreatedByName = creator?.FullName.AsFirstNameLastName,
				UpdatedOnUtc = saved.UpdatedOn,
				TemplateKey = saved.TemplateKey,
				Definition = saved.Definition
			};
		}

		private static string Visible(string value) => ProtectedDataEnvelope.HasEnvelopePrefix(value) ? null : value;
	}
}
