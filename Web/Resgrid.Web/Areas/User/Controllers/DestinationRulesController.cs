using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Mapping → Destination Rules (M0317): what a call's or a unit's destination must offer ("Burns → Burn Center",
	/// "Helicopter → Helipad", "Water Tender → Water Source with Drafting"), whether the call form suggests (and fills in) the
	/// best destination, and the call form's suggestion lookup. Rules and the setting are department-admin only, like the POI
	/// capabilities they use; anyone who can see calls can ask for suggestions.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class DestinationRulesController : SecureBaseController
	{
		private readonly IDestinationRulesService _destinationRulesService;
		private readonly INearestDestinationService _nearestDestinationService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IMappingService _mappingService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly Resgrid.Model.Services.IAuthorizationService _authorizationService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.DestinationRules> _localizer;

		public DestinationRulesController(IDestinationRulesService destinationRulesService, INearestDestinationService nearestDestinationService,
			IDepartmentSettingsService departmentSettingsService, IMappingService mappingService, ICallsService callsService, IUnitsService unitsService,
			Resgrid.Model.Services.IAuthorizationService authorizationService, IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.DestinationRules> localizer)
		{
			_destinationRulesService = destinationRulesService;
			_nearestDestinationService = nearestDestinationService;
			_departmentSettingsService = departmentSettingsService;
			_mappingService = mappingService;
			_callsService = callsService;
			_unitsService = unitsService;
			_authorizationService = authorizationService;
			_localizer = localizer;
		}

		/// <summary>Department admins manage the rules and the setting (the same people who manage POI types and capabilities).</summary>
		internal static bool CanManage() => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!CanManage())
				return Unauthorized();

			var config = await _departmentSettingsService.GetDestinationSuggestionConfigAsync(DepartmentId, true) ?? new DestinationSuggestionConfig();
			var lookups = await LoadLookupsAsync();

			var model = new DestinationRulesIndexView
			{
				Mode = config.GetMode(),
				PreferredWithinMinutes = config.GetPreferredWithinMinutes(),
				CallTypes = lookups.CallTypes,
				Priorities = lookups.Priorities,
				UnitTypes = lookups.UnitTypes,
				HasDestinationTypes = lookups.DestinationTypes.Count > 0,
				Message = TempData?["DestinationRulesMessage"] as string,
				ErrorMessage = TempData?["DestinationRulesError"] as string
			};

			foreach (var rule in await _destinationRulesService.GetRulesForDepartmentAsync(DepartmentId))
				model.Rules.Add(ToRow(rule, lookups));

			return View(model);
		}

		/// <summary>Stores the mode (off / suggest / suggest and fill) and the preferred-capability window.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveSettings(int mode, int? preferredWithinMinutes, CancellationToken cancellationToken)
		{
			if (!CanManage())
				return Unauthorized();

			// A value that did not bind (a non-number mode or window) reads as 0 / null here, which would save "Off" or the default.
			if (!ModelState.IsValid || !Enum.IsDefined(typeof(DestinationSuggestionModes), mode) ||
			    preferredWithinMinutes is < 0 or > DestinationSuggestionConfig.MaximumPreferredWithinMinutes)
			{
				TempData["DestinationRulesError"] = string.Format(_localizer["SettingsInvalid"].Value, DestinationSuggestionConfig.MaximumPreferredWithinMinutes);
				return RedirectToAction("Index");
			}

			await _departmentSettingsService.SetDestinationSuggestionConfigAsync(DepartmentId, new DestinationSuggestionConfig
			{
				Mode = mode,
				PreferredWithinMinutes = preferredWithinMinutes ?? DestinationSuggestionConfig.DefaultPreferredWithinMinutes
			}, cancellationToken);

			TempData["DestinationRulesMessage"] = _localizer["SettingsSaved"].Value;
			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			if (!CanManage())
				return Unauthorized();

			var model = new DestinationRuleEditView();
			if (!string.IsNullOrWhiteSpace(id))
			{
				var rule = await _destinationRulesService.GetRuleByIdAsync(DepartmentId, id);
				if (rule == null)
					return NotFound();

				model.DestinationRuleId = rule.DestinationRuleId;
				model.Name = rule.Name;
				model.CallTypeId = rule.CallTypeId;
				model.CallPriority = rule.CallPriority;
				model.UnitTypeId = rule.UnitTypeId;
				model.PoiTypeIds = rule.GetPoiTypeIds();
				model.RequiredCapabilityIds = rule.GetRequiredCapabilityIds();
				model.PreferredCapabilityIds = rule.GetPreferredCapabilityIds();
			}

			await FillEditViewAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(DestinationRuleEditView model, CancellationToken cancellationToken)
		{
			if (!CanManage())
				return Unauthorized();

			model ??= new DestinationRuleEditView();

			// A condition that did not bind is null, which would save the rule for every call type, priority or unit type.
			if (!ModelState.IsValid)
			{
				await FillEditViewAsync(model);
				return View(model);
			}

			var rule = new DestinationRule
			{
				DestinationRuleId = string.IsNullOrWhiteSpace(model.DestinationRuleId) ? null : model.DestinationRuleId.Trim(),
				Name = model.Name,
				CallTypeId = model.CallTypeId is > 0 ? model.CallTypeId : null,
				CallPriority = model.CallPriority,
				UnitTypeId = model.UnitTypeId is > 0 ? model.UnitTypeId : null,
				PoiTypeIds = string.Join(",", (model.PoiTypeIds ?? new List<int>()).Where(x => x > 0)),
				RequiredCapabilityIds = string.Join(",", model.RequiredCapabilityIds ?? new List<string>()),
				PreferredCapabilityIds = string.Join(",", model.PreferredCapabilityIds ?? new List<string>())
			};

			var result = await _destinationRulesService.SaveRuleAsync(DepartmentId, rule, UserId, cancellationToken);
			if (result.Error == DestinationRuleSaveErrors.NotFound)
				return NotFound();

			if (!result.Success)
			{
				ModelState.AddModelError(string.Empty, ErrorText(result.Error));
				await FillEditViewAsync(model);
				return View(model);
			}

			TempData["DestinationRulesMessage"] = string.Format(_localizer["RuleSaved"].Value, result.Rule.Name);
			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!CanManage())
				return Unauthorized();

			var rule = await _destinationRulesService.GetRuleByIdAsync(DepartmentId, id);
			if (rule == null)
				return NotFound();

			// Deleted by someone else since the lookup: same answer as a rule that was already gone.
			if (!await _destinationRulesService.DeleteRuleAsync(DepartmentId, rule.DestinationRuleId, cancellationToken))
				return NotFound();

			TempData["DestinationRulesMessage"] = string.Format(_localizer["RuleDeleted"].Value, rule.Name);

			return RedirectToAction("Index");
		}

		/// <summary>"Try it": which rules apply to a call type, priority and unit type, and what the destination must then be.</summary>
		[HttpGet]
		public async Task<IActionResult> Preview(int? callTypeId, int? priority, int? unitTypeId)
		{
			if (!CanManage())
				return Unauthorized();

			var lookups = await LoadLookupsAsync();
			var context = new DestinationContext { CallTypeId = callTypeId is > 0 ? callTypeId : null, CallPriority = priority, UnitTypeId = unitTypeId is > 0 ? unitTypeId : null };
			var requirements = await _destinationRulesService.ResolveAsync(DepartmentId, context);

			return Json(new
			{
				success = true,
				applied = requirements.AppliedRules.Select(x => x.Name).ToList(),
				setAside = requirements.SetAsideRules.Select(x => new { name = x.Rule.Name, reason = SetAsideText(x.Reason) }).ToList(),
				types = requirements.PoiTypeIds == null ? null : requirements.PoiTypeIds.Select(id => lookups.DestinationTypeName(id)).ToList(),
				required = requirements.RequiredCapabilityIds.Select(lookups.CapabilityName).ToList(),
				preferred = requirements.PreferredCapabilityIds.Select(lookups.CapabilityName).ToList()
			});
		}

		/// <summary>
		/// The call form destination suggestions (New Call, Update Call, Add Archived Call, the recurring call form): the department's
		/// setting, what the rules require for the call type and priority (and the travelling unit's type), and the best destinations
		/// from the incident location with drive time and distance. Off answers at once.
		/// </summary>
		/// <param name="latitude">The incident location.</param>
		/// <param name="longitude">The incident location.</param>
		/// <param name="priority">The call priority (the call's Priority value); omit to take the call's (with callId).</param>
		/// <param name="type">The call type name; omit (with no callTypeId) to take the call's (with callId).</param>
		/// <param name="callTypeId">The call type id, when the page knows it; matched before the name.</param>
		/// <param name="unitId">The travelling unit: its type's rules, travel mode and vehicle limits apply. A unit of another
		/// department, deleted, or hidden from the member by Security &gt; View Units is ignored.</param>
		/// <param name="callId">The call the destination is for: supplies its type and priority when not given, only when the member can view it.</param>
		/// <param name="cancellationToken">Request cancellation.</param>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> Suggest(double? latitude, double? longitude, int? priority, string type, int? callTypeId = null, int? unitId = null,
			int? callId = null, CancellationToken cancellationToken = default)
		{
			// A missing coordinate would otherwise bind as 0 (a real place); an explicit 0 is still accepted.
			if (!latitude.HasValue || !longitude.HasValue || double.IsNaN(latitude.Value) || double.IsNaN(longitude.Value) ||
			    Math.Abs(latitude.Value) > 90 || Math.Abs(longitude.Value) > 180)
				return Json(new { success = false });

			var request = new DestinationSuggestionRequest
			{
				DepartmentId = DepartmentId,
				Latitude = latitude.Value,
				Longitude = longitude.Value,
				CallTypeId = callTypeId is > 0 ? callTypeId : null,
				CallType = type,
				CallPriority = priority,
				UnitId = await VisibleUnitIdAsync(unitId),
				MaxResults = 5
			};

			var typeGiven = request.CallTypeId.HasValue || !string.IsNullOrWhiteSpace(type);
			if (callId is > 0 && (!typeGiven || !priority.HasValue))
			{
				var call = await _callsService.GetCallByIdAsync(callId.Value);
				if (call != null && call.DepartmentId == DepartmentId && !call.IsDeleted && await _authorizationService.CanUserViewCallAsync(UserId, call.CallId))
				{
					if (!typeGiven)
					{
						request.CallTypeId = call.CallTypeId;
						request.CallType = call.Type;
					}

					request.CallPriority ??= call.Priority;
				}
			}

			var suggestions = await _nearestDestinationService.SuggestDestinationsAsync(request, cancellationToken);

			if (suggestions.Mode == DestinationSuggestionModes.Off)
				return Json(new { success = true, mode = 0 });

			var capabilityNames = (await _mappingService.GetPoiCapabilitiesForDepartmentAsync(DepartmentId) ?? new List<PoiCapability>())
				.Where(x => x != null)
				.GroupBy(x => x.PoiCapabilityId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => x.First().Name, StringComparer.OrdinalIgnoreCase);
			string capability(string id) => id != null && capabilityNames.TryGetValue(id, out var name) ? name : id;

			return Json(new
			{
				success = true,
				mode = (int)suggestions.Mode,
				suggestedPoiId = suggestions.SuggestedPoiId,
				rules = suggestions.Requirements.AppliedRules.Select(x => x.Name).ToList(),
				required = suggestions.Requirements.RequiredCapabilityIds.Select(capability).ToList(),
				preferred = suggestions.Requirements.PreferredCapabilityIds.Select(capability).ToList(),
				destinations = suggestions.Destinations.Select(x => new
				{
					poiId = x.Poi.PoiId,
					name = PoiDisplayHelper.GetDisplayName(x.Poi, x.Poi.Type?.Name),
					typeName = x.Poi.Type?.Name,
					address = x.Poi.Address,
					distanceMeters = Math.Round(x.DistanceMeters),
					travelSeconds = x.TravelSeconds >= 0 ? Math.Round(x.TravelSeconds) : -1,
					travelDistanceMeters = x.TravelDistanceMeters >= 0 ? Math.Round(x.TravelDistanceMeters) : -1,
					status = (int)x.Status,
					statusText = x.Status == PoiAvailabilityStatuses.Open ? null : _localizer["Status" + x.Status].Value,
					availabilityNote = x.Poi.Availability?.Note,
					tier = x.Tier,
					meetsRequirements = x.MeetsRequirements,
					missing = x.MissingCapabilityIds.Select(capability).ToList(),
					preferred = x.PreferredCapabilityIds.Select(capability).ToList(),
					down = (x.Poi.Availability?.GetUnavailableCapabilityIds() ?? new List<string>())
						.Where(id => suggestions.Requirements.RequiredCapabilityIds.Contains(id, StringComparer.OrdinalIgnoreCase))
						.Select(capability)
						.ToList()
				}).ToList()
			});
		}

		/// <summary>
		/// The travelling unit for a suggestion, or null (plain driving, no unit-type rules) when it is not this department's, is
		/// deleted, or Security &gt; View Units hides it from the member: a hidden unit's type and travel profile must not shape what
		/// the member is shown (as the v4 GetSuggestedDestinations).
		/// </summary>
		private async Task<int?> VisibleUnitIdAsync(int? unitId)
		{
			if (!unitId.HasValue || unitId.Value <= 0)
				return null;

			var unit = await _unitsService.GetUnitByIdAsync(unitId.Value);
			if (unit == null || unit.DepartmentId != DepartmentId || unit.IsDeleted ||
			    !await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
				return null;

			return unit.UnitId;
		}

		private string ErrorText(DestinationRuleSaveErrors error)
		{
			switch (error)
			{
				case DestinationRuleSaveErrors.NameRequired:
				case DestinationRuleSaveErrors.NameTooLong:
					return string.Format(_localizer["RuleNameRequired"].Value, DestinationRule.MaxNameLength);
				case DestinationRuleSaveErrors.NothingRequired:
					return _localizer["RuleNothingRequired"].Value;
				default:
					// A type, priority or capability was deleted while the form was open.
					return _localizer["RuleChoicesChanged"].Value;
			}
		}

		private string SetAsideText(DestinationRuleSetAsideReasons reason)
		{
			return reason == DestinationRuleSetAsideReasons.TypesRemoved ? _localizer["SetAsideTypesRemoved"].Value : _localizer["SetAsideTypeConflict"].Value;
		}

		private async Task FillEditViewAsync(DestinationRuleEditView model)
		{
			var lookups = await LoadLookupsAsync();
			model.CallTypes = lookups.CallTypes;
			model.Priorities = lookups.Priorities;
			model.UnitTypes = lookups.UnitTypes;
			model.DestinationTypes = lookups.DestinationTypes;
			model.Capabilities = lookups.Capabilities;
			model.PoiTypeIds ??= new List<int>();
			model.RequiredCapabilityIds ??= new List<string>();
			model.PreferredCapabilityIds ??= new List<string>();
		}

		private DestinationRuleRow ToRow(DestinationRule rule, Lookups lookups)
		{
			var row = new DestinationRuleRow { DestinationRuleId = rule.DestinationRuleId, Name = rule.Name };

			if (rule.CallTypeId.HasValue)
			{
				row.CallTypeName = lookups.CallTypes.FirstOrDefault(x => x.Id == rule.CallTypeId.Value.ToString())?.Name;
				row.CallTypeMissing = row.CallTypeName == null;
			}

			if (rule.CallPriority.HasValue)
			{
				row.PriorityName = lookups.Priorities.FirstOrDefault(x => x.Id == rule.CallPriority.Value.ToString())?.Name;
				row.PriorityMissing = row.PriorityName == null;
			}

			if (rule.UnitTypeId.HasValue)
			{
				row.UnitTypeName = lookups.UnitTypes.FirstOrDefault(x => x.Id == rule.UnitTypeId.Value.ToString())?.Name;
				row.UnitTypeMissing = row.UnitTypeName == null;
			}

			var typeIds = rule.GetPoiTypeIds();
			row.DestinationTypeNames = typeIds.Select(id => lookups.DestinationTypes.FirstOrDefault(x => x.Id == id.ToString())?.Name).Where(x => x != null).ToList();
			row.AllTypesRemoved = typeIds.Count > 0 && row.DestinationTypeNames.Count == 0;

			var required = rule.GetRequiredCapabilityIds();
			var preferred = rule.GetPreferredCapabilityIds();
			row.RequiredCapabilityNames = required.Select(lookups.KnownCapabilityName).Where(x => x != null).ToList();
			row.PreferredCapabilityNames = preferred.Select(lookups.KnownCapabilityName).Where(x => x != null).ToList();
			row.MissingReferenceCount = typeIds.Count - row.DestinationTypeNames.Count + required.Count - row.RequiredCapabilityNames.Count
				+ preferred.Count - row.PreferredCapabilityNames.Count;

			return row;
		}

		private async Task<Lookups> LoadLookupsAsync()
		{
			var lookups = new Lookups();

			lookups.CallTypes = (await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>())
				.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Type))
				.OrderBy(x => x.Type, StringComparer.CurrentCultureIgnoreCase)
				.Select(x => new DestinationRuleOption { Id = x.CallTypeId.ToString(), Name = x.Type })
				.ToList();

			lookups.Priorities = (await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>())
				.Where(x => x != null)
				.OrderBy(x => x.Sort)
				.ThenBy(x => x.DepartmentCallPriorityId)
				.Select(x => new DestinationRuleOption { Id = x.DepartmentCallPriorityId.ToString(), Name = x.Name })
				.ToList();

			lookups.UnitTypes = (await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>())
				.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Type))
				.OrderBy(x => x.Type, StringComparer.CurrentCultureIgnoreCase)
				.Select(x => new DestinationRuleOption { Id = x.UnitTypeId.ToString(), Name = x.Type })
				.ToList();

			lookups.DestinationTypes = (await _mappingService.GetPOITypesForDepartmentAsync(DepartmentId) ?? new List<PoiType>())
				.Where(x => x != null && x.IsDestination)
				.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(x => new DestinationRuleOption { Id = x.PoiTypeId.ToString(), Name = x.Name })
				.ToList();

			lookups.Capabilities = (await _mappingService.GetPoiCapabilitiesForDepartmentAsync(DepartmentId) ?? new List<PoiCapability>())
				.Where(x => x != null)
				.Select(x => new DestinationRuleOption { Id = x.PoiCapabilityId, Name = x.Name })
				.ToList();

			return lookups;
		}

		private class Lookups
		{
			public List<DestinationRuleOption> CallTypes { get; set; } = new List<DestinationRuleOption>();
			public List<DestinationRuleOption> Priorities { get; set; } = new List<DestinationRuleOption>();
			public List<DestinationRuleOption> UnitTypes { get; set; } = new List<DestinationRuleOption>();
			public List<DestinationRuleOption> DestinationTypes { get; set; } = new List<DestinationRuleOption>();
			public List<DestinationRuleOption> Capabilities { get; set; } = new List<DestinationRuleOption>();

			public string KnownCapabilityName(string id) =>
				Capabilities.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))?.Name;

			public string CapabilityName(string id) => KnownCapabilityName(id) ?? id;

			public string DestinationTypeName(int id) => DestinationTypes.FirstOrDefault(x => x.Id == id.ToString())?.Name ?? id.ToString();
		}
	}
}
