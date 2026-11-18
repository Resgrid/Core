using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Reporting;

namespace Resgrid.Model
{
	/// <summary>
	/// Whether a unit or member a recurring call names can be sent when its call goes out (M0320). A unit or person that is
	/// committed (on another call) or unavailable (out of service, off duty) is left off the call and reported to the dispatcher.
	/// A status with no meaning (a custom option without a base type) cannot be judged and counts as available, so a department
	/// that never set meanings is never held up. Same availability matrix as the run card engine and the nearest-unit board.
	/// </summary>
	public static class RecurringCallResourceAvailability
	{
		/// <summary>
		/// The status's custom base types, keyed by custom status detail id, for one <see cref="CustomStateTypes"/>. Deleted options are
		/// kept: a unit or person can still be on one.
		/// </summary>
		public static Dictionary<int, int> CustomBaseTypes(IEnumerable<CustomState> states, CustomStateTypes type)
		{
			var map = new Dictionary<int, int>();

			foreach (var state in (states ?? Enumerable.Empty<CustomState>()).Where(x => x != null && x.Type == (int)type))
			{
				foreach (var detail in (state.Details ?? new List<CustomStateDetail>()).Where(x => x != null && x.CustomStateDetailId > 0))
					map[detail.CustomStateDetailId] = detail.BaseType;
			}

			return map;
		}

		/// <summary>A unit is available unless its latest status means committed or unavailable; no status on file means available.</summary>
		public static bool IsUnitAvailable(UnitState latest, IReadOnlyDictionary<int, int> unitCustomBaseTypes)
		{
			if (latest == null)
				return true;

			return Allows(UnitCallInvolvement.Classify(latest.State, unitCustomBaseTypes ?? new Dictionary<int, int>()));
		}

		/// <summary>
		/// A member is available unless their latest status (following an On Unit status to its unit) or their staffing means
		/// committed or unavailable. No status or staffing on file means available.
		/// </summary>
		public static bool IsPersonAvailable(ActionLog lastStatus, UserState lastStaffing, IReadOnlyDictionary<int, int> personnelCustomBaseTypes,
			IReadOnlyDictionary<int, int> staffingCustomBaseTypes, IReadOnlyDictionary<int, AvailabilityClass> latestUnitAvailability)
		{
			if (lastStatus != null)
			{
				AvailabilityClass status;
				if (personnelCustomBaseTypes != null && personnelCustomBaseTypes.TryGetValue(lastStatus.ActionTypeId, out var baseType))
					status = AvailabilityMatrix.ForCustomBaseType(baseType);
				else
					status = PersonnelOnUnit.FollowUnit(lastStatus, false, latestUnitAvailability) ?? AvailabilityMatrix.ForBuiltInPersonnelActionType(lastStatus.ActionTypeId);

				if (!Allows(status))
					return false;
			}

			if (lastStaffing != null)
			{
				AvailabilityClass staffing;
				if (staffingCustomBaseTypes != null && staffingCustomBaseTypes.TryGetValue(lastStaffing.State, out var staffingBase))
					staffing = AvailabilityMatrix.ForStaffingBaseType(staffingBase);
				else
					staffing = lastStaffing.State == (int)UserStateTypes.Unavailable ? AvailabilityClass.Unavailable
						: lastStaffing.State == (int)UserStateTypes.Committed ? AvailabilityClass.Committed
						: AvailabilityClass.Available;

				if (!Allows(staffing))
					return false;
			}

			return true;
		}

		private static bool Allows(AvailabilityClass availability)
		{
			return availability != AvailabilityClass.Committed && availability != AvailabilityClass.Unavailable;
		}
	}
}
