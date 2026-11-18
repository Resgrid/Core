using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CallResourceReleaseService : ICallResourceReleaseService
	{
		private readonly ICallsService _callsService;
		private readonly ICallsRepository _callsRepository;
		private readonly ICallDispatchUnitRepository _callDispatchUnitRepository;
		private readonly ICallPersonnelReleasesRepository _callPersonnelReleasesRepository;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICustomStateService _customStateService;
		private readonly ICommunicationService _communicationService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;

		public CallResourceReleaseService(
			ICallsService callsService,
			ICallsRepository callsRepository,
			ICallDispatchUnitRepository callDispatchUnitRepository,
			ICallPersonnelReleasesRepository callPersonnelReleasesRepository,
			ICallDispatchStatusService callDispatchStatusService,
			IUnitsService unitsService,
			IActionLogsService actionLogsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			ICustomStateService customStateService,
			ICommunicationService communicationService,
			IDepartmentSettingsService departmentSettingsService,
			IEventAggregator eventAggregator)
		{
			_callsService = callsService;
			_callsRepository = callsRepository;
			_callDispatchUnitRepository = callDispatchUnitRepository;
			_callPersonnelReleasesRepository = callPersonnelReleasesRepository;
			_callDispatchStatusService = callDispatchStatusService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_customStateService = customStateService;
			_communicationService = communicationService;
			_departmentSettingsService = departmentSettingsService;
			_eventAggregator = eventAggregator;
		}

		public async Task<CallResourceReleaseResult> ReleaseUnitAsync(int departmentId, int callId, int unitId, string releasedByUserId, bool notify,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var (call, failure) = await GetLiveCallAsync(departmentId, callId);
			if (failure != null)
				return failure;

			call = await _callsService.PopulateCallData(call, false, false, false, false, true, false, false, false, false);

			var dispatches = (call.UnitDispatches ?? Enumerable.Empty<CallDispatchUnit>()).Where(x => x != null && x.UnitId == unitId).ToList();
			if (dispatches.Count == 0)
				return CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.NotOnCall);

			var live = dispatches.Where(x => !x.IsReleased).ToList();
			if (live.Count == 0)
				return CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.AlreadyReleased, dispatches.OrderByDescending(x => x.ReleasedOn).First());

			var now = DateTime.UtcNow;
			var before = await _unitsService.GetLastUnitStateByUnitIdAsync(unitId);

			// Conditional writes: a second dispatcher releasing the same unit at the same moment changes no row, so only the
			// request that released it applies the status, sends the notice and keeps its release metadata.
			var released = new List<CallDispatchUnit>();
			foreach (var dispatch in live)
			{
				if (!await _callDispatchUnitRepository.TryReleaseAsync(dispatch.CallDispatchUnitId, now, releasedByUserId, cancellationToken))
					continue;

				dispatch.Release(now, releasedByUserId);
				released.Add(dispatch);
			}

			if (released.Count == 0)
			{
				var current = ((await _callDispatchUnitRepository.GetCallUnitDispatchesByCallIdAsync(callId)) ?? Enumerable.Empty<CallDispatchUnit>())
					.Where(x => x != null && x.UnitId == unitId && x.IsReleased)
					.OrderByDescending(x => x.ReleasedOn)
					.FirstOrDefault();

				return CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.AlreadyReleased, current ?? live[0]);
			}

			// The department's release status, for this unit only (no group: the shift personnel stay on the call). A unit
			// already back in service, out of service, or working another open call keeps its status. The release stands if
			// the status cannot be written: the notice, counts and call update below still go out, as for a member.
			var statusApplied = false;
			try
			{
				await _callDispatchStatusService.ApplyReleaseStatusesAsync(call, Array.Empty<int>(), new[] { unitId }, cancellationToken);

				// Only a status the release automation wrote against this call counts, not a crew's own status saved meanwhile.
				var after = await _unitsService.GetLastUnitStateByUnitIdAsync(unitId);
				statusApplied = after != null && (before == null || after.UnitStateId != before.UnitStateId) &&
					after.DestinationType == (int)DestinationEntityTypes.Call && after.DestinationId == callId &&
					after.SetByOrigin == (int)StatusSetOrigins.DispatchAutomation;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Releasing unit {unitId} from call {callId}: the release status could not be set.");
			}

			if (notify)
			{
				try
				{
					var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
					await _communicationService.SendCancelUnitCallAsync(call, released[0], departmentNumber);
				}
				catch (Exception ex)
				{
					// The release stands: the dispatcher sees it on the console, only the crew's notice failed.
					Logging.LogException(ex, $"Releasing unit {unitId} from call {callId}: the cancellation notice failed.");
				}
			}

			await _callsService.InvalidateOpenCallCountsAsync(departmentId);
			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = departmentId, Call = call });

			return new CallResourceReleaseResult { Outcome = CallResourceReleaseOutcome.Released, Dispatch = released[0], StatusApplied = statusApplied };
		}

		public async Task<CallResourceReleaseResult> ReleasePersonnelAsync(int departmentId, int callId, string userId, string releasedByUserId, bool notify,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (String.IsNullOrWhiteSpace(userId))
				return CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.NotOnCall);

			var (call, failure) = await GetLiveCallAsync(departmentId, callId);
			if (failure != null)
				return failure;

			call = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false);

			var existing = (call.PersonnelReleases ?? new List<CallPersonnelRelease>())
				.FirstOrDefault(x => x != null && String.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
			if (existing != null)
				return CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.AlreadyReleased, personnelRelease: existing);

			var before = await _actionLogsService.GetLastActionLogForUserAsync(userId, departmentId);

			// The release stores the member's id as the call's records spell it, not as the request did: every release reader
			// (and the PostgreSQL text comparisons) then sees the same identity.
			var memberUserId = await GetMemberOnCallAsync(call, userId, before);
			if (memberUserId == null)
				return CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.NotOnCall);

			userId = memberUserId;
			var now = DateTime.UtcNow;
			var release = new CallPersonnelRelease
			{
				DepartmentId = departmentId,
				CallId = callId,
				UserId = userId,
				ReleasedOn = now,
				ReleasedByUserId = releasedByUserId
			};

			try
			{
				release = await _callPersonnelReleasesRepository.SaveOrUpdateAsync(release, cancellationToken, true);
			}
			catch (Exception)
			{
				// One release per call and member (unique index): a second dispatcher releasing the same member at the same moment
				// finds the first one's row.
				var raced = ((await _callPersonnelReleasesRepository.GetByCallIdAsync(callId)) ?? Enumerable.Empty<CallPersonnelRelease>())
					.FirstOrDefault(x => x != null && String.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
				if (raced != null)
					return CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.AlreadyReleased, personnelRelease: raced);

				throw;
			}

			call.PersonnelReleases ??= new List<CallPersonnelRelease>();
			call.PersonnelReleases.Add(release);

			var statusApplied = await ApplyPersonnelReleaseStatusAsync(call, userId, cancellationToken);

			if (notify)
			{
				try
				{
					var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
					await _communicationService.SendCancelCallAsync(call, new CallDispatch { CallId = callId, UserId = userId }, departmentNumber, departmentId);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Releasing member {userId} from call {callId}: the cancellation notice failed.");
				}
			}

			await _callsService.InvalidateOpenCallCountsAsync(departmentId);
			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = departmentId, Call = call });

			return new CallResourceReleaseResult { Outcome = CallResourceReleaseOutcome.Released, PersonnelRelease = release, StatusApplied = statusApplied };
		}

		public async Task<int> ClearPersonnelReleasesAsync(int callId, IEnumerable<string> userIds, CancellationToken cancellationToken = default(CancellationToken))
		{
			var ids = (userIds ?? Enumerable.Empty<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).ToList();
			if (callId <= 0 || ids.Count == 0)
				return 0;

			return await _callPersonnelReleasesRepository.DeleteForUsersAsync(callId, ids, cancellationToken);
		}

		/// <summary>Only a call that has gone out has resources working it; a pending or scheduled call is changed by editing its list.</summary>
		private async Task<(Call Call, CallResourceReleaseResult Failure)> GetLiveCallAsync(int departmentId, int callId)
		{
			var call = callId > 0 ? await _callsService.GetCallByIdAsync(callId) : null;

			if (call == null || call.DepartmentId != departmentId || call.IsDeleted)
				return (null, CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.CallNotFound));

			var now = DateTime.UtcNow;
			var isLive = call.State == (int)CallStates.Active &&
				(call.HasBeenDispatched.GetValueOrDefault() || !call.DispatchOn.HasValue || call.DispatchOn.Value <= now);

			return isLive ? (call, null) : (null, CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.CallNotLive));
		}

		/// <summary>
		/// The member is on the call: dispatched by name, in a dispatched group or role, riding a unit still on the call, in the
		/// station group of a unit still on the call (when unit dispatches also page the unit's group, as the broadcast does), or
		/// with a latest status set against the call. Returns the member's id as the matching record spells it, or null.
		/// </summary>
		private async Task<string> GetMemberOnCallAsync(Call call, string userId, ActionLog latest)
		{
			bool Same(string other) => String.Equals(other, userId, StringComparison.OrdinalIgnoreCase);

			var direct = call.Dispatches?.FirstOrDefault(x => x != null && Same(x.UserId));
			if (direct != null)
				return direct.UserId;

			if (latest != null && latest.DestinationType == (int)DestinationEntityTypes.Call && latest.DestinationId == call.CallId)
				return String.IsNullOrWhiteSpace(latest.UserId) ? userId : latest.UserId;

			foreach (var group in call.GroupDispatches ?? Enumerable.Empty<CallDispatchGroup>())
			{
				var member = (await _departmentGroupsService.GetAllMembersForGroupAsync(group.DepartmentGroupId))?.FirstOrDefault(x => x != null && Same(x.UserId));
				if (member != null)
					return member.UserId;
			}

			foreach (var role in call.RoleDispatches ?? Enumerable.Empty<CallDispatchRole>())
			{
				var member = (await _personnelRolesService.GetAllMembersOfRoleAsync(role.RoleId))?.FirstOrDefault(x => x != null && Same(x.UserId));
				if (member != null)
					return member.UserId;
			}

			var liveUnits = call.GetActiveUnitDispatches();
			foreach (var unitDispatch in liveUnits)
			{
				var rider = (await _unitsService.GetCurrentRolesForUnitAsync(unitDispatch.UnitId))?.FirstOrDefault(x => x != null && Same(x.UserId));
				if (rider != null)
					return rider.UserId;
			}

			if (liveUnits.Any() && await _departmentSettingsService.GetUnitDispatchAlsoDispatchToGroupAsync(call.DepartmentId))
			{
				var checkedGroups = new HashSet<int>();
				foreach (var unitDispatch in liveUnits)
				{
					var unit = await _unitsService.GetUnitByIdAsync(unitDispatch.UnitId);
					if (unit?.StationGroupId == null || !checkedGroups.Add(unit.StationGroupId.Value))
						continue;

					var member = (await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value))?.FirstOrDefault(x => x != null && Same(x.UserId));
					if (member != null)
						return member.UserId;
				}
			}

			return null;
		}

		/// <summary>
		/// The department's personnel release status (Dispatch Settings, the shift personnel release status when set; else its
		/// own Available status, else the built-in Standing By), linked to the call like any release status. A member already
		/// available or unavailable, or working another open call, keeps their status.
		/// </summary>
		private async Task<bool> ApplyPersonnelReleaseStatusAsync(Call call, string userId, CancellationToken cancellationToken)
		{
			try
			{
				// Read now, not the status read before the release was saved: a member who has since started responding to
				// another open call keeps that status.
				var current = await _actionLogsService.GetLastActionLogForUserAsync(userId, call.DepartmentId);
				if (current != null)
				{
					var baseTypes = CallStatusLinkage.BuildBaseTypeMap(await _customStateService.GetAllCustomStatesForDepartmentAsync(call.DepartmentId), CustomStateTypes.Personnel);
					if (CallStatusLinkage.IsClearingPersonnelStatus(current.ActionTypeId, baseTypes))
						return false;

					if (current.DestinationType == (int)DestinationEntityTypes.Call && current.DestinationId.HasValue && current.DestinationId.Value != call.CallId)
					{
						var other = await _callsRepository.GetByIdAsync(current.DestinationId.Value);
						if (other != null && other.DepartmentId == call.DepartmentId && !other.IsDeleted && other.State == (int)CallStates.Active)
							return false;
					}
				}

				var statusToSet = await _departmentSettingsService.GetShiftCallReleasePersonnelStatusToSetAsync(call.DepartmentId);
				if (statusToSet < 0)
					statusToSet = await _customStateService.ResolvePersonnelStatusAsync(call.DepartmentId, ActionBaseTypes.Available) ?? (int)ActionTypes.StandingBy;

				using (StatusWriteActor.BeginAutomation(StatusSetOrigins.DispatchAutomation))
				{
					await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, statusToSet, null, call.CallId, (int)DestinationEntityTypes.Call, cancellationToken);
				}

				return true;
			}
			catch (Exception ex)
			{
				// The release stands without the status: the member sets their own.
				Logging.LogException(ex, $"Releasing member {userId} from call {call.CallId}: the release status could not be set.");
				return false;
			}
		}
	}
}
