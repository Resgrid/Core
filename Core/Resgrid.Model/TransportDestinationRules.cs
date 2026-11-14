namespace Resgrid.Model
{
	/// <summary>
	/// Transport destination checks (<see cref="DepartmentSettingTypes.TransportDestinationChecks"/>): a call can name the
	/// destination its patient or load is taken to (<see cref="Call.DestinationPoiId"/>), and a unit reporting a transport
	/// status to another destination POI is a deviation. For a transport status the apps pre-select the POI the unit's current
	/// transport status already points at, else the call's destination, and, with the setting on, require a reason for a
	/// deviation unless it is the POI the unit already reported; the server records each new deviation as a call note
	/// (<see cref="RepeatsPreviousDestination"/>). The apps (Omni, Unit, Dispatch) carry the same rules, so keep them in step.
	/// </summary>
	public static class TransportDestinationRules
	{
		/// <summary>
		/// True for the status meanings that take a unit to a call's destination: Transporting and At Hospital. Meaning comes
		/// from the base type only, never from a status name or id.
		/// </summary>
		public static bool IsTransportBaseType(int? baseType)
		{
			return baseType == (int)ActionBaseTypes.Transporting || baseType == (int)ActionBaseTypes.AtHospital;
		}

		/// <summary>The call's assigned destination POI id, or null when it has none.</summary>
		public static int? AssignedDestinationPoiId(Call call)
		{
			return call?.DestinationPoiId is int id && id > 0 ? id : null;
		}

		/// <summary>
		/// True when a status's destination is a POI other than the call's assigned destination. No destination, a call or a
		/// station is never a deviation, and neither is any destination when the call has none assigned.
		/// </summary>
		public static bool IsDeviation(int? destinationId, int? destinationType, int? assignedDestinationPoiId)
		{
			if (!(assignedDestinationPoiId > 0) || !(destinationId > 0))
				return false;

			if (destinationType != (int)DestinationEntityTypes.Poi)
				return false;

			return destinationId.Value != assignedDestinationPoiId.Value;
		}

		/// <summary>
		/// True when a new status repeats the previous status's POI destination (Transporting then At Hospital at the same
		/// place): the deviation was already recorded when the unit first headed there.
		/// </summary>
		public static bool RepeatsPreviousDestination(UnitState state, UnitState previousState)
		{
			if (state == null || previousState == null || previousState.UnitId != state.UnitId)
				return false;

			return previousState.DestinationType == (int)DestinationEntityTypes.Poi && state.DestinationType == (int)DestinationEntityTypes.Poi &&
				previousState.DestinationId.HasValue && previousState.DestinationId == state.DestinationId;
		}
	}
}
