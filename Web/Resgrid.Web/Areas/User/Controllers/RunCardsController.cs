using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
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
		private readonly IResponsePackagesService _responsePackagesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IRunCardTemplatesService _runCardTemplatesService;

		public RunCardsController(IRunCardsService runCardsService, ICallsService callsService, IUnitsService unitsService,
			IPersonnelRolesService personnelRolesService, ICustomStateService customStateService,
			IDepartmentGroupsService departmentGroupsService, IDispatchRecommendationService dispatchRecommendationService,
			IAuthorizationService authorizationService, IFeatureToggleService featureToggleService,
			IResponsePackagesService responsePackagesService, IDepartmentSettingsService departmentSettingsService,
			IRunCardTemplatesService runCardTemplatesService)
		{
			_runCardTemplatesService = runCardTemplatesService;
			_responsePackagesService = responsePackagesService;
			_departmentSettingsService = departmentSettingsService;
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
				RunCards = await _runCardsService.GetAllRunCardsForDepartmentAsync(DepartmentId, true),
				ConditionsEnabled = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId)
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

			// A deleted unit type, role, call type, station or status leaves its id on the card,
			// where the editor cannot show it and every save would reject it.
			var model = new EditRunCardModel
			{
				RunCard = card,
				RemovedReferenceCount = await _runCardsService.RemoveDetachedReferencesAsync(card)
			};
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

			// Conditional run cards: with the feature off the editor shows no graphs or packages, so keep the stored ones.
			var conditionsEnabled = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId);
			var storedLevels = card.AlarmLevels;

			if (conditionsEnabled)
			{
				card.RelaxUnfilledRows = input.RelaxUnfilledRows;
				card.WaitingCallAutoDispatchOverride = input.WaitingCallAutoDispatchOverride.HasValue ? Math.Max(0, Math.Min(1, input.WaitingCallAutoDispatchOverride.Value)) : (int?)null;
			}

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
				}).ToList(),
				ResponsePackageId = conditionsEnabled ? l.ResponsePackageId : null,
				DecisionNodes = conditionsEnabled ? ToDecisionNodes(l) : new List<RunCardDecisionNode>()
			}).ToList();

			if (!conditionsEnabled)
				RunCardDecisionGraph.CarryOverConditionalParts(storedLevels, card.AlarmLevels);

			card.AvailabilitySelections = (input.Selections ?? new List<RunCardSelectionInput>()).Select(s => new RunCardAvailabilitySelection
			{
				RunCardAvailabilitySelectionId = s.RunCardAvailabilitySelectionId,
				RunCardId = card.RunCardId,
				SelectionType = s.SelectionType,
				UnitTypeId = s.UnitTypeId,
				IsCustomState = s.IsCustomState,
				StateId = s.StateId
			}).ToList();

			RunCard saved;
			try
			{
				saved = await _runCardsService.SaveRunCardAsync(card, cancellationToken);
			}
			catch (RunCardValidationException ex)
			{
				// Decision graph and package problems read as they are ("Alarm level 1: 'PIT first?' has no No branch.").
				return BadRequest(new { success = false, message = ex.Message });
			}
			catch (ArgumentException)
			{
				// The service rejects ids the department does not own, e.g. a unit type deleted
				// while this editor was open; reloading the editor drops those.
				return BadRequest(new { success = false, message = "The run card contains invalid references. Reload the editor and review the card." });
			}

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
		public async Task<IActionResult> Preview(int priority, string type, double? latitude, double? longitude, int alarmLevel = 1, int? runCardId = null)
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
				TargetAlarmLevel = alarmLevel,
				// The card being edited, disabled or not; the service ignores a card of another department.
				RunCardId = runCardId > 0 ? runCardId : null
			});

			return Json(new { success = true, result });
		}

		/// <summary>The posted decision graph of a level as nodes; ids come from the stored rows matched by key on save.</summary>
		private static List<RunCardDecisionNode> ToDecisionNodes(RunCardAlarmLevelInput level)
		{
			return (level.DecisionNodes ?? new List<RunCardDecisionNodeInput>()).Where(n => n != null).Select(n => new RunCardDecisionNode
			{
				RunCardAlarmLevelId = level.RunCardAlarmLevelId,
				NodeKey = string.IsNullOrWhiteSpace(n.NodeKey) ? null : n.NodeKey.Trim(),
				IsRoot = n.IsRoot,
				NodeKind = n.NodeKind,
				Label = n.Label?.Trim(),
				ConditionType = n.ConditionType,
				SubjectStatusFilter = n.SubjectStatusFilter,
				SubjectStatusBaseTypes = n.SubjectStatusBaseTypes,
				ReferenceStatusFilter = n.ReferenceStatusFilter,
				ReferenceStatusBaseTypes = n.ReferenceStatusBaseTypes,
				ThresholdSeconds = n.ThresholdSeconds,
				RequiredCount = Math.Max(1, n.RequiredCount),
				YesNodeKey = string.IsNullOrWhiteSpace(n.YesNodeKey) ? null : n.YesNodeKey.Trim(),
				NoNodeKey = string.IsNullOrWhiteSpace(n.NoNodeKey) ? null : n.NoNodeKey.Trim(),
				ResponsePackageId = n.ResponsePackageId,
				AdviceText = n.AdviceText?.Trim(),
				SortOrder = n.SortOrder,
				SubjectTargets = ToTargets(n.SubjectTargets),
				ReferenceTargets = ToTargets(n.ReferenceTargets)
			}).ToList();
		}

		private static List<RunCardTarget> ToTargets(IEnumerable<RunCardTargetInput> targets)
		{
			return (targets ?? Enumerable.Empty<RunCardTargetInput>()).Where(t => t != null).Select(t => new RunCardTarget
			{
				TargetKind = t.TargetKind,
				TargetRefId = t.TargetKind == (int)RunCardTargetKinds.AnyUnit || t.TargetKind == (int)RunCardTargetKinds.AnyPerson ? null : t.TargetRefId
			}).ToList();
		}

		#region Templates

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Templates()
		{
			if (!await ConditionsEnabledAsync())
				return RedirectToAction("Index", "RunCards", new { area = "User" });

			var model = new RunCardTemplatesModel
			{
				Templates = Resgrid.Model.RunCards.RunCardTemplateCatalog.All.ToList(),
				Suggestions = await _runCardTemplatesService.GetSuggestionsAsync(DepartmentId),
				UnitTypesWithoutClass = (await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>())
					.Count(t => t.BaseType <= 0)
			};

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> TemplatePreview(string templateId)
		{
			if (!await ConditionsEnabledAsync())
				return RedirectToAction("Index", "RunCards", new { area = "User" });

			var preview = await _runCardTemplatesService.PreviewAsync(DepartmentId, templateId);

			if (preview == null)
				return RedirectToAction("Templates", "RunCards", new { area = "User" });

			return View(preview);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ApplyTemplate([FromForm] string templateId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			if (!await ConditionsEnabledAsync())
				return RedirectToAction("Index", "RunCards", new { area = "User" });

			var result = await _runCardTemplatesService.ApplyAsync(DepartmentId, templateId, UserId, cancellationToken);

			if (!result.Success || !result.RunCardId.HasValue)
			{
				TempData["TemplateError"] = result.Message;
				return RedirectToAction("TemplatePreview", "RunCards", new { area = "User", templateId });
			}

			TempData["TemplateApplied"] = result.Message;
			return RedirectToAction("Edit", "RunCards", new { area = "User", runCardId = result.RunCardId.Value });
		}

		#endregion

		#region Response packages

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Packages()
		{
			if (!await ConditionsEnabledAsync())
				return RedirectToAction("Index", "RunCards", new { area = "User" });

			var model = new ResponsePackagesIndexModel
			{
				Packages = await _responsePackagesService.GetPackagesForDepartmentAsync(DepartmentId, true)
			};

			foreach (var package in model.Packages)
				model.Usage[package.ResponsePackageId] = await _responsePackagesService.GetPackageUsageAsync(DepartmentId, package.ResponsePackageId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewPackage()
		{
			if (!await ConditionsEnabledAsync())
				return RedirectToAction("Index", "RunCards", new { area = "User" });

			var model = new EditResponsePackageModel { IsNew = true, Package = new ResponsePackage { DepartmentId = DepartmentId } };
			await PopulatePackageModelAsync(model);

			return View("EditPackage", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditPackage(int responsePackageId)
		{
			if (!await ConditionsEnabledAsync())
				return RedirectToAction("Index", "RunCards", new { area = "User" });

			var package = await _responsePackagesService.GetPackageByIdAsync(DepartmentId, responsePackageId, true);

			if (package == null)
				return Unauthorized();

			var model = new EditResponsePackageModel
			{
				Package = package,
				Usage = await _responsePackagesService.GetPackageUsageAsync(DepartmentId, responsePackageId)
			};
			await PopulatePackageModelAsync(model);

			return View("EditPackage", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> SavePackage([FromBody] ResponsePackageEditInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return new StatusCodeResult((int)HttpStatusCode.BadRequest);

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			if (!await ConditionsEnabledAsync())
				return Json(new { success = false, message = "Conditional run cards are not enabled for this department." });

			var package = new ResponsePackage
			{
				ResponsePackageId = input.ResponsePackageId,
				DepartmentId = DepartmentId,
				Name = input.Name?.Trim(),
				Description = input.Description,
				IsRetired = input.IsRetired,
				Rows = (input.Rows ?? new List<ResponsePackageRowInput>()).Where(r => r != null).Select(r => new ResponsePackageRow
				{
					ResponsePackageRowId = r.ResponsePackageRowId,
					ResponsePackageId = input.ResponsePackageId,
					Label = r.Label?.Trim(),
					FillMode = r.FillMode,
					RequiredCount = r.RequiredCount,
					StatusFilter = r.StatusFilter,
					StatusBaseTypes = r.StatusBaseTypes,
					ResponseCodeId = r.ResponseCodeId,
					SubstitutePackageId = r.SubstitutePackageId,
					KeepOutstanding = r.KeepOutstanding,
					SortOrder = r.SortOrder,
					Targets = ToTargets(r.Targets)
				}).ToList()
			};

			try
			{
				var saved = await _responsePackagesService.SavePackageAsync(package, UserId, cancellationToken);
				return Json(new { success = true, responsePackageId = saved.ResponsePackageId });
			}
			catch (RunCardValidationException ex)
			{
				return BadRequest(new { success = false, message = ex.Message });
			}
			catch (ArgumentException)
			{
				return BadRequest(new { success = false, message = "The package contains invalid references. Reload the editor and review the package." });
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeletePackage([FromForm] int responsePackageId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			try
			{
				return Json(new { success = await _responsePackagesService.DeletePackageAsync(DepartmentId, responsePackageId, cancellationToken) });
			}
			catch (InvalidOperationException ex)
			{
				// Still used by a card, an outcome or another package's substitute: deleting it would silently empty them.
				return Json(new { success = false, message = ex.Message });
			}
		}

		private async Task PopulatePackageModelAsync(EditResponsePackageModel model)
		{
			model.UnitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>();
			model.PersonnelRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>();
			model.OtherPackages = (await _responsePackagesService.GetPackagesForDepartmentAsync(DepartmentId) ?? new List<ResponsePackage>())
				.Where(p => p.ResponsePackageId != model.Package.ResponsePackageId)
				.ToList();
			model.ResponseCodes = await _departmentSettingsService.GetDispatchResponseCodesAsync(DepartmentId) ?? new DispatchResponseCodesConfig();
		}

		private async Task<bool> ConditionsEnabledAsync()
		{
			return await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId)
				&& await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId);
		}

		#endregion

		private async Task PopulateEditModelAsync(EditRunCardModel model)
		{
			model.ConditionsEnabled = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId);

			if (model.ConditionsEnabled)
				model.ResponsePackages = await _responsePackagesService.GetPackagesForDepartmentAsync(DepartmentId) ?? new List<ResponsePackage>();

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

			// A status meaning counts every status with that base type, built-in or the department's own
			// (RunCardSelectionMeaning), so one selection keeps working when a unit type's status set changes.
			var statusMeanings = Enum.GetValues(typeof(ActionBaseTypes)).Cast<ActionBaseTypes>().Where(x => x != ActionBaseTypes.None).ToList();

			foreach (var options in model.UnitStatusOptions.Values)
				options.AddRange(StatusMeaningOptions(statusMeanings.Where(x => x != ActionBaseTypes.NotResponding).Select(x => ((int)x, x.GetDisplayString()))));

			model.PersonnelStatusOptions.AddRange(StatusMeaningOptions(statusMeanings.Select(x => ((int)x, x.GetDisplayString()))));
			model.StaffingOptions.AddRange(StatusMeaningOptions(Enum.GetValues(typeof(StaffingBaseTypes)).Cast<StaffingBaseTypes>()
				.Where(x => x != StaffingBaseTypes.None).Select(x => ((int)x, x.GetDisplayString()))));
		}

		private static IEnumerable<StatusOptionModel> StatusMeaningOptions(IEnumerable<(int Value, string Name)> meanings)
		{
			return meanings.Select(x => new StatusOptionModel
			{
				StateId = RunCardSelectionMeaning.StateIdFor(x.Value),
				IsCustomState = false,
				Text = $"Any \"{x.Name}\" status"
			});
		}
	}
}
