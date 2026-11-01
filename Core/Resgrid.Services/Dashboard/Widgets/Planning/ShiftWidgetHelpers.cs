using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>Small pieces the shift widgets share with the Shifts pages (ShiftsController's FindDay, lookups and time windows).</summary>
	internal static class ShiftWidgetHelpers
	{
		internal static string DayKey(DateTime localDate) => localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		internal static ShiftDay FindDay(Shift shift, DateTime date) =>
			shift?.Days?.FirstOrDefault(x => x != null && x.Day.Date == date.Date);

		/// <summary>A signup's department-local window (the shift's start/end times on the signup's day).</summary>
		internal static (DateTime Start, DateTime End) Window(Shift shift, DateTime shiftDay) =>
			ShiftTimeWindow.GetWindow(shiftDay, shift?.StartTime, shift?.EndTime, shift?.Hours);

		internal static DateTime ToUtc(DateTime local, Department department) =>
			department != null ? local.DepartmentLocalToUtc(department) : DateTime.SpecifyKind(local, DateTimeKind.Utc);

		internal static DateTime LocalNow(DateTime nowUtc, Department department) =>
			department != null ? nowUtc.TimeConverter(department) : nowUtc;

		internal static Dictionary<string, string> Names(IEnumerable<PersonName> names)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var name in names ?? Enumerable.Empty<PersonName>())
			{
				if (name?.UserId != null)
					result[name.UserId] = name.Name;
			}

			return result;
		}

		internal static Dictionary<int, string> GroupNames(IEnumerable<DepartmentGroup> groups, int departmentId)
		{
			var result = new Dictionary<int, string>();
			foreach (var group in groups ?? Enumerable.Empty<DepartmentGroup>())
			{
				if (group != null && group.DepartmentId == departmentId)
					result[group.DepartmentGroupId] = group.Name;
			}

			return result;
		}

		internal static string NameFor(Dictionary<string, string> names, string userId) =>
			userId != null && names.TryGetValue(userId, out var name) ? name : string.Empty;

		internal static string GroupNameFor(Dictionary<int, string> groups, int? groupId) =>
			groupId.HasValue && groups.TryGetValue(groupId.Value, out var name) ? name : string.Empty;

		internal static string ViewShiftUrl(int? shiftDayId, string fallback) =>
			shiftDayId.HasValue && shiftDayId.Value > 0 ? "/User/Shifts/ViewShift?shiftDayId=" + shiftDayId.Value.ToString(CultureInfo.InvariantCulture) : fallback;
	}
}
