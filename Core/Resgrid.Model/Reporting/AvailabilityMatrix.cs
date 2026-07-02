using System.Collections.Generic;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Hard-coded, NON per-department mapping of a resource's base status type to a canonical
	/// <see cref="AvailabilityClass"/>. This is the single place the system knows what a base status
	/// type *means* operationally (e.g. a "Responding" base type means the person/unit is not
	/// available for another call).
	///
	/// Status resolution rules (applied by the reporting service, not by SQL):
	///  * Personnel current status lives on the latest <c>ActionLog.ActionTypeId</c>, which holds
	///    EITHER a built-in <see cref="ActionTypes"/> value OR a <c>CustomStateDetailId</c>.
	///  * Unit current status lives on the latest <c>UnitState.State</c>, which holds EITHER a
	///    built-in <see cref="UnitStateTypes"/> value OR a <c>CustomStateDetailId</c>.
	///  * A custom status maps to a canonical base via <c>CustomStateDetail.BaseType</c>
	///    (an <see cref="ActionBaseTypes"/> value), which then maps here via
	///    <see cref="ForPersonnelBaseType"/> / <see cref="ForCustomBaseType"/>.
	///
	/// NOTE: built-in enum values (0..n) overlap numerically with low CustomStateDetailId values, so
	/// disambiguation is done by the service using the department's known set of custom state detail
	/// ids (see PlatformReportingService) — NOT by numeric range. The helpers here only translate an
	/// already-classified base/built-in value into an <see cref="AvailabilityClass"/>.
	/// </summary>
	public static class AvailabilityMatrix
	{
		/// <summary>Highest built-in <see cref="ActionTypes"/> value (personnel).</summary>
		public const int MaxBuiltInActionType = (int)ActionTypes.OnUnit; // 7

		/// <summary>Highest built-in <see cref="UnitStateTypes"/> value (units).</summary>
		public const int MaxBuiltInUnitStateType = (int)UnitStateTypes.Enroute; // 12

		// Canonical base type (ActionBaseTypes) -> availability. Used for custom statuses (personnel
		// and units both store CustomStateDetail.BaseType as an ActionBaseTypes value).
		private static readonly IReadOnlyDictionary<int, AvailabilityClass> ByActionBaseType = new Dictionary<int, AvailabilityClass>
		{
			{ (int)ActionBaseTypes.None,          AvailabilityClass.Unknown },
			{ (int)ActionBaseTypes.Available,     AvailabilityClass.Available },
			{ (int)ActionBaseTypes.NotResponding, AvailabilityClass.Unavailable },
			{ (int)ActionBaseTypes.Responding,    AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.OnScene,       AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.MadeContact,   AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Investigating, AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Dispatched,    AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Cleared,       AvailabilityClass.Available },
			{ (int)ActionBaseTypes.Returning,     AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Staging,       AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Unavailable,   AvailabilityClass.Unavailable },
			{ (int)ActionBaseTypes.Enroute,       AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Transporting,  AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Delivering,    AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.AtPatient,     AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.AtHospital,    AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Searching,     AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Loading,       AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.Standby,       AvailabilityClass.Committed },
			{ (int)ActionBaseTypes.OnPatrol,      AvailabilityClass.Available },
			{ (int)ActionBaseTypes.Maintenance,   AvailabilityClass.Unavailable },
			{ (int)ActionBaseTypes.OnBreak,       AvailabilityClass.Delayed },
			{ (int)ActionBaseTypes.Completed,     AvailabilityClass.Available },
		};

		// Built-in personnel status (ActionTypes) -> availability.
		private static readonly IReadOnlyDictionary<int, AvailabilityClass> ByActionType = new Dictionary<int, AvailabilityClass>
		{
			{ (int)ActionTypes.StandingBy,           AvailabilityClass.Available },   // "Available"
			{ (int)ActionTypes.NotResponding,        AvailabilityClass.Unavailable },
			{ (int)ActionTypes.Responding,           AvailabilityClass.Committed },
			{ (int)ActionTypes.OnScene,              AvailabilityClass.Committed },
			{ (int)ActionTypes.AvailableStation,     AvailabilityClass.Available },
			{ (int)ActionTypes.RespondingToStation,  AvailabilityClass.Committed },
			{ (int)ActionTypes.RespondingToScene,    AvailabilityClass.Committed },
			{ (int)ActionTypes.OnUnit,               AvailabilityClass.Available },   // staffing a unit; unit state is authoritative in richer views
		};

		// Built-in unit status (UnitStateTypes) -> availability.
		private static readonly IReadOnlyDictionary<int, AvailabilityClass> ByUnitStateType = new Dictionary<int, AvailabilityClass>
		{
			{ (int)UnitStateTypes.Available,    AvailabilityClass.Available },
			{ (int)UnitStateTypes.Delayed,      AvailabilityClass.Delayed },
			{ (int)UnitStateTypes.Unavailable,  AvailabilityClass.Unavailable },
			{ (int)UnitStateTypes.Committed,    AvailabilityClass.Committed },
			{ (int)UnitStateTypes.OutOfService, AvailabilityClass.Unavailable },
			{ (int)UnitStateTypes.Responding,   AvailabilityClass.Committed },
			{ (int)UnitStateTypes.OnScene,      AvailabilityClass.Committed },
			{ (int)UnitStateTypes.Staging,      AvailabilityClass.Committed },
			{ (int)UnitStateTypes.Returning,    AvailabilityClass.Committed },
			{ (int)UnitStateTypes.Cancelled,    AvailabilityClass.Available },
			{ (int)UnitStateTypes.Released,     AvailabilityClass.Available },
			{ (int)UnitStateTypes.Manual,       AvailabilityClass.Unknown },
			{ (int)UnitStateTypes.Enroute,      AvailabilityClass.Committed },
		};

		/// <summary>Maps a canonical personnel base type (<see cref="ActionBaseTypes"/>) to availability.</summary>
		public static AvailabilityClass ForPersonnelBaseType(int actionBaseType) =>
			ByActionBaseType.TryGetValue(actionBaseType, out var c) ? c : AvailabilityClass.Unknown;

		/// <summary>
		/// Maps a custom status' <c>CustomStateDetail.BaseType</c> (an <see cref="ActionBaseTypes"/> value)
		/// to availability. Applies to both personnel and unit custom statuses.
		/// </summary>
		public static AvailabilityClass ForCustomBaseType(int actionBaseType) =>
			ForPersonnelBaseType(actionBaseType);

		/// <summary>Maps a built-in personnel status (<see cref="ActionTypes"/>) to availability.</summary>
		public static AvailabilityClass ForBuiltInPersonnelActionType(int actionType) =>
			ByActionType.TryGetValue(actionType, out var c) ? c : AvailabilityClass.Unknown;

		/// <summary>Maps a built-in unit status (<see cref="UnitStateTypes"/>) to availability.</summary>
		public static AvailabilityClass ForUnitStateType(int unitStateType) =>
			ByUnitStateType.TryGetValue(unitStateType, out var c) ? c : AvailabilityClass.Unknown;
	}
}
