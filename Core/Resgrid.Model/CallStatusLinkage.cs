using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Decides which unit states and personnel action logs that name a call as their destination belong on
	/// that call's record (call view, call export, call history/timeline, incident and NFIRS reports).
	///
	/// A row explicitly typed as a call destination always belongs to the call, whatever the status is and
	/// however that status is configured today: editing a custom status later must never hide history.
	/// Rows with no destination type are legacy (written before destination types existed, or by paths that
	/// never set one), and their destination id may be a station id that merely equals the call id. Those
	/// are kept unless the status only ever targets stations or POIs.
	/// </summary>
	public static class CallStatusLinkage
	{
		/// <summary>
		/// Raw status values at or below this are built-in <see cref="UnitStateTypes"/> / <see cref="ActionTypes"/>;
		/// anything above is a <see cref="CustomStateDetail.CustomStateDetailId"/>. Matches the cut-off
		/// <c>CustomStateService</c> uses when resolving status labels.
		/// </summary>
		public const int MaxBuiltInStatusId = 25;

		/// <summary>
		/// Builds a raw-status to status-detail lookup for one department: the built-in statuses (keyed by their
		/// enum value) plus every custom status detail of the given custom state type, deleted ones included so
		/// historical rows still resolve.
		/// </summary>
		public static Dictionary<int, CustomStateDetail> BuildStatusLookup(IEnumerable<CustomStateDetail> builtInStatuses,
			IEnumerable<CustomState> customStates, CustomStateTypes customStateType)
		{
			var lookup = new Dictionary<int, CustomStateDetail>();

			if (builtInStatuses != null)
			{
				foreach (var detail in builtInStatuses.Where(x => x != null))
					lookup[detail.CustomStateDetailId] = detail;
			}

			if (customStates != null)
			{
				foreach (var state in customStates.Where(x => x != null && x.Type == (int)customStateType && x.Details != null))
				{
					foreach (var detail in state.Details.Where(x => x != null && x.CustomStateDetailId > MaxBuiltInStatusId))
						lookup[detail.CustomStateDetailId] = detail;
				}
			}

			return lookup;
		}

		/// <summary>
		/// True when a status row whose destination id equals the call id belongs on that call.
		/// </summary>
		/// <param name="destinationType">The row's stored destination type (null for legacy rows).</param>
		/// <param name="status">The row's status detail, or null when it can't be resolved.</param>
		public static bool BelongsToCall(int? destinationType, CustomStateDetail status)
		{
			if (destinationType.HasValue)
				return destinationType.Value == (int)DestinationEntityTypes.Call;

			if (status == null)
				return true;

			var detailType = status.DetailType;
			if (detailType.SupportsCalls())
				return true;

			return !detailType.SupportsStations() && !detailType.SupportsPois();
		}

		public static bool BelongsToCall(this UnitState unitState, IReadOnlyDictionary<int, CustomStateDetail> statusLookup)
		{
			if (unitState == null)
				return false;

			CustomStateDetail status = null;
			statusLookup?.TryGetValue(unitState.State, out status);

			return BelongsToCall(unitState.DestinationType, status);
		}

		public static bool BelongsToCall(this ActionLog actionLog, IReadOnlyDictionary<int, CustomStateDetail> statusLookup)
		{
			if (actionLog == null)
				return false;

			CustomStateDetail status = null;
			statusLookup?.TryGetValue(actionLog.ActionTypeId, out status);

			return BelongsToCall(actionLog.DestinationType, status);
		}

		/// <summary>
		/// Maps a raw unit state (built-in value or custom status detail id) to the built-in
		/// <see cref="UnitStateTypes"/> that call-time reports (incident, NFIRS, invoicing) key on. Custom statuses
		/// resolve through their <see cref="CustomStateDetail.BaseType"/>; a custom status with no operational
		/// base type (or one with no built-in equivalent) returns null.
		/// </summary>
		public static UnitStateTypes? ResolveUnitStateKind(int rawState, IReadOnlyDictionary<int, int> customBaseTypes)
		{
			if (rawState <= MaxBuiltInStatusId)
			{
				if (System.Enum.IsDefined(typeof(UnitStateTypes), rawState))
					return (UnitStateTypes)rawState;

				return null;
			}

			if (customBaseTypes == null || !customBaseTypes.TryGetValue(rawState, out var baseType))
				return null;

			switch ((ActionBaseTypes)baseType)
			{
				case ActionBaseTypes.Available:
					return UnitStateTypes.Available;
				case ActionBaseTypes.Unavailable:
				case ActionBaseTypes.NotResponding:
					return UnitStateTypes.Unavailable;
				case ActionBaseTypes.Maintenance:
					return UnitStateTypes.OutOfService;
				case ActionBaseTypes.Dispatched:
					return UnitStateTypes.Committed;
				case ActionBaseTypes.Responding:
					return UnitStateTypes.Responding;
				case ActionBaseTypes.Enroute:
					return UnitStateTypes.Enroute;
				case ActionBaseTypes.OnScene:
				case ActionBaseTypes.MadeContact:
				case ActionBaseTypes.Investigating:
				case ActionBaseTypes.AtPatient:
				case ActionBaseTypes.Searching:
					return UnitStateTypes.OnScene;
				case ActionBaseTypes.Staging:
					return UnitStateTypes.Staging;
				case ActionBaseTypes.Cleared:
					return UnitStateTypes.Released;
				case ActionBaseTypes.Returning:
					return UnitStateTypes.Returning;
				default:
					return null;
			}
		}

		/// <summary>
		/// Builds the custom status detail id to <see cref="CustomStateDetail.BaseType"/> map used by
		/// <see cref="ResolveUnitStateKind"/>, deleted details included so historical rows still resolve.
		/// </summary>
		public static Dictionary<int, int> BuildUnitBaseTypeMap(IEnumerable<CustomState> customStates)
		{
			return BuildBaseTypeMap(customStates, CustomStateTypes.Unit);
		}

		/// <summary>
		/// Builds the custom status detail id to <see cref="CustomStateDetail.BaseType"/> map for one custom state type,
		/// deleted details included so historical rows still resolve.
		/// </summary>
		public static Dictionary<int, int> BuildBaseTypeMap(IEnumerable<CustomState> customStates, CustomStateTypes customStateType)
		{
			var map = new Dictionary<int, int>();

			if (customStates == null)
				return map;

			foreach (var state in customStates.Where(x => x != null && x.Type == (int)customStateType && x.Details != null))
			{
				foreach (var detail in state.Details.Where(x => x != null && x.CustomStateDetailId > MaxBuiltInStatusId))
					map[detail.CustomStateDetailId] = detail.BaseType;
			}

			return map;
		}

		/// <summary>
		/// The call a status row points at: its destination when that is typed as a call, or untyped (legacy rows).
		/// </summary>
		public static int? LinkedCallId(int? destinationId, int? destinationType)
		{
			if (!destinationId.HasValue || destinationId.Value <= 0)
				return null;

			if (destinationType.HasValue && destinationType.Value != (int)DestinationEntityTypes.Call)
				return null;

			return destinationId.Value;
		}

		/// <summary>
		/// True when a unit state ends the unit's involvement in whatever it was working: back in service, out of
		/// service, returning, released or cancelled. Custom statuses resolve through their base type.
		/// </summary>
		public static bool IsClearingUnitState(int rawState, IReadOnlyDictionary<int, int> customBaseTypes)
		{
			var kind = ResolveUnitStateKind(rawState, customBaseTypes);

			return kind is UnitStateTypes.Available or UnitStateTypes.Unavailable or UnitStateTypes.OutOfService
				or UnitStateTypes.Returning or UnitStateTypes.Cancelled or UnitStateTypes.Released;
		}

		/// <summary>
		/// True when a personnel status ends the person's involvement: available / standing by, not responding, available
		/// at a station, or a custom status whose base type means cleared, returning, unavailable or out of service.
		/// Responding to a station is not clearing: volunteers respond to the station to pick up the apparatus.
		/// </summary>
		public static bool IsClearingPersonnelStatus(int rawStatus, IReadOnlyDictionary<int, int> customBaseTypes)
		{
			if (rawStatus <= MaxBuiltInStatusId)
				return rawStatus == (int)ActionTypes.StandingBy || rawStatus == (int)ActionTypes.NotResponding || rawStatus == (int)ActionTypes.AvailableStation;

			if (customBaseTypes == null || !customBaseTypes.TryGetValue(rawStatus, out var baseType))
				return false;

			switch ((ActionBaseTypes)baseType)
			{
				case ActionBaseTypes.Available:
				case ActionBaseTypes.NotResponding:
				case ActionBaseTypes.Cleared:
				case ActionBaseTypes.Returning:
				case ActionBaseTypes.Unavailable:
				case ActionBaseTypes.Maintenance:
					return true;
				default:
					return false;
			}
		}
	}
}
