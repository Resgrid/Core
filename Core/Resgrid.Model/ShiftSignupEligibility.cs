using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Whether a member may sign themselves up for a shift group on a day. The group's role rows are the places it offers:
	/// each <see cref="ShiftGroupRole"/> gives <c>Required + Optional</c> slots for its role.
	///
	/// The rules:
	/// 1. A group with no role slots states no needs and takes anyone.
	/// 2. Otherwise the member must hold one of the group's roles (<see cref="ShiftActionErrors.RoleRequired"/>). The
	///    role check works from the same per-user role map as <see cref="ShiftTradeEligibility"/>.
	/// 3. The member must fit in a slot (<see cref="ShiftActionErrors.GroupFull"/>): everyone already in the group for the
	///    day, on duty or waiting for approval, keeps a slot for a role they hold, and the member needs one left over. People
	///    are placed as a matching, so a member who holds two roles is moved to the other one if that makes room.
	///    Required and optional slots count alike, so a group with only optional slots is full once those are taken.
	///    People in the group who hold none of its roles take no slot.
	/// 4. A department that allows over-signup (<see cref="DepartmentSettingTypes.AllowShiftOverSignup"/>) skips rule 3;
	///    rule 2 still applies.
	///
	/// Supervisors assigning people to a day are not limited by this.
	/// </summary>
	public class ShiftSignupEligibility
	{
		public ShiftGroup Group { get; set; }

		/// <summary>The day's roster (<see cref="ShiftDaySchedule.Roster"/>); entries for other groups are ignored.</summary>
		public List<ShiftDayRosterEntry> Roster { get; set; } = new List<ShiftDayRosterEntry>();

		public Dictionary<string, List<int>> RoleIdsByUser { get; set; } = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

		public bool AllowOverSignup { get; set; }

		/// <summary>Why the user cannot sign up for the group, or <see cref="ShiftActionErrors.None"/> when they can.</summary>
		public ShiftActionErrors Check(string userId)
		{
			var slots = SlotsByRole(Group);

			if (slots.Count == 0)
				return ShiftActionErrors.None;

			var userRoles = HeldRoles(userId, slots);

			if (userRoles.Count == 0)
				return ShiftActionErrors.RoleRequired;

			if (AllowOverSignup)
				return ShiftActionErrors.None;

			var occupants = (Roster ?? new List<ShiftDayRosterEntry>())
				.Where(x => x != null && x.DepartmentGroupId == Group.DepartmentGroupId && !String.IsNullOrWhiteSpace(x.UserId)
				            && !String.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.Select(x => x.UserId)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Select(x => HeldRoles(x, slots))
				.Where(x => x.Count > 0)
				.ToList();

			var placedBefore = MaxPlaced(occupants, slots);
			occupants.Add(userRoles);

			return MaxPlaced(occupants, slots) > placedBefore ? ShiftActionErrors.None : ShiftActionErrors.GroupFull;
		}

		/// <summary>Role id to the slots the group offers for it (Required + Optional, negatives as zero); roles with none are left out.</summary>
		public static Dictionary<int, int> SlotsByRole(ShiftGroup group)
		{
			var slots = new Dictionary<int, int>();

			foreach (var role in (group?.Roles ?? Enumerable.Empty<ShiftGroupRole>()).Where(x => x != null))
			{
				slots.TryGetValue(role.PersonnelRoleId, out var existing);
				slots[role.PersonnelRoleId] = existing + Math.Max(role.Required, 0) + Math.Max(role.Optional, 0);
			}

			return slots.Where(x => x.Value > 0).ToDictionary(x => x.Key, x => x.Value);
		}

		private List<int> HeldRoles(string userId, Dictionary<int, int> slots)
		{
			if (userId == null || RoleIdsByUser == null || !RoleIdsByUser.TryGetValue(userId, out var roles) || roles == null)
				return new List<int>();

			return roles.Where(slots.ContainsKey).Distinct().ToList();
		}

		/// <summary>The most people that fit in the slots at once, each in one slot for a role they hold (bipartite matching).</summary>
		private static int MaxPlaced(List<List<int>> people, Dictionary<int, int> slots)
		{
			// No more than one slot per person can be used, so a large Required/Optional count is capped.
			var units = slots.SelectMany(x => Enumerable.Repeat(x.Key, Math.Min(x.Value, people.Count))).ToList();
			var owner = Enumerable.Repeat(-1, units.Count).ToArray();
			var placed = 0;

			for (var person = 0; person < people.Count; person++)
			{
				if (TryPlace(person, people, units, owner, new bool[units.Count]))
					placed++;
			}

			return placed;
		}

		private static bool TryPlace(int person, List<List<int>> people, List<int> units, int[] owner, bool[] seen)
		{
			for (var unit = 0; unit < units.Count; unit++)
			{
				if (seen[unit] || !people[person].Contains(units[unit]))
					continue;

				seen[unit] = true;

				if (owner[unit] < 0 || TryPlace(owner[unit], people, units, owner, seen))
				{
					owner[unit] = person;
					return true;
				}
			}

			return false;
		}
	}
}
