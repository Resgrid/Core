using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Closes an active call when the last unit dispatched to it reports back in service or out of service
	/// (<see cref="DepartmentSettingTypes.CloseCallWhenUnitsClear"/>). Runs from <c>UnitsService</c> after a status is saved,
	/// through a <see cref="Lazy{T}"/> there, so the call and dispatch-status graph it needs is not a construction cycle.
	///
	/// It errs toward leaving the call open, for the dispatcher to close: a unit that has not reported since its dispatch, a
	/// unit still committed (returning, transporting, at the hospital), a unit now on another call, a custom status with no
	/// base type, a call with no unit dispatched and a call under an active incident command all keep it open.
	/// </summary>
	public class CallAutoCloseService : ICallAutoCloseService
	{
		private static readonly TimeSpan CloseClaimLength = TimeSpan.FromMinutes(2);
		private const string CloseClaimCacheKey = "CallAutoClose_{0}";

		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICustomStateService _customStateService;
		private readonly ICallDispatchUnitRepository _callDispatchUnitRepository;
		private readonly IUnitStatesRepository _unitStatesRepository;
		private readonly ICallsService _callsService;
		private readonly ICallClosureService _callClosureService;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUnitsRepository _unitsRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICacheProvider _cacheProvider;

		public CallAutoCloseService(IDepartmentSettingsService departmentSettingsService, ICustomStateService customStateService,
			ICallDispatchUnitRepository callDispatchUnitRepository, IUnitStatesRepository unitStatesRepository, ICallsService callsService,
			ICallClosureService callClosureService, ICallDispatchStatusService callDispatchStatusService,
			Lazy<IProtectedWriteService> protectedWriteService, IDepartmentsService departmentsService, IUnitsRepository unitsRepository,
			IEventAggregator eventAggregator, ICacheProvider cacheProvider)
		{
			_departmentSettingsService = departmentSettingsService;
			_customStateService = customStateService;
			_callDispatchUnitRepository = callDispatchUnitRepository;
			_unitStatesRepository = unitStatesRepository;
			_callsService = callsService;
			_callClosureService = callClosureService;
			_callDispatchStatusService = callDispatchStatusService;
			_protectedWriteService = protectedWriteService;
			_departmentsService = departmentsService;
			_unitsRepository = unitsRepository;
			_eventAggregator = eventAggregator;
			_cacheProvider = cacheProvider;
		}

		public async Task<List<int>> CloseCallsFinishedByUnitStateAsync(int departmentId, UnitState saved, UnitState previous,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var closed = new List<int>();

			if (saved == null || saved.UnitId <= 0 || departmentId <= 0)
				return closed;

			try
			{
				if (!await _departmentSettingsService.GetCloseCallWhenUnitsClearAsync(departmentId))
					return closed;

				// An offline status replayed later says nothing about where the unit is now.
				var now = DateTime.UtcNow;
				if (!CallStatusAttribution.IsLiveStatus(saved.Timestamp, now))
					return closed;

				var baseTypes = CallStatusLinkage.BuildUnitBaseTypeMap(await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId));

				// Only the moment the unit leaves a call: from working (committed, or a status with no base type) to back in
				// service or out of service. Available to out of service, or into quarters from available, is not that moment.
				if (!UnitCallInvolvement.HasFinishedCall(saved.State, baseTypes))
					return closed;

				if (previous == null || previous.UnitStateId <= 0 || previous.UnitId != saved.UnitId ||
					UnitCallInvolvement.HasFinishedCall(previous.State, baseTypes))
					return closed;

				var openDispatches = ((await _callDispatchUnitRepository.GetOpenCallUnitDispatchesForUnitAsync(departmentId, saved.UnitId)) ?? Enumerable.Empty<CallDispatchUnit>())
					.Where(x => x != null).ToList();

				if (openDispatches.Count == 0)
					return closed;

				var callId = await ResolveFinishedCallIdAsync(saved, previous, openDispatches, now);
				if (!callId.HasValue)
					return closed;

				if (await TryCloseAsync(departmentId, callId.Value, saved, baseTypes, now, cancellationToken))
					closed.Add(callId.Value);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Closing calls finished by a status of unit {saved.UnitId} failed; the calls stay open.");
			}

			return closed;
		}

		/// <summary>
		/// The call the unit just finished: the one its new or previous status points at, else its only open dispatch, else the
		/// call of its latest status that pointed at one of its open dispatches (a transport leg in between may have pointed at
		/// a hospital). An old call the unit was never cleared from is not closed by a status set for a later call.
		/// </summary>
		private async Task<int?> ResolveFinishedCallIdAsync(UnitState saved, UnitState previous, List<CallDispatchUnit> openDispatches, DateTime now)
		{
			var openDispatched = openDispatches.Select(x => x.CallId).Distinct().ToList();

			// A status pointing at a call the unit is not dispatched to (or one already closed) finished that call, not this one.
			var savedCallId = TypedCallId(saved);
			if (savedCallId.HasValue)
				return openDispatched.Contains(savedCallId.Value) ? savedCallId : null;

			var previousCallId = TypedCallId(previous);
			if (previousCallId.HasValue)
				return openDispatched.Contains(previousCallId.Value) ? previousCallId : null;

			if (openDispatched.Count == 1)
				return openDispatched[0];

			var earliestDispatch = openDispatches.Select(x => x.DispatchedOn).Append(now).Min();

			var history = (await _unitStatesRepository.GetAllUnitStatesForUnitInDateRangeAsync(saved.UnitId, earliestDispatch, now)) ?? Enumerable.Empty<UnitState>();
			var lastCallId = history
				.Where(x => x != null && x.UnitStateId != saved.UnitStateId)
				.OrderByDescending(x => x.Timestamp)
				.ThenByDescending(x => x.UnitStateId)
				.Select(TypedCallId)
				.FirstOrDefault(x => x.HasValue);

			return lastCallId.HasValue && openDispatched.Contains(lastCallId.Value) ? lastCallId : null;
		}

		private async Task<bool> TryCloseAsync(int departmentId, int callId, UnitState saved, IReadOnlyDictionary<int, int> baseTypes,
			DateTime now, CancellationToken cancellationToken)
		{
			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null || call.DepartmentId != departmentId || call.IsDeleted || call.State != (int)CallStates.Active)
				return false;

			// A scheduled call that has not gone out yet has nobody working it.
			if (!call.HasBeenDispatched.GetValueOrDefault() && call.DispatchOn.HasValue && call.DispatchOn.Value > now)
				return false;

			var dispatches = ((await _callDispatchUnitRepository.GetCallUnitDispatchesByCallIdAsync(callId)) ?? Enumerable.Empty<CallDispatchUnit>())
				.Where(x => x != null)
				.GroupBy(x => x.UnitId)
				.Select(g => g.OrderBy(x => x.DispatchedOn).First())
				.ToList();

			if (dispatches.All(x => x.UnitId != saved.UnitId))
				return false;

			foreach (var dispatch in dispatches.Where(x => x.UnitId != saved.UnitId))
			{
				var latest = await _unitStatesRepository.GetLastUnitStateByUnitIdAsync(dispatch.UnitId);

				// Every other unit must have reported finished since it was sent: one still working, or one that never
				// reported after its dispatch, keeps the call open.
				if (latest == null || latest.Timestamp < dispatch.DispatchedOn || !UnitCallInvolvement.HasFinishedCall(latest.State, baseTypes))
					return false;
			}

			// An incident command runs the call: it closes from the command.
			if (await _callClosureService.GetBlockingIncidentCommandAsync(departmentId, callId) != null)
			{
				Logging.LogInfo($"Call {callId} not closed automatically: it has an active incident command.");
				return false;
			}

			// Two units clearing at the same moment would both see the call finished; only the first closes it.
			var claimKey = string.Format(CloseClaimCacheKey, callId);
			var claim = await _cacheProvider.IncrementAsync(claimKey, CloseClaimLength);
			if (claim > 1)
				return false;

			Call savedCall;
			try
			{
				call = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false);

				var closedByUserId = saved.SetByUserId;
				if (string.IsNullOrWhiteSpace(closedByUserId))
					closedByUserId = (await _departmentsService.GetDepartmentByIdAsync(departmentId, false))?.ManagingUserId;

				var unit = await _unitsRepository.GetByIdAsync(saved.UnitId);

				call.State = (int)CallStates.Closed;
				call.ClosedOn = now;
				call.ClosedByUserId = closedByUserId;
				call.CompletedNotes = unit != null && !string.IsNullOrWhiteSpace(unit.Name)
					? $"Call closed automatically: {unit.Name} was the last unit to clear."
					: "Call closed automatically: the last unit cleared.";

				// ADP protected write: CompletedNotes is cataloged. A blocked write leaves the call open for the dispatcher.
				var protectedWrite = await _protectedWriteService.Value.PrepareCallWriteAsync(departmentId, call, null, null, closedByUserId,
					workloadCaller: true, cancellationToken);
				if (!protectedWrite.Success)
				{
					Logging.LogError($"Call {callId} not closed automatically: protected write blocked ({protectedWrite.Reason}).");
					await _cacheProvider.RemoveAsync(claimKey);
					return false;
				}

				savedCall = await _callsService.SaveCallAsync(call, cancellationToken);
			}
			catch
			{
				// The call is still open, so the next unit status may try again instead of waiting out the claim.
				await _cacheProvider.RemoveAsync(claimKey);
				throw;
			}

			_eventAggregator.SendMessage<CallClosedEvent>(new CallClosedEvent { DepartmentId = departmentId, Call = savedCall });

			// Units that cleared keep their statuses (the release skips units back in service or out of service); shift
			// personnel are released as on any close.
			if ((call.GroupDispatches != null && call.GroupDispatches.Any()) || (call.UnitDispatches != null && call.UnitDispatches.Any()))
				await _callDispatchStatusService.ApplyReleaseStatusesAsync(call, cancellationToken: cancellationToken);

			Logging.LogInfo($"Call {callId} closed automatically after unit {saved.UnitId}, its last unit, cleared.");

			return true;
		}

		private static int? TypedCallId(UnitState state)
		{
			if (state == null || state.DestinationType != (int)DestinationEntityTypes.Call || !state.DestinationId.HasValue || state.DestinationId.Value <= 0)
				return null;

			return state.DestinationId.Value;
		}
	}
}
