using System;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The department's "Suppress (Mute) For These Staffing Levels" rule, shared by every path that decides whether a member
	/// is contacted (CommunicationService, communication tests, the Admin Assist notification impact). The saved list holds
	/// staffing values (built-in values or the department's own level ids), which mute that level only, and meanings
	/// (<see cref="StatusMatch.MeaningListValue"/>), which mute every level with that staffing base type, built-in or custom.
	/// </summary>
	public static class StaffingSuppression
	{
		/// <summary>Whether the saved list has a meaning entry, i.e. whether the department's staffing levels must be read.</summary>
		public static bool HasMeanings(DepartmentSuppressStaffingInfo info) =>
			info?.StaffingLevelsToSupress != null && info.StaffingLevelsToSupress.Any(x => StatusMatch.MeaningFromListValue(x).HasValue);

		/// <summary>
		/// True when the rule is on and mutes a member on <paramref name="staffing"/>. <paramref name="staffingBaseTypeOf"/> gives
		/// a stored level's base type for the department (<see cref="StatusMatch.StaffingBaseTypes"/>); it is only needed when the
		/// list has meanings, and without it meaning entries match nothing.
		/// </summary>
		public static bool IsSuppressed(DepartmentSuppressStaffingInfo info, int staffing, Func<int, int> staffingBaseTypeOf)
		{
			if (info == null || !info.EnableSupressStaffing || info.StaffingLevelsToSupress == null || info.StaffingLevelsToSupress.Count == 0)
				return false;

			if (info.StaffingLevelsToSupress.Contains(staffing))
				return true;

			if (staffingBaseTypeOf == null || !HasMeanings(info))
				return false;

			var baseType = staffingBaseTypeOf(staffing);
			return baseType >= 0 && info.StaffingLevelsToSupress.Contains(StatusMatch.MeaningListValue(baseType));
		}
	}
}
