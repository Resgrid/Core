using System;
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
				var callId = await _callStatusAttributionService.GetWorkingCallIdForUnitAsync(departmentId, state.UnitId, previousState ?? state);
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
