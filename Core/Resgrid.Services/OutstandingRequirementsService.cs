using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Calls awaiting resources (conditional run cards). Requirements are written when a call is saved from its
	/// recommendation and resolve only by fulfilment, a person's dismiss, or a person closing the call; reads reconcile
	/// against the call's dispatches, so no dispatch path has to be hooked to resolve them.
	/// </summary>
	public class OutstandingRequirementsService : IOutstandingRequirementsService
	{
		private static readonly JsonSerializerSettings AlertJson = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };

		private readonly ICallOutstandingRequirementsRepository _requirementsRepository;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IRunCardsService _runCardsService;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly IQueueService _queueService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IGeoService _geoService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IFeatureToggleService _featureToggleService;

		public OutstandingRequirementsService(ICallOutstandingRequirementsRepository requirementsRepository, ICallsService callsService,
			IUnitsService unitsService, ICustomStateService customStateService, IDepartmentSettingsService departmentSettingsService,
			IRunCardsService runCardsService, ICallDispatchStatusService callDispatchStatusService, IQueueService queueService,
			IEventAggregator eventAggregator, IDepartmentGroupsService departmentGroupsService, IGeoService geoService,
			IPersonnelRolesService personnelRolesService, IFeatureToggleService featureToggleService)
		{
			_featureToggleService = featureToggleService;
			_personnelRolesService = personnelRolesService;
			_requirementsRepository = requirementsRepository;
			_callsService = callsService;
			_unitsService = unitsService;
			_customStateService = customStateService;
			_departmentSettingsService = departmentSettingsService;
			_runCardsService = runCardsService;
			_callDispatchStatusService = callDispatchStatusService;
			_queueService = queueService;
			_eventAggregator = eventAggregator;
			_departmentGroupsService = departmentGroupsService;
			_geoService = geoService;
		}

		public async Task<List<CallOutstandingRequirement>> CreateFromRecommendationAsync(Call call, DispatchRecommendationResult result, string userId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var created = new List<CallOutstandingRequirement>();
			var decision = result?.Decision;

			if (call == null || call.CallId <= 0 || decision == null || call.State != (int)CallStates.Active || !result.MatchedRunCardId.HasValue)
				return created;

			var outstanding = decision.Fallbacks
				.Where(f => f.Outstanding && (f.Step == RequirementFallbackSteps.Substitute || f.Step == RequirementFallbackSteps.Shortfall))
				.ToList();

			if (!outstanding.Any())
				return created;

			var existing = (await _requirementsRepository.GetByCallIdAsync(call.CallId) ?? Enumerable.Empty<CallOutstandingRequirement>()).ToList();
			var codes = await _departmentSettingsService.GetDispatchResponseCodesAsync(call.DepartmentId) ?? new DispatchResponseCodesConfig();

			foreach (var fallback in outstanding)
			{
				var row = decision.ResolvedRows.FirstOrDefault(r => r.Scope == fallback.RequirementScope && r.RequirementId == fallback.RequirementId);
				var missing = fallback.RequiredCount - fallback.FilledCount;

				if (row == null || missing <= 0)
					continue;

				var packageRowId = row.Scope == RequirementScopes.Package ? row.RequirementId : (int?)null;

				// Re-evaluating or adding resources to the same call must not stack a second copy of a requirement it still has.
				if (existing.Any(e => e.IsOpen && e.RunCardId == result.MatchedRunCardId && e.AlarmLevel == result.AlarmLevel
					&& e.ResponsePackageRowId == packageRowId && string.Equals(e.Label, row.Label, StringComparison.OrdinalIgnoreCase)))
					continue;

				var code = row.ResponseCodeId.HasValue ? codes.Find(row.ResponseCodeId) : null;

				var requirement = await _requirementsRepository.SaveOrUpdateAsync(new CallOutstandingRequirement
				{
					DepartmentId = call.DepartmentId,
					CallId = call.CallId,
					Label = Truncate(row.Label, 200),
					TargetsJson = JsonConvert.SerializeObject(row.Targets ?? new List<RunCardTarget>()),
					RequiredCount = missing,
					StatusFilter = (int)row.StatusFilter,
					StatusBaseTypes = row.StatusBaseTypes,
					ResponseCodeId = code?.Id,
					ResponseCode = code?.Code,
					RunCardId = result.MatchedRunCardId,
					AlarmLevel = result.AlarmLevel,
					ResponsePackageRowId = packageRowId,
					SentInstead = Truncate(fallback.SentInstead, 500),
					CreatedOn = DateTime.UtcNow,
					CreatedByUserId = userId,
					Resolution = (int)OutstandingRequirementResolutions.Open
				}, cancellationToken, true);

				created.Add(requirement);
			}

			if (created.Any())
				await RaiseChangedAsync(call.DepartmentId, call.CallId);

			return created;
		}

		public async Task<List<CallOutstandingRequirement>> GetForCallAsync(int departmentId, int callId, bool includeResolved = false,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var requirements = (await _requirementsRepository.GetByCallIdAsync(callId) ?? Enumerable.Empty<CallOutstandingRequirement>())
				.Where(r => r != null && r.DepartmentId == departmentId)
				.OrderBy(r => r.CreatedOn)
				.ToList();

			if (requirements.Any(r => r.IsOpen))
				await ReconcileAsync(departmentId, callId, requirements, null, cancellationToken);

			return includeResolved ? requirements : requirements.Where(r => r.IsOpen).ToList();
		}

		public async Task<bool> HasOpenRequirementsAsync(int departmentId, int callId, CancellationToken cancellationToken = default(CancellationToken))
		{
			return (await GetForCallAsync(departmentId, callId, false, cancellationToken)).Any();
		}

		public async Task<List<AwaitingResourcesCall>> GetAwaitingCallsAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var awaiting = new List<AwaitingResourcesCall>();
			var open = (await _requirementsRepository.GetOpenByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<CallOutstandingRequirement>())
				.Where(r => r != null && r.IsOpen)
				.ToList();

			if (!open.Any())
				return awaiting;

			var pool = await BuildAvailableUnitPoolAsync(departmentId);
			var codes = await _departmentSettingsService.GetDispatchResponseCodesAsync(departmentId) ?? new DispatchResponseCodesConfig();

			foreach (var group in open.GroupBy(r => r.CallId))
			{
				var call = await _callsService.GetCallByIdAsync(group.Key);
				var requirements = group.OrderBy(r => r.CreatedOn).ToList();

				await ReconcileAsync(departmentId, group.Key, requirements, call, cancellationToken);

				requirements = requirements.Where(r => r.IsOpen).ToList();

				if (call == null || !requirements.Any())
					continue;

				var priority = await _callsService.GetCallPrioritiesByIdAsync(departmentId, call.Priority);
				var callPoint = GeoMath.ParseLatLonString(call.GeoLocationData);
				var onCall = new HashSet<int>((call.UnitDispatches ?? new List<CallDispatchUnit>()).Select(d => d.UnitId));

				var entry = new AwaitingResourcesCall
				{
					CallId = call.CallId,
					CallNumber = call.Number,
					CallName = call.Name,
					Priority = call.Priority,
					PriorityName = priority?.Name,
					PriorityColor = priority?.Color,
					Address = call.Address,
					LoggedOn = call.LoggedOn
				};

				foreach (var requirement in requirements)
				{
					var targets = ReadTargets(requirement);
					var item = new AwaitingResourceRequirement
					{
						CallOutstandingRequirementId = requirement.CallOutstandingRequirementId,
						Label = requirement.Label,
						RequiredCount = requirement.RequiredCount,
						ResolvedCount = requirement.ResolvedCount,
						SentInstead = requirement.SentInstead,
						ResponseCode = requirement.ResponseCode,
						ResponseCodeId = requirement.ResponseCodeId,
						ResponseCodeColor = codes.Find(requirement.ResponseCodeId)?.Color,
						CreatedOn = requirement.CreatedOn
					};

					foreach (var unit in pool.Where(u => !onCall.Contains(u.Unit.UnitId) && RunCardTargetRules.MatchesUnit(targets, u.UnitType.UnitTypeId, u.UnitType.BaseType)))
					{
						item.AvailableUnits.Add(new AwaitingCandidateUnit
						{
							UnitId = unit.Unit.UnitId,
							UnitName = unit.Unit.Name,
							UnitTypeName = unit.UnitType.Type,
							ResponseSeconds = await EstimateResponseSecondsAsync(departmentId, unit, callPoint)
						});
					}

					item.AvailableUnits = item.AvailableUnits.OrderBy(u => u.ResponseSeconds.HasValue ? 0 : 1).ThenBy(u => u.ResponseSeconds ?? 0).ThenBy(u => u.UnitName).ToList();
					entry.Requirements.Add(item);
				}

				awaiting.Add(entry);
			}

			return awaiting.OrderBy(a => a.Priority == 0 ? int.MaxValue : a.Priority).ThenBy(a => a.LoggedOn).ToList();
		}

		public async Task<int> GetAwaitingCallCountAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var open = (await _requirementsRepository.GetOpenByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<CallOutstandingRequirement>())
				.Where(r => r != null && r.IsOpen)
				.ToList();
			var count = 0;

			foreach (var group in open.GroupBy(r => r.CallId))
			{
				var requirements = group.ToList();
				await ReconcileAsync(departmentId, group.Key, requirements, null, cancellationToken);

				if (requirements.Any(r => r.IsOpen))
					count++;
			}

			return count;
		}

		public async Task<bool> DismissAsync(int departmentId, int callOutstandingRequirementId, string userId, string note,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var requirement = await _requirementsRepository.GetByIdAsync(callOutstandingRequirementId);

			if (requirement == null || requirement.DepartmentId != departmentId || !requirement.IsOpen)
				return false;

			requirement.ResolvedOn = DateTime.UtcNow;
			requirement.ResolvedByUserId = userId;
			requirement.Resolution = (int)OutstandingRequirementResolutions.Dismissed;
			requirement.ResolutionNote = Truncate(note?.Trim(), 1000);
			requirement.ClaimedOn = null;
			requirement.ClaimedByUnitId = null;

			await _requirementsRepository.SaveOrUpdateAsync(requirement, cancellationToken, true);
			await RaiseChangedAsync(departmentId, requirement.CallId);

			return true;
		}

		public async Task OnUnitStateSavedAsync(int departmentId, UnitState saved, UnitState previous, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (saved == null)
				return;

			try
			{
				// Every status change in every department lands here; the cached flag keeps departments without conditional
				// run cards from paying a query for it.
				if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, departmentId))
					return;

				var open = (await _requirementsRepository.GetOpenByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<CallOutstandingRequirement>())
					.Where(r => r != null && r.IsOpen)
					.ToList();

				if (!open.Any())
					return;

				var details = await BuildUnitStatusDetailsAsync(departmentId);

				// Only the change to a dispatchable status matters: a unit re-reporting Available must not alert again.
				if (!IsDispatchable(saved.State, details) || (previous != null && IsDispatchable(previous.State, details)))
					return;

				var unit = await _unitsService.GetUnitByIdAsync(saved.UnitId);
				var unitType = await ResolveUnitTypeAsync(departmentId, unit);

				if (unit == null || unit.DepartmentId != departmentId || unitType == null)
					return;

				var matches = new List<Tuple<Call, CallOutstandingRequirement, double?>>();
				var candidate = new AvailableUnit { Unit = unit, UnitType = unitType, InQuarters = UnitResponseOrigin.IsInQuarters(saved.State, details) };

				foreach (var group in open.GroupBy(r => r.CallId))
				{
					var call = await _callsService.GetCallByIdAsync(group.Key);
					var requirements = group.OrderBy(r => r.CreatedOn).ToList();

					await ReconcileAsync(departmentId, group.Key, requirements, call, cancellationToken);

					if (call == null || call.State != (int)CallStates.Active)
						continue;

					if ((call.UnitDispatches ?? new List<CallDispatchUnit>()).Any(d => d.UnitId == unit.UnitId))
						continue;

					var requirement = requirements.FirstOrDefault(r => r.IsOpen && RunCardTargetRules.MatchesUnit(ReadTargets(r), unitType.UnitTypeId, unitType.BaseType));

					if (requirement == null)
						continue;

					matches.Add(Tuple.Create(call, requirement, await EstimateResponseSecondsAsync(departmentId, candidate, GeoMath.ParseLatLonString(call.GeoLocationData))));
				}

				if (!matches.Any())
					return;

				// Highest priority, then oldest, first: the order the alert lists them and auto-dispatch considers them.
				matches = matches.OrderBy(m => m.Item1.Priority == 0 ? int.MaxValue : m.Item1.Priority).ThenBy(m => m.Item1.LoggedOn).ToList();

				if (await TryAutoDispatchAsync(departmentId, candidate, matches, cancellationToken))
					return;

				var now = DateTime.UtcNow;
				var alert = new OutstandingRequirementsAlert
				{
					Kind = OutstandingRequirementsAlertKinds.UnitAvailable,
					UnitId = unit.UnitId,
					UnitName = unit.Name,
					UnitTypeName = unitType.Type,
					Message = matches.Count == 1
						? $"{unit.Name} available: call {matches[0].Item1.Number} is waiting for {matches[0].Item2.Label}."
						: $"{unit.Name} available: {matches.Count} calls are waiting for a {unitType.Type}.",
					Calls = matches.Select(m => new OutstandingRequirementsAlertCall
					{
						CallId = m.Item1.CallId,
						CallNumber = m.Item1.Number,
						CallName = m.Item1.Name,
						Priority = m.Item1.Priority,
						Label = m.Item2.Label,
						ResponseSeconds = m.Item3,
						AgeMinutes = (int)Math.Max(0, (now - m.Item2.CreatedOn).TotalMinutes)
					}).ToList()
				};

				await RaiseAsync(departmentId, matches.Count == 1 ? matches[0].Item1.CallId : 0, alert);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Checking calls awaiting resources after unit {saved.UnitId}'s status change failed.");
			}
		}

		public async Task<AwaitingDispatchResult> DispatchUnitToCallAsync(int departmentId, int callId, int unitId, string userId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null || call.DepartmentId != departmentId)
				return new AwaitingDispatchResult { Success = false, Message = "Call not found." };

			if (call.State != (int)CallStates.Active)
				return new AwaitingDispatchResult { Success = false, Message = "Only active calls can have resources added." };

			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit == null || unit.DepartmentId != departmentId)
				return new AwaitingDispatchResult { Success = false, Message = "Unit not found." };

			var requirements = (await _requirementsRepository.GetByCallIdAsync(callId) ?? Enumerable.Empty<CallOutstandingRequirement>())
				.Where(r => r != null && r.DepartmentId == departmentId && r.IsOpen)
				.OrderBy(r => r.CreatedOn)
				.ToList();
			var unitType = await ResolveUnitTypeAsync(departmentId, unit);
			var requirement = unitType == null ? null : requirements.FirstOrDefault(r => RunCardTargetRules.MatchesUnit(ReadTargets(r), unitType.UnitTypeId, unitType.BaseType));

			var resolved = await AddUnitAndResolveAsync(departmentId, call, unit, requirement, userId, OutstandingRequirementResolutions.Dispatched, cancellationToken);

			return new AwaitingDispatchResult
			{
				Success = true,
				ResolvedRequirements = resolved,
				Message = resolved > 0 ? $"{unit.Name} dispatched; the call no longer awaits {requirement?.Label}." : $"{unit.Name} dispatched."
			};
		}

		#region Internals

		/// <summary>
		/// What the response estimates of one listing share (the service is per request): the department's recommendation
		/// settings, latest unit fixes, and station points, which can fall through to address geocoding.
		/// </summary>
		private sealed class EstimateInputs
		{
			public int DepartmentId { get; set; }
			public DispatchRecommendationConfig Config { get; set; }
			public Dictionary<int, UnitsLocation> LatestFixes { get; set; }
			public Dictionary<int, GeoMath.GeoPoint?> StationPoints { get; set; } = new Dictionary<int, GeoMath.GeoPoint?>();
		}

		private EstimateInputs _estimateInputs;

		private sealed class AvailableUnit
		{
			public Unit Unit { get; set; }
			public UnitType UnitType { get; set; }
			public bool InQuarters { get; set; }
		}

		/// <summary>
		/// Resolves open requirements the call now covers: a matching unit or person dispatched after the requirement was
		/// written (each resource counted once, oldest requirement first), or the call having been closed by a person.
		/// </summary>
		private async Task ReconcileAsync(int departmentId, int callId, List<CallOutstandingRequirement> requirements, Call call, CancellationToken cancellationToken)
		{
			if (!requirements.Any(r => r.IsOpen))
				return;

			call = call ?? await _callsService.GetCallByIdAsync(callId);
			var now = DateTime.UtcNow;
			var changed = false;

			if (call == null || call.DepartmentId != departmentId || call.IsDeleted || call.State != (int)CallStates.Active)
			{
				foreach (var requirement in requirements.Where(r => r.IsOpen))
				{
					requirement.ResolvedOn = call?.ClosedOn ?? now;
					requirement.ResolvedByUserId = call?.ClosedByUserId;
					requirement.Resolution = (int)OutstandingRequirementResolutions.CallClosed;
					await _requirementsRepository.SaveOrUpdateAsync(requirement, cancellationToken, true);
					changed = true;
				}

				if (changed)
					await RaiseChangedAsync(departmentId, callId);

				return;
			}

			call = await _callsService.PopulateCallData(call, true, false, false, false, true, false, false, false, false);

			var unitTypes = (await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>())
				.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Type))
				.GroupBy(t => t.Type.Trim(), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var units = (await _unitsService.GetUnitsForDepartmentUnlimitedAsync(departmentId) ?? new List<Unit>()).ToDictionary(u => u.UnitId, u => u);
			Dictionary<string, List<PersonnelRole>> roles = null;
			var usedUnits = new HashSet<int>();
			var usedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var requirement in requirements.Where(r => r.IsOpen).OrderBy(r => r.CreatedOn))
			{
				var targets = ReadTargets(requirement);

				foreach (var dispatch in (call.UnitDispatches ?? new List<CallDispatchUnit>()).Where(d => d.DispatchedOn > requirement.CreatedOn).OrderBy(d => d.DispatchedOn))
				{
					if (requirement.ResolvedCount >= requirement.RequiredCount || usedUnits.Contains(dispatch.UnitId))
						continue;

					if (!units.TryGetValue(dispatch.UnitId, out var unit) || string.IsNullOrWhiteSpace(unit.Type) || !unitTypes.TryGetValue(unit.Type.Trim(), out var type))
						continue;

					if (!RunCardTargetRules.MatchesUnit(targets, type.UnitTypeId, type.BaseType))
						continue;

					usedUnits.Add(dispatch.UnitId);
					requirement.ResolvedCount++;
					requirement.ResolvedByUnitId = dispatch.UnitId;
					changed = true;
				}

				if (requirement.ResolvedCount < requirement.RequiredCount && RunCardTargetRules.HasPersonTargets(targets))
				{
					roles = roles ?? await GetRolesAsync(departmentId);

					foreach (var dispatch in (call.Dispatches ?? new List<CallDispatch>()).Where(d => d.DispatchedOn > requirement.CreatedOn && !string.IsNullOrWhiteSpace(d.UserId)).OrderBy(d => d.DispatchedOn))
					{
						if (requirement.ResolvedCount >= requirement.RequiredCount || usedUsers.Contains(dispatch.UserId))
							continue;

						roles.TryGetValue(dispatch.UserId, out var userRoles);

						if (!RunCardTargetRules.MatchesPerson(targets, (userRoles ?? new List<PersonnelRole>()).Select(r => r.PersonnelRoleId)))
							continue;

						usedUsers.Add(dispatch.UserId);
						requirement.ResolvedCount++;
						changed = true;
					}
				}

				if (requirement.ResolvedCount >= requirement.RequiredCount)
				{
					requirement.ResolvedOn = now;
					requirement.Resolution = (int)OutstandingRequirementResolutions.Dispatched;
					requirement.ClaimedOn = null;
					requirement.ClaimedByUnitId = null;
				}

				if (changed)
					await _requirementsRepository.SaveOrUpdateAsync(requirement, cancellationToken, true);
			}

			if (changed)
				await RaiseChangedAsync(departmentId, callId);
		}

		private async Task<bool> TryAutoDispatchAsync(int departmentId, AvailableUnit candidate, List<Tuple<Call, CallOutstandingRequirement, double?>> matches,
			CancellationToken cancellationToken)
		{
			var config = await _departmentSettingsService.GetDispatchRecommendationConfigAsync(departmentId) ?? new DispatchRecommendationConfig();
			Dictionary<int, DateTime> lastDispatched = null;

			foreach (var match in matches)
			{
				var call = match.Item1;
				var requirement = match.Item2;
				var responseSeconds = match.Item3;
				var card = requirement.RunCardId.HasValue ? await _runCardsService.GetRunCardByIdAsync(requirement.RunCardId.Value) : null;
				var enabled = card?.WaitingCallAutoDispatchOverride.HasValue == true ? card.WaitingCallAutoDispatchOverride.Value == 1 : config.WaitingCallAutoDispatch;

				if (!enabled)
					continue;

				// The limit applies to automatic dispatch only; a unit too far away (or unmeasurable) is alerted instead.
				if (config.WaitingCallMaxResponseSeconds > 0 && (!responseSeconds.HasValue || responseSeconds.Value > config.WaitingCallMaxResponseSeconds))
					continue;

				if (config.RestPeriodMinutes > 0)
				{
					lastDispatched = lastDispatched ?? await _runCardsService.GetLastUnitDispatchTimesAsync(departmentId);

					if (lastDispatched.TryGetValue(candidate.Unit.UnitId, out var last) && (DateTime.UtcNow - last).TotalMinutes < config.RestPeriodMinutes)
						return false;
				}

				// Re-read: the status may have moved on since the change that triggered this.
				var latest = await _unitsService.GetLastUnitStateByUnitIdAsync(candidate.Unit.UnitId);
				if (latest == null || !IsDispatchable(latest.State, await BuildUnitStatusDetailsAsync(departmentId)))
					return false;

				// One unit, one call: two units freeing together cannot both take the row, nor one unit two calls.
				if (!await _requirementsRepository.TryClaimAsync(requirement.CallOutstandingRequirementId, departmentId, candidate.Unit.UnitId, DateTime.UtcNow, cancellationToken))
					continue;

				try
				{
					var systemUserId = latest.SetByUserId ?? call.ReportingUserId;
					await AddUnitAndResolveAsync(departmentId, call, candidate.Unit, requirement, systemUserId, OutstandingRequirementResolutions.AutoDispatched, cancellationToken);

					await _callsService.SaveCallNoteAsync(new CallNote
					{
						CallId = call.CallId,
						UserId = systemUserId,
						Note = $"Auto-dispatched {candidate.Unit.Name}: the call was waiting for {requirement.Label}" + (card != null ? $" (run card {card.Name})." : "."),
						Timestamp = DateTime.UtcNow
					}, cancellationToken);

					await RaiseAsync(departmentId, call.CallId, new OutstandingRequirementsAlert
					{
						Kind = OutstandingRequirementsAlertKinds.AutoDispatched,
						UnitId = candidate.Unit.UnitId,
						UnitName = candidate.Unit.Name,
						UnitTypeName = candidate.UnitType.Type,
						Message = $"{candidate.Unit.Name} auto-dispatched to call {call.Number} (was waiting for {requirement.Label}).",
						Calls = new List<OutstandingRequirementsAlertCall>
						{
							new OutstandingRequirementsAlertCall { CallId = call.CallId, CallNumber = call.Number, CallName = call.Name, Priority = call.Priority, Label = requirement.Label, ResponseSeconds = responseSeconds }
						}
					});

					return true;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Auto-dispatching unit {candidate.Unit.UnitId} to call {call.CallId} failed; dispatchers are alerted instead.");
					await _requirementsRepository.ReleaseClaimAsync(requirement.CallOutstandingRequirementId, departmentId, candidate.Unit.UnitId, cancellationToken);
					return false;
				}
			}

			return false;
		}

		/// <summary>
		/// Adds the unit to the call the way escalation does (additive, dispatch status for the new unit, broadcast to the
		/// new unit only), then resolves the requirement it was sent for. Returns how many requirements resolved.
		/// </summary>
		private async Task<int> AddUnitAndResolveAsync(int departmentId, Call call, Unit unit, CallOutstandingRequirement requirement, string userId,
			OutstandingRequirementResolutions resolution, CancellationToken cancellationToken)
		{
			call = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false);

			if (call.UnitDispatches == null)
				call.UnitDispatches = new Collection<CallDispatchUnit>();

			var resolved = 0;

			if (call.UnitDispatches.All(d => d.UnitId != unit.UnitId))
			{
				call.UnitDispatches.Add(new CallDispatchUnit
				{
					CallId = call.CallId,
					UnitId = unit.UnitId,
					DispatchedOn = DateTime.UtcNow,
					ResponseCodeId = requirement?.ResponseCodeId,
					ResponseCode = requirement?.ResponseCode
				});

				var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);
				var unitIds = new List<int> { unit.UnitId };

				await _callDispatchStatusService.ApplyDispatchStatusesAsync(savedCall, null, unitIds, cancellationToken);

				var cqi = new CallQueueItem { Call = savedCall, Profiles = new List<UserProfile>() };
				cqi.SetBroadcastDispatches(new List<string>(), new List<int>(), unitIds, new List<int>());
				await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

				_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = departmentId, Call = savedCall });
			}

			if (requirement != null && requirement.IsOpen)
			{
				requirement.ResolvedCount = Math.Min(requirement.RequiredCount, requirement.ResolvedCount + 1);
				requirement.ResolvedByUnitId = unit.UnitId;
				requirement.ClaimedOn = null;
				requirement.ClaimedByUnitId = null;

				if (requirement.ResolvedCount >= requirement.RequiredCount)
				{
					requirement.ResolvedOn = DateTime.UtcNow;
					requirement.ResolvedByUserId = userId;
					requirement.Resolution = (int)resolution;
					resolved++;
				}

				await _requirementsRepository.SaveOrUpdateAsync(requirement, cancellationToken, true);
			}

			await RaiseChangedAsync(departmentId, call.CallId);

			return resolved;
		}

		private async Task<List<AvailableUnit>> BuildAvailableUnitPoolAsync(int departmentId)
		{
			var pool = new List<AvailableUnit>();
			var details = await BuildUnitStatusDetailsAsync(departmentId);
			var states = (await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(departmentId) ?? new List<UnitState>())
				.GroupBy(s => s.UnitId)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Timestamp).First());
			var unitTypes = (await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>())
				.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Type))
				.GroupBy(t => t.Type.Trim(), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			foreach (var unit in await _unitsService.GetUnitsForDepartmentUnlimitedAsync(departmentId) ?? new List<Unit>())
			{
				if (string.IsNullOrWhiteSpace(unit.Type) || !unitTypes.TryGetValue(unit.Type.Trim(), out var type))
					continue;

				var stateId = states.TryGetValue(unit.UnitId, out var state) ? state.State : (int)UnitStateTypes.Available;

				if (!IsDispatchable(stateId, details))
					continue;

				pool.Add(new AvailableUnit { Unit = unit, UnitType = type, InQuarters = UnitResponseOrigin.IsInQuarters(stateId, details) });
			}

			return pool;
		}

		private async Task<double?> EstimateResponseSecondsAsync(int departmentId, AvailableUnit unit, GeoMath.GeoPoint? callPoint)
		{
			if (callPoint == null)
				return null;

			try
			{
				if (_estimateInputs == null || _estimateInputs.DepartmentId != departmentId)
				{
					_estimateInputs = new EstimateInputs
					{
						DepartmentId = departmentId,
						Config = await _departmentSettingsService.GetDispatchRecommendationConfigAsync(departmentId) ?? new DispatchRecommendationConfig(),
						LatestFixes = (await _unitsService.GetLatestUnitLocationsAsync(departmentId) ?? new List<UnitsLocation>())
							.Where(l => l != null && l.IsValidFix != false && !(l.Latitude == 0 && l.Longitude == 0))
							.GroupBy(l => l.UnitId)
							.ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Timestamp).First())
					};
				}

				var inputs = _estimateInputs;
				inputs.LatestFixes.TryGetValue(unit.Unit.UnitId, out var location);
				var liveFix = location != null ? new GeoMath.GeoPoint((double)location.Latitude, (double)location.Longitude) : (GeoMath.GeoPoint?)null;

				GeoMath.GeoPoint? stationPoint = null;
				if ((unit.InQuarters || liveFix == null) && unit.Unit.StationGroupId.HasValue)
				{
					var stationGroupId = unit.Unit.StationGroupId.Value;
					if (!inputs.StationPoints.TryGetValue(stationGroupId, out stationPoint))
					{
						stationPoint = await _geoService.GetStationCoordinatesAsync(await _departmentGroupsService.GetGroupByIdAsync(stationGroupId, false));
						inputs.StationPoints[stationGroupId] = stationPoint;
					}
				}

				var origin = UnitResponseOrigin.Resolve(unit.InQuarters, liveFix, stationPoint, inputs.Config);

				if (!origin.Point.HasValue)
					return null;

				var distance = GeoMath.HaversineMeters(callPoint.Value.Latitude, callPoint.Value.Longitude, origin.Point.Value.Latitude, origin.Point.Value.Longitude);
				return origin.TurnoutSeconds + UnitResponseOrigin.EstimateTravelSeconds(distance);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Estimating unit {unit.Unit.UnitId}'s response for an awaiting call failed.");
				return null;
			}
		}

		private async Task<Dictionary<int, CustomStateDetail>> BuildUnitStatusDetailsAsync(int departmentId)
		{
			var map = new Dictionary<int, CustomStateDetail>();

			foreach (var state in await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(departmentId) ?? new List<CustomState>())
			{
				foreach (var detail in (state?.Details ?? new List<CustomStateDetail>()).Where(d => d != null && d.CustomStateDetailId > 0))
					map[detail.CustomStateDetailId] = detail;
			}

			return map;
		}

		/// <summary>Available- or Delayed-class status, the engine's own rule for a unit with no availability selections.</summary>
		private static bool IsDispatchable(int stateId, Dictionary<int, CustomStateDetail> details)
		{
			var availability = details.TryGetValue(stateId, out var detail)
				? AvailabilityMatrix.ForCustomBaseType(detail.BaseType)
				: AvailabilityMatrix.ForUnitStateType(stateId);

			return availability == AvailabilityClass.Available || availability == AvailabilityClass.Delayed;
		}

		private async Task<UnitType> ResolveUnitTypeAsync(int departmentId, Unit unit)
		{
			if (unit == null || string.IsNullOrWhiteSpace(unit.Type))
				return null;

			return (await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>())
				.FirstOrDefault(t => t != null && string.Equals(t.Type?.Trim(), unit.Type.Trim(), StringComparison.OrdinalIgnoreCase));
		}

		private async Task<Dictionary<string, List<PersonnelRole>>> GetRolesAsync(int departmentId)
		{
			var roles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, List<PersonnelRole>>();
			return new Dictionary<string, List<PersonnelRole>>(roles, StringComparer.OrdinalIgnoreCase);
		}

		private static List<RunCardTarget> ReadTargets(CallOutstandingRequirement requirement)
		{
			if (string.IsNullOrWhiteSpace(requirement?.TargetsJson))
				return new List<RunCardTarget>();

			try
			{
				return JsonConvert.DeserializeObject<List<RunCardTarget>>(requirement.TargetsJson) ?? new List<RunCardTarget>();
			}
			catch (JsonException)
			{
				return new List<RunCardTarget>();
			}
		}

		private Task RaiseChangedAsync(int departmentId, int callId)
		{
			return RaiseAsync(departmentId, callId, new OutstandingRequirementsAlert { Kind = OutstandingRequirementsAlertKinds.Changed });
		}

		private async Task RaiseAsync(int departmentId, int callId, OutstandingRequirementsAlert alert)
		{
			await _eventAggregator.SendMessageAsync(new OutstandingRequirementsUpdatedEvent
			{
				DepartmentId = departmentId,
				CallId = callId,
				Payload = JsonConvert.SerializeObject(alert, AlertJson)
			});
		}

		private static string Truncate(string value, int length)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= length)
				return value;

			return value.Substring(0, length);
		}

		#endregion
	}
}
