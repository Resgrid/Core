using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.Version3;
using Resgrid.Web.Services.Models.v4.RunCards;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Run cards (CAD-style response plans): CRUD plus a recommendation preview.
	/// Gated behind the Dispatch.RunCards feature flag.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RunCardsController : V4AuthenticatedApiControllerbase
	{
		private readonly IRunCardsService _runCardsService;
		private readonly IDispatchRecommendationService _dispatchRecommendationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IAuthorizationService _authorizationService;

		public RunCardsController(IRunCardsService runCardsService, IDispatchRecommendationService dispatchRecommendationService,
			IFeatureToggleService featureToggleService, IAuthorizationService authorizationService)
		{
			_runCardsService = runCardsService;
			_dispatchRecommendationService = dispatchRecommendationService;
			_featureToggleService = featureToggleService;
			_authorizationService = authorizationService;
		}

		/// <summary>
		/// All run cards for the department (fully hydrated).
		/// </summary>
		[HttpGet("GetAllRunCards")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<List<RunCardData>>> GetAllRunCards()
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return NotFound();

			var cards = await _runCardsService.GetAllRunCardsForDepartmentAsync(DepartmentId);

			return Ok(cards.Select(ConvertRunCardData).ToList());
		}

		/// <summary>
		/// One run card by id.
		/// </summary>
		[HttpGet("GetRunCard")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<RunCardData>> GetRunCard(int runCardId)
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return NotFound();

			var card = await _runCardsService.GetRunCardByIdAsync(runCardId);

			if (card == null || card.DepartmentId != DepartmentId)
				return NotFound();

			return Ok(ConvertRunCardData(card));
		}

		/// <summary>
		/// Creates or updates a run card (child graph is replaced to match the input).
		/// Department admin only.
		/// </summary>
		[HttpPost("SaveRunCard")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<ActionResult> SaveRunCard([FromBody] RunCardData input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Name) || input.Triggers == null || !input.Triggers.Any()
				|| input.AlarmLevels == null || !input.AlarmLevels.Any())
				return BadRequest();

			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return NotFound();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			RunCard card;
			if (input.RunCardId > 0)
			{
				card = await _runCardsService.GetRunCardByIdAsync(input.RunCardId);

				if (card == null || card.DepartmentId != DepartmentId)
					return NotFound();

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
				UnitRequirements = (l.UnitRequirements ?? new List<RunCardUnitRequirementData>()).Select(r => new RunCardUnitRequirement
				{
					RunCardUnitRequirementId = r.RunCardUnitRequirementId,
					RunCardAlarmLevelId = l.RunCardAlarmLevelId,
					UnitTypeId = r.UnitTypeId,
					RequiredCount = Math.Max(1, r.RequiredCount),
					SortOrder = r.SortOrder
				}).ToList(),
				RoleRequirements = (l.RoleRequirements ?? new List<RunCardRoleRequirementData>()).Select(r => new RunCardRoleRequirement
				{
					RunCardRoleRequirementId = r.RunCardRoleRequirementId,
					RunCardAlarmLevelId = l.RunCardAlarmLevelId,
					PersonnelRoleId = r.PersonnelRoleId,
					RequiredCount = Math.Max(1, r.RequiredCount),
					SortOrder = r.SortOrder
				}).ToList()
			}).ToList();

			card.AvailabilitySelections = (input.Selections ?? new List<RunCardSelectionData>()).Select(s => new RunCardAvailabilitySelection
			{
				RunCardAvailabilitySelectionId = s.RunCardAvailabilitySelectionId,
				RunCardId = card.RunCardId,
				SelectionType = s.SelectionType,
				UnitTypeId = s.UnitTypeId,
				IsCustomState = s.IsCustomState,
				StateId = s.StateId
			}).ToList();

			var saved = await _runCardsService.SaveRunCardAsync(card, cancellationToken);

			return Ok(new { runCardId = saved.RunCardId });
		}

		/// <summary>
		/// Deletes a run card. Department admin only.
		/// </summary>
		[HttpDelete("DeleteRunCard")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<ActionResult> DeleteRunCard(int runCardId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var card = await _runCardsService.GetRunCardByIdAsync(runCardId);

			if (card == null || card.DepartmentId != DepartmentId)
				return NotFound();

			await _runCardsService.DeleteRunCardAsync(runCardId, cancellationToken);

			return Ok();
		}

		/// <summary>
		/// Recommendation preview: what would the department's run cards dispatch for
		/// this priority/type/location right now? Nothing is dispatched.
		/// </summary>
		[HttpGet("GetRecommendation")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatchRecommendationResult>> GetRecommendation(int priority, string type, double? latitude, double? longitude, int alarmLevel = 1)
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return NotFound();

			var result = await _dispatchRecommendationService.GetRecommendationAsync(new DispatchRecommendationRequest
			{
				DepartmentId = DepartmentId,
				Priority = priority,
				CallTypeName = type,
				Latitude = latitude,
				Longitude = longitude,
				TargetAlarmLevel = alarmLevel
			});

			return Ok(result);
		}

		private static RunCardData ConvertRunCardData(RunCard card)
		{
			return new RunCardData
			{
				RunCardId = card.RunCardId,
				Name = card.Name,
				Description = card.Description,
				IsDisabled = card.IsDisabled,
				DispatchModeOverride = card.DispatchModeOverride,
				AutoDispatchOverride = card.AutoDispatchOverride,
				MinimumStaffingLevelOverride = card.MinimumStaffingLevelOverride,
				HomeStationGroupId = card.HomeStationGroupId,
				Triggers = (card.Triggers ?? new List<RunCardTrigger>()).Select(t => new RunCardTriggerData
				{
					RunCardTriggerId = t.RunCardTriggerId,
					TriggerType = t.TriggerType,
					Priority = t.Priority,
					CallTypeId = t.CallTypeId,
					StartsOn = t.StartsOn,
					EndsOn = t.EndsOn
				}).ToList(),
				AlarmLevels = (card.AlarmLevels ?? new List<RunCardAlarmLevel>()).Select(l => new RunCardAlarmLevelData
				{
					RunCardAlarmLevelId = l.RunCardAlarmLevelId,
					AlarmLevel = l.AlarmLevel,
					Name = l.Name,
					UnitRequirements = (l.UnitRequirements ?? new List<RunCardUnitRequirement>()).Select(r => new RunCardUnitRequirementData
					{
						RunCardUnitRequirementId = r.RunCardUnitRequirementId,
						UnitTypeId = r.UnitTypeId,
						RequiredCount = r.RequiredCount,
						SortOrder = r.SortOrder
					}).ToList(),
					RoleRequirements = (l.RoleRequirements ?? new List<RunCardRoleRequirement>()).Select(r => new RunCardRoleRequirementData
					{
						RunCardRoleRequirementId = r.RunCardRoleRequirementId,
						PersonnelRoleId = r.PersonnelRoleId,
						RequiredCount = r.RequiredCount,
						SortOrder = r.SortOrder
					}).ToList()
				}).ToList(),
				Selections = (card.AvailabilitySelections ?? new List<RunCardAvailabilitySelection>()).Select(s => new RunCardSelectionData
				{
					RunCardAvailabilitySelectionId = s.RunCardAvailabilitySelectionId,
					SelectionType = s.SelectionType,
					UnitTypeId = s.UnitTypeId,
					IsCustomState = s.IsCustomState,
					StateId = s.StateId
				}).ToList()
			};
		}
	}
}
