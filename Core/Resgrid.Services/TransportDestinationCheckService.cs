using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Records transport destination deviations as call notes (<see cref="ITransportDestinationCheckService"/>). Called through a
	/// <see cref="Lazy{T}"/> from <c>UnitsService</c>: it needs the calls service, which sits above the units service.
	/// </summary>
	public class TransportDestinationCheckService : ITransportDestinationCheckService
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICallStatusAttributionService _callStatusAttributionService;
		private readonly ICallsService _callsService;
		private readonly IMappingService _mappingService;
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;

		public TransportDestinationCheckService(IDepartmentSettingsService departmentSettingsService, ICallStatusAttributionService callStatusAttributionService,
			ICallsService callsService, IMappingService mappingService, IUnitsService unitsService, ICustomStateService customStateService)
		{
			_departmentSettingsService = departmentSettingsService;
			_callStatusAttributionService = callStatusAttributionService;
			_callsService = callsService;
			_mappingService = mappingService;
			_unitsService = unitsService;
			_customStateService = customStateService;
		}

		public async Task<CallNote> OnUnitStateSavedAsync(int departmentId, UnitState state, UnitState previousState, CancellationToken cancellationToken = default(CancellationToken))
		{
			// Cheap checks first: almost every status has no POI destination.
			if (state == null || state.UnitStateId <= 0 || state.DestinationType != (int)DestinationEntityTypes.Poi || !(state.DestinationId > 0))
				return null;

			if (TransportDestinationRules.RepeatsPreviousDestination(state, previousState))
				return null;

			if (IsOutOfOrderReplay(state, previousState))
				return null;

			try
			{
				if (!await _departmentSettingsService.GetTransportDestinationChecksAsync(departmentId))
					return null;

				var customStates = await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId);
				var statuses = CallStatusLinkage.BuildStatusLookup(_customStateService.GetDefaultUnitStatuses(), customStates, CustomStateTypes.Unit);
				statuses.TryGetValue(state.State, out var status);

				if (status == null || !TransportDestinationRules.IsTransportBaseType(status.BaseType))
					return null;

				// The call the unit was working before it headed off: its previous status's call, else its one open dispatch.
				var basis = await WorkingCallBasisAsync(previousState, statuses);
				var callId = await _callStatusAttributionService.GetWorkingCallIdForUnitAsync(departmentId, state.UnitId, basis ?? state);
				if (!callId.HasValue)
					return null;

				var call = await _callsService.GetCallByIdAsync(callId.Value);
				if (call == null || call.DepartmentId != departmentId || call.IsDeleted || call.State != (int)CallStates.Active)
					return null;

				var assignedId = TransportDestinationRules.AssignedDestinationPoiId(call);
				if (!TransportDestinationRules.IsDeviation(state.DestinationId, state.DestinationType, assignedId))
					return null;

				var unit = await _unitsService.GetUnitByIdAsync(state.UnitId);
				var chosen = await _mappingService.GetDestinationPOIByIdAsync(departmentId, state.DestinationId.Value);
				var assigned = await _mappingService.GetDestinationPOIByIdAsync(departmentId, assignedId.Value);

				var note = new CallNote
				{
					CallId = call.CallId,
					UserId = !string.IsNullOrWhiteSpace(state.SetByUserId) ? state.SetByUserId : call.ReportingUserId,
					Note = BuildNoteText(unit?.Name, status.ButtonText, PoiName(chosen, state.DestinationId.Value), PoiName(assigned, assignedId.Value), ReasonFor(state)),
					Source = (int)CallNoteSources.System,
					Timestamp = DateTime.UtcNow
				};

				return await _callsService.SaveCallNoteAsync(note, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recording the transport destination change of unit {state.UnitId} (status {state.UnitStateId}) failed; the status is saved.");
				return null;
			}
		}

		/// <summary>Most earlier transport statuses walked back over to find the call a unit is transporting for.</summary>
		private const int MaxTransportStatusesWalkedBack = 5;

		/// <summary>
		/// A status older than the live window that arrives after a newer one (an offline outbox replayed late). The unit's
		/// working call now need not be the one it was working then, so its note could land on the wrong call: it is not noted.
		/// An offline status replayed in order still resolves from the status before it.
		/// </summary>
		private static bool IsOutOfOrderReplay(UnitState state, UnitState previousState)
		{
			return previousState != null && previousState.UnitStateId > 0 && previousState.Timestamp > state.Timestamp &&
				!CallStatusAttribution.IsLiveStatus(state.Timestamp, DateTime.UtcNow);
		}

		/// <summary>
		/// The status to resolve the working call from. A transport status points at its POI, not at the call, so after a first
		/// transport status (a second hospital, or arriving somewhere else) the unit's earlier transport statuses are walked back
		/// over to the status that does tell: a unit that took the call itself has no dispatch to fall back on.
		/// </summary>
		private async Task<UnitState> WorkingCallBasisAsync(UnitState previousState, Dictionary<int, CustomStateDetail> statuses)
		{
			var basis = previousState;

			for (var i = 0; i < MaxTransportStatusesWalkedBack && IsTransportToPoi(basis, statuses); i++)
			{
				var earlier = await _unitsService.GetLastUnitStateBeforeIdAsync(basis.UnitId, basis.UnitStateId);
				if (earlier == null || earlier.UnitStateId <= 0)
					break;

				basis = earlier;
			}

			return basis;
		}

		private static bool IsTransportToPoi(UnitState state, Dictionary<int, CustomStateDetail> statuses)
		{
			return state != null && state.UnitStateId > 0 && state.DestinationType == (int)DestinationEntityTypes.Poi && state.DestinationId > 0 &&
				statuses.TryGetValue(state.State, out var status) && status != null && TransportDestinationRules.IsTransportBaseType(status.BaseType);
		}

		/// <summary>
		/// The call note text. English, like the other notes Resgrid writes on a call (outbound text is not localized yet).
		/// </summary>
		public static string BuildNoteText(string unitName, string statusName, string chosenName, string assignedName, string reason)
		{
			var unit = string.IsNullOrWhiteSpace(unitName) ? "A unit" : unitName.Trim();
			var statusPart = string.IsNullOrWhiteSpace(statusName) ? "a transport status" : statusName.Trim();
			var reasonPart = string.IsNullOrWhiteSpace(reason) ? "No reason given." : $"Reason: {reason.Trim()}";

			return $"{unit} set {statusPart} to {chosenName}, not the call's assigned destination {assignedName}. {reasonPart}";
		}

		private static string PoiName(Poi poi, int poiId)
		{
			if (poi == null)
				return $"POI {poiId}";

			if (!string.IsNullOrWhiteSpace(poi.Name))
				return poi.Name.Trim();

			return !string.IsNullOrWhiteSpace(poi.Address) ? poi.Address.Trim() : $"POI {poiId}";
		}

		// A protected status keeps its note sealed; the call note then points at it rather than copying it out.
		private static string ReasonFor(UnitState state)
		{
			if (state.IsProtected)
				return "recorded on the unit's status (protected).";

			return state.Note;
		}
	}
}
