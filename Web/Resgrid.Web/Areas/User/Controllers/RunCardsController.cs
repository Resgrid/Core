using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.RunCards;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Management UI for run cards (CAD-style response plans). Department-admin only
	/// and gated behind the Dispatch.RunCards feature flag.
	/// </summary>
	[Area("User")]
	public class RunCardsController : SecureBaseController
	{
		private readonly IRunCardsService _runCardsService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDispatchRecommendationService _dispatchRecommendationService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IFeatureToggleService _featureToggleService;

		public RunCardsController(IRunCardsService runCardsService, ICallsService callsService, IUnitsService unitsService,
			IPersonnelRolesService personnelRolesService, ICustomStateService customStateService,
			IDepartmentGroupsService departmentGroupsService, IDispatchRecommendationService dispatchRecommendationService,
			IAuthorizationService authorizationService, IFeatureToggleService featureToggleService)
		{
			_runCardsService = runCardsService;
			_callsService = callsService;
			_unitsService = unitsService;
			_personnelRolesService = personnelRolesService;
			_customStateService = customStateService;
			_departmentGroupsService = departmentGroupsService;
			_dispatchRecommendationService = dispatchRecommendationService;
			_authorizationService = authorizationService;
			_featureToggleService = featureToggleService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Index()
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return RedirectToAction("Dashboard", "Home", new { area = "User" });

			var model = new RunCardsIndexModel
			{
				RunCards = await _runCardsService.GetAllRunCardsForDepartmentAsync(DepartmentId, true)
			};

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> New()
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return RedirectToAction("Dashboard", "Home", new { area = "User" });

			var model = new EditRunCardModel { IsNew = true, RunCard = new RunCard { DepartmentId = DepartmentId } };
			await PopulateEditModelAsync(model);

			return View("Edit", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Edit(int runCardId)
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return RedirectToAction("Dashboard", "Home", new { area = "User" });

			var card = await _runCardsService.GetRunCardByIdAsync(runCardId);

			if (card == null || card.DepartmentId != DepartmentId)
				return Unauthorized();

			var model = new EditRunCardModel { RunCard = card };
			await PopulateEditModelAsync(model);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Save([FromBody] RunCardEditInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return new StatusCodeResult((int)HttpStatusCode.BadRequest);

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return Json(new { success = false, message = "Run cards are not enabled for this department." });

			if (string.IsNullOrWhiteSpace(input.Name))
				return Json(new { success = false, message = "A run card needs a name." });

			if (input.Triggers == null || !input.Triggers.Any())
				return Json(new { success = false, message = "A run card needs at least one trigger." });

			if (input.AlarmLevels == null || !input.AlarmLevels.Any())
				return Json(new { success = false, message = "A run card needs at least one alarm level." });

			if (input.AlarmLevels.Any(l => l.AlarmLevel < 1))
				return Json(new { success = false, message = "Run card alarm levels start at 1." });

			if (input.AlarmLevels.GroupBy(l => l.AlarmLevel).Any(g => g.Count() > 1))
				return Json(new { success = false, message = "A run card cannot define the same alarm level twice." });

			RunCard card;
			if (input.RunCardId > 0)
			{
				card = await _runCardsService.GetRunCardByIdAsync(input.RunCardId);

				if (card == null || card.DepartmentId != DepartmentId)
					return Unauthorized();

				card.UpdatedOn = DateTime.UtcNow;
				card.UpdatedByUserId = UserId;
			}
			else
			{
				card = new RunCard
				{
					DepartmentId = DepartmentId,
					AddedOn = DateTime.UtcNow,
					AddedByUserId = UserId
				};
			}

			card.Name = input.Name.Trim();
			card.Description = input.Description;
			card.IsDisabled = input.IsDisabled;
			card.DispatchModeOverride = input.DispatchModeOverride;
			card.AutoDispatchOverride = input.AutoDispatchOverride;
			card.MinimumStaffingLevelOverride = input.MinimumStaffingLevelOverride;
			card.HomeStationGroupId = input.HomeStationGroupId;

			card.Triggers = input.Triggers.Select(t => new RunCardTrigger
			{
				RunCardTriggerId = t.RunCardTriggerId,
				RunCardId = card.RunCardId,
				TriggerType = t.TriggerType,
				Priority = t.Priority,
				CallTypeId = t.CallTypeId,
				StartsOn = t.StartsOn,
				EndsOn = t.EndsOn
			}).ToList();

			card.AlarmLevels = input.AlarmLevels.Select(l => new RunCardAlarmLevel
			{
				RunCardAlarmLevelId = l.RunCardAlarmLevelId,
				RunCardId = card.RunCardId,
				AlarmLevel = l.AlarmLevel,
				Name = l.Name,
				UnitRequirements = (l.UnitRequirements ?? new List<RunCardUnitRequirementInput>()).Select(r => new RunCardUnitRequirement
				{
					RunCardUnitRequirementId = r.RunCardUnitRequirementId,
					RunCardAlarmLevelId = l.RunCardAlarmLevelId,
					UnitTypeId = r.UnitTypeId,
					RequiredCount = Math.Max(1, r.RequiredCount),
					SortOrder = r.SortOrder
				}).ToList(),
				RoleRequirements = (l.RoleRequirements ?? new List<RunCardRoleRequirementInput>()).Select(r => new RunCardRoleRequirement
				{
					RunCardRoleRequirementId = r.RunCardRoleRequirementId,
					RunCardAlarmLevelId = l.RunCardAlarmLevelId,
					PersonnelRoleId = r.PersonnelRoleId,
					RequiredCount = Math.Max(1, r.RequiredCount),
					SortOrder = r.SortOrder
				}).ToList()
			}).ToList();

			card.AvailabilitySelections = (input.Selections ?? new List<RunCardSelectionInput>()).Select(s => new RunCardAvailabilitySelection
			{
				RunCardAvailabilitySelectionId = s.RunCardAvailabilitySelectionId,
				RunCardId = card.RunCardId,
				SelectionType = s.SelectionType,
				UnitTypeId = s.UnitTypeId,
				IsCustomState = s.IsCustomState,
				StateId = s.StateId
			}).ToList();

			var saved = await _runCardsService.SaveRunCardAsync(card, cancellationToken);

			return Json(new { success = true, runCardId = saved.RunCardId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Delete([FromForm] int runCardId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var card = await _runCardsService.GetRunCardByIdAsync(runCardId);

			if (card == null || card.DepartmentId != DepartmentId)
				return Json(new { success = false });

			await _runCardsService.DeleteRunCardAsync(runCardId, cancellationToken);

			return Json(new { success = true });
		}

		/// <summary>
		/// Test/simulate endpoint for the editor's preview tab: what would this
		/// priority/type/location dispatch right now?
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Preview(int priority, string type, double? latitude, double? longitude, int alarmLevel = 1)
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return Json(new { success = false });

			var result = await _dispatchRecommendationService.GetRecommendationAsync(new DispatchRecommendationRequest
			{
				DepartmentId = DepartmentId,
				Priority = priority,
				CallTypeName = type,
				Latitude = latitude,
				Longitude = longitude,
				TargetAlarmLevel = alarmLevel
			});

			return Json(new { success = true, result });
		}

		private async Task PopulateEditModelAsync(EditRunCardModel model)
		{
			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId);
			model.CallPriorities = new SelectList(priorities, "DepartmentCallPriorityId", "Name");

			model.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			model.StationGroups = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			model.UnitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>();
			model.PersonnelRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>();

			var unitCustomStates = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId) ?? new List<CustomState>();

			foreach (var unitType in model.UnitTypes)
			{
				var options = new List<StatusOptionModel>();
				var customState = unitType.CustomStatesId.HasValue
					? unitCustomStates.FirstOrDefault(s => s.CustomStateId == unitType.CustomStatesId.Value)
					: null;

				if (customState != null)
				{
					options.AddRange(customState.GetActiveDetails().Select(d => new StatusOptionModel
					{
						StateId = d.CustomStateDetailId,
						IsCustomState = true,
						Text = d.ButtonText
					}));
				}
				else
				{
					options.AddRange(Enum.GetValues(typeof(UnitStateTypes)).Cast<UnitStateTypes>().Select(s => new StatusOptionModel
					{
						StateId = (int)s,
						IsCustomState = false,
						Text = s.ToString()
					}));
				}

				model.UnitStatusOptions[unitType.UnitTypeId] = options;
			}

			var personnelState = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
			if (personnelState != null)
			{
				model.PersonnelStatusOptions = personnelState.GetActiveDetails().Select(d => new StatusOptionModel
				{
					StateId = d.CustomStateDetailId,
					IsCustomState = true,
					Text = d.ButtonText
				}).ToList();
			}
			else
			{
				model.PersonnelStatusOptions = Enum.GetValues(typeof(ActionTypes)).Cast<ActionTypes>().Select(s => new StatusOptionModel
				{
					StateId = (int)s,
					IsCustomState = false,
					Text = s.ToString()
				}).ToList();
			}

			var staffingState = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingState != null)
			{
				model.StaffingOptions = staffingState.GetActiveDetails().Select(d => new StatusOptionModel
				{
					StateId = d.CustomStateDetailId,
					IsCustomState = true,
					Text = d.ButtonText
				}).ToList();
			}
			else
			{
				model.StaffingOptions = Enum.GetValues(typeof(UserStateTypes)).Cast<UserStateTypes>().Select(s => new StatusOptionModel
				{
					StateId = (int)s,
					IsCustomState = false,
					Text = s.ToString()
				}).ToList();
			}
		}
	}
}
