using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>Shared projection for the broadcaster and administrative simulation, after trades and approvals resolve.</summary>
	public static class ShiftRosterGroups
	{
		public static List<string> Select(int groupId, IEnumerable<ShiftDayRosterEntry> roster, IEnumerable<string> groupMembers)
		{
			if (roster == null || groupMembers == null) throw new ArgumentException("Complete roster and membership evidence is required.");
			var entries = roster.Where(r => r.IsOnDuty()).ToArray();
			var people = new HashSet<string>(entries.Where(r => r.DepartmentGroupId == groupId).Select(r => r.UserId), StringComparer.OrdinalIgnoreCase);
			var members = new HashSet<string>(groupMembers, StringComparer.OrdinalIgnoreCase);
			foreach (var entry in entries.Where(r => !r.DepartmentGroupId.HasValue && members.Contains(r.UserId))) people.Add(entry.UserId);
			return people.ToList();
		}
	}
}
