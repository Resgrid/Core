namespace Resgrid.Model
{
	/// <summary>
	/// The base type of each built-in status and staffing level, so a built-in value carries the same canonical meaning a
	/// department's custom option does through <see cref="CustomStateDetail.BaseType"/>. Before this, a built-in value
	/// resolved to a blank detail whose base type defaulted to 0 (Available) whatever the status was.
	/// </summary>
	public static class BuiltInStatusBaseTypes
	{
		/// <summary>The base type of a built-in unit status (<see cref="UnitStateTypes"/>).</summary>
		public static ActionBaseTypes ForUnitState(int unitState)
		{
			switch ((UnitStateTypes)unitState)
			{
				case UnitStateTypes.Available:
					return ActionBaseTypes.Available;
				case UnitStateTypes.Delayed:
					return ActionBaseTypes.OnBreak;
				case UnitStateTypes.Unavailable:
				case UnitStateTypes.OutOfService:
					return ActionBaseTypes.Unavailable;
				case UnitStateTypes.Committed:
					return ActionBaseTypes.Dispatched;
				case UnitStateTypes.Responding:
					return ActionBaseTypes.Responding;
				case UnitStateTypes.OnScene:
					return ActionBaseTypes.OnScene;
				case UnitStateTypes.Staging:
					return ActionBaseTypes.Staging;
				case UnitStateTypes.Returning:
					return ActionBaseTypes.Returning;
				case UnitStateTypes.Cancelled:
				case UnitStateTypes.Released:
					return ActionBaseTypes.Cleared;
				case UnitStateTypes.Enroute:
					return ActionBaseTypes.Enroute;
				default:
					// Manual, and anything unknown.
					return ActionBaseTypes.None;
			}
		}

		/// <summary>The base type of a built-in personnel status (<see cref="ActionTypes"/>).</summary>
		public static ActionBaseTypes ForPersonnelStatus(int actionType)
		{
			switch ((ActionTypes)actionType)
			{
				case ActionTypes.StandingBy:
					return ActionBaseTypes.Available;
				case ActionTypes.NotResponding:
					return ActionBaseTypes.NotResponding;
				case ActionTypes.Responding:
				case ActionTypes.RespondingToScene:
				case ActionTypes.RespondingToStation:
					return ActionBaseTypes.Responding;
				case ActionTypes.OnScene:
					return ActionBaseTypes.OnScene;
				case ActionTypes.AvailableStation:
					return ActionBaseTypes.InQuarters;
				default:
					// On Unit follows the unit the person is riding; it has no meaning of its own.
					return ActionBaseTypes.None;
			}
		}

		/// <summary>The base type of a built-in staffing level (<see cref="UserStateTypes"/>): the same numbers.</summary>
		public static StaffingBaseTypes ForStaffing(int userState)
		{
			switch ((UserStateTypes)userState)
			{
				case UserStateTypes.Available:
					return StaffingBaseTypes.Available;
				case UserStateTypes.Delayed:
					return StaffingBaseTypes.Delayed;
				case UserStateTypes.Unavailable:
					return StaffingBaseTypes.Unavailable;
				case UserStateTypes.Committed:
					return StaffingBaseTypes.Committed;
				case UserStateTypes.OnShift:
					return StaffingBaseTypes.OnShift;
				default:
					return StaffingBaseTypes.None;
			}
		}
	}
}
