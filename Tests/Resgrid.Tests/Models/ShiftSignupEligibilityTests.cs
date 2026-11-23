using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class ShiftSignupEligibilityTests
	{
		private const int GroupId = 100;
		private const int Driver = 1;
		private const int Firefighter = 2;
		private const int Medic = 3;

		private static ShiftGroup Group(params (int RoleId, int Required, int Optional)[] roles)
		{
			var group = new ShiftGroup { ShiftGroupId = 1, DepartmentGroupId = GroupId, Roles = new List<ShiftGroupRole>() };

			foreach (var role in roles)
				group.Roles.Add(new ShiftGroupRole { PersonnelRoleId = role.RoleId, Required = role.Required, Optional = role.Optional });

			return group;
		}

		private static ShiftDayRosterEntry OnDuty(string userId, int? groupId = GroupId, bool pending = false)
		{
			return new ShiftDayRosterEntry { UserId = userId, DepartmentGroupId = groupId, ApprovalPending = pending, Source = ShiftRosterSources.Signup };
		}

		private static ShiftSignupEligibility Eligibility(ShiftGroup group, Dictionary<string, List<int>> roles, params ShiftDayRosterEntry[] roster)
		{
			return new ShiftSignupEligibility
			{
				Group = group,
				Roster = new List<ShiftDayRosterEntry>(roster),
				RoleIdsByUser = new Dictionary<string, List<int>>(roles, StringComparer.OrdinalIgnoreCase)
			};
		}

		[Test]
		public void A_group_with_no_role_slots_takes_anyone()
		{
			var eligibility = Eligibility(Group(), new Dictionary<string, List<int>>(), OnDuty("a"), OnDuty("b"));

			eligibility.Check("newcomer").Should().Be(ShiftActionErrors.None);
			Eligibility(null, new Dictionary<string, List<int>>()).Check("newcomer").Should().Be(ShiftActionErrors.None);
		}

		[Test]
		public void A_member_without_any_of_the_groups_roles_is_refused()
		{
			var eligibility = Eligibility(Group((Driver, 1, 0)), new Dictionary<string, List<int>> { ["medic"] = new List<int> { Medic } });

			eligibility.Check("medic").Should().Be(ShiftActionErrors.RoleRequired);
			eligibility.Check("nobody").Should().Be(ShiftActionErrors.RoleRequired);
		}

		[Test]
		public void Required_and_optional_slots_both_count_and_the_next_one_is_full()
		{
			// One driver required, one more optional: two drivers fit, a third does not.
			var roles = new Dictionary<string, List<int>> { ["d1"] = new List<int> { Driver }, ["d2"] = new List<int> { Driver }, ["d3"] = new List<int> { Driver } };
			var group = Group((Driver, 1, 1));

			Eligibility(group, roles, OnDuty("d1")).Check("d2").Should().Be(ShiftActionErrors.None);
			Eligibility(group, roles, OnDuty("d1"), OnDuty("d2")).Check("d3").Should().Be(ShiftActionErrors.GroupFull);
		}

		[Test]
		public void A_group_with_only_optional_slots_is_full_once_they_are_taken()
		{
			var roles = new Dictionary<string, List<int>> { ["a"] = new List<int> { Firefighter }, ["b"] = new List<int> { Firefighter } };
			var group = Group((Firefighter, 0, 1));

			Eligibility(group, roles).Check("a").Should().Be(ShiftActionErrors.None);
			Eligibility(group, roles, OnDuty("a")).Check("b").Should().Be(ShiftActionErrors.GroupFull);
		}

		[Test]
		public void A_pending_signup_holds_its_slot()
		{
			var roles = new Dictionary<string, List<int>> { ["a"] = new List<int> { Driver }, ["b"] = new List<int> { Driver } };

			Eligibility(Group((Driver, 1, 0)), roles, OnDuty("a", pending: true)).Check("b").Should().Be(ShiftActionErrors.GroupFull);
		}

		[Test]
		public void A_member_with_two_roles_is_moved_to_make_room()
		{
			// Alice drives or fights fire, Bob fights fire, Carl only drives. With one driver and two firefighter slots,
			// Carl takes the driver slot and Alice moves to the spare firefighter slot. With one of each there is no room.
			var roles = new Dictionary<string, List<int>>
			{
				["alice"] = new List<int> { Driver, Firefighter },
				["bob"] = new List<int> { Firefighter },
				["carl"] = new List<int> { Driver }
			};

			Eligibility(Group((Driver, 1, 0), (Firefighter, 1, 1)), roles, OnDuty("alice"), OnDuty("bob")).Check("carl").Should().Be(ShiftActionErrors.None);
			Eligibility(Group((Driver, 1, 0), (Firefighter, 1, 0)), roles, OnDuty("alice"), OnDuty("bob")).Check("carl").Should().Be(ShiftActionErrors.GroupFull);
		}

		[Test]
		public void People_in_other_groups_or_without_the_groups_roles_take_no_slot()
		{
			var roles = new Dictionary<string, List<int>> { ["d1"] = new List<int> { Driver }, ["d2"] = new List<int> { Driver }, ["other"] = new List<int> { Driver } };

			Eligibility(Group((Driver, 1, 0)), roles, OnDuty("other", groupId: 999), OnDuty("helper")).Check("d1").Should().Be(ShiftActionErrors.None);
		}

		[Test]
		public void Over_signup_skips_the_slot_count_but_not_the_role_check()
		{
			var roles = new Dictionary<string, List<int>> { ["d1"] = new List<int> { Driver }, ["d2"] = new List<int> { Driver }, ["medic"] = new List<int> { Medic } };
			var eligibility = Eligibility(Group((Driver, 1, 0)), roles, OnDuty("d1"));
			eligibility.AllowOverSignup = true;

			eligibility.Check("d2").Should().Be(ShiftActionErrors.None);
			eligibility.Check("medic").Should().Be(ShiftActionErrors.RoleRequired);
		}

		[Test]
		public void Slots_sum_duplicate_role_rows_and_ignore_negative_counts()
		{
			var slots = ShiftSignupEligibility.SlotsByRole(Group((Driver, 1, 0), (Driver, 0, 2), (Medic, -1, -1)));

			slots.Should().BeEquivalentTo(new Dictionary<int, int> { [Driver] = 3 });
		}
	}
}
