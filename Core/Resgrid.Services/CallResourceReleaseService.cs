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

			foreach (var dispatch in live)
			{
				dispatch.Release(now, releasedByUserId);
				await _callDispatchUnitRepository.SaveOrUpdateAsync(dispatch, cancellationToken, true);
			}

			// The department's release status, for this unit only (no group: the shift personnel stay on the call). A unit
			// already back in service, out of service, or working another open call keeps its status.
			await _callDispatchStatusService.ApplyReleaseStatusesAsync(call, Array.Empty<int>(), new[] { unitId }, cancellationToken);

			var after = await _unitsService.GetLastUnitStateByUnitIdAsync(unitId);
			var statusApplied = after != null && (before == null || after.UnitStateId != before.UnitStateId);

			if (notify)
			{
				try
				{
					var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
					await _communicationService.SendCancelUnitCallAsync(call, live[0], departmentNumber);
				}
				catch (Exception ex)
				{
					// The release stands: the dispatcher sees it on the console, only the crew's notice failed.
					Logging.LogException(ex, $"Releasing unit {unitId} from call {callId}: the cancellation notice failed.");
				}
			}

			await _callsService.InvalidateOpenCallCountsAsync(departmentId);
			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = departmentId, Call = call });

			return new CallResourceReleaseResult { Outcome = CallResourceReleaseOutcome.Released, Dispatch = live[0], StatusApplied = statusApplied };
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

			if (!await IsMemberOnCallAsync(call, userId, before))
				return CallResourceReleaseResult.Failed(CallResourceReleaseOutcome.NotOnCall);

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

			var statusApplied = await ApplyPersonnelReleaseStatusAsync(call, userId, before, cancellationToken);

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
		/// The member is on the call: dispatched by name, in a dispatched group or role, riding a unit still on the call, or with a
		/// latest status set against the call.
		/// </summary>
		private async Task<bool> IsMemberOnCallAsync(Call call, string userId, ActionLog latest)
		{
			bool Same(string other) => String.Equals(other, userId, StringComparison.OrdinalIgnoreCase);

			if (call.Dispatches != null && call.Dispatches.Any(x => x != null && Same(x.UserId)))
				return true;

			if (latest != null && latest.DestinationType == (int)DestinationEntityTypes.Call && latest.DestinationId == call.CallId)
				return true;

			foreach (var group in call.GroupDispatches ?? Enumerable.Empty<CallDispatchGroup>())
			{
				var members = await _departmentGroupsService.GetAllMembersForGroupAsync(group.DepartmentGroupId);
				if (members != null && members.Any(x => x != null && Same(x.UserId)))
					return true;
			}

			foreach (var role in call.RoleDispatches ?? Enumerable.Empty<CallDispatchRole>())
			{
				var members = await _personnelRolesService.GetAllMembersOfRoleAsync(role.RoleId);
				if (members != null && members.Any(x => x != null && Same(x.UserId)))
					return true;
			}

			foreach (var unitDispatch in call.GetActiveUnitDispatches())
			{
				var crew = await _unitsService.GetCurrentRolesForUnitAsync(unitDispatch.UnitId);
				if (crew != null && crew.Any(x => x != null && Same(x.UserId)))
					return true;
			}

			return false;
		}

		/// <summary>
		/// The department's personnel release status (Dispatch Settings, the shift personnel release status when set; else its
		/// own Available status, else the built-in Standing By), linked to the call like any release status. A member already
		/// available or unavailable, or working another open call, keeps their status.
		/// </summary>
		private async Task<bool> ApplyPersonnelReleaseStatusAsync(Call call, string userId, ActionLog current, CancellationToken cancellationToken)
		{
			try
			{
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
