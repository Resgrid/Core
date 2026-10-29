using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// GitHub #311: every member of a department saw its hidden and disabled people on the personnel list. Department
	/// admins see all of them, a group admin sees the ones in their group, and nobody else sees them.
	/// </summary>
	[TestFixture]
	public class PersonnelListVisibilityHelperTests
	{
		private const string Owner = "OWNER";
		private const string DepartmentAdmin = "DEPT-ADMIN";
		private const string GroupAdmin = "GROUP-ADMIN";
		private const string Viewer = "VIEWER";

		private static Department Department() => new Department
		{
			DepartmentId = 311,
			ManagingUserId = Owner,
			AdminUsers = new List<string> { DepartmentAdmin }
		};

		private static DepartmentGroup Group() => new DepartmentGroup
		{
			DepartmentGroupId = 4,
			Members = new List<DepartmentGroupMember>
			{
				new DepartmentGroupMember { UserId = GroupAdmin, IsAdmin = true },
				new DepartmentGroupMember { UserId = Viewer, IsAdmin = false }
			}
		};

		private static DepartmentMember Member(bool? hidden, bool? disabled) =>
			new DepartmentMember { DepartmentId = 311, UserId = "LISTED", IsHidden = hidden, IsDisabled = disabled };

		[TestCase(null, null)]
		[TestCase(false, false)]
		public void A_member_who_is_neither_hidden_nor_disabled_is_listed_for_everyone(bool? hidden, bool? disabled)
		{
			PersonnelListVisibilityHelper.CanViewMember(Member(hidden, disabled), Department(), null, Viewer).Should().BeTrue();
		}

		[Test]
		public void A_missing_membership_is_not_hidden()
		{
			PersonnelListVisibilityHelper.CanViewMember(null, Department(), null, Viewer).Should().BeTrue();
		}

		[TestCase(true, false)]
		[TestCase(false, true)]
		[TestCase(true, true)]
		public void An_ordinary_member_does_not_see_a_hidden_or_disabled_member_even_in_their_own_group(bool hidden, bool disabled)
		{
			PersonnelListVisibilityHelper.CanViewMember(Member(hidden, disabled), Department(), Group(), Viewer).Should().BeFalse();
		}

		[TestCase(DepartmentAdmin)]
		[TestCase(Owner)]
		public void A_department_admin_sees_hidden_and_disabled_members_in_any_group_or_none(string admin)
		{
			PersonnelListVisibilityHelper.CanViewMember(Member(true, false), Department(), null, admin).Should().BeTrue();
			PersonnelListVisibilityHelper.CanViewMember(Member(false, true), Department(), Group(), admin).Should().BeTrue();
		}

		[Test]
		public void A_group_admin_sees_hidden_and_disabled_members_of_their_group()
		{
			PersonnelListVisibilityHelper.CanViewMember(Member(true, false), Department(), Group(), GroupAdmin).Should().BeTrue();
			PersonnelListVisibilityHelper.CanViewMember(Member(false, true), Department(), Group(), GroupAdmin).Should().BeTrue();
		}

		[Test]
		public void A_group_admin_does_not_see_hidden_or_disabled_members_outside_their_group()
		{
			var otherGroup = new DepartmentGroup
			{
				DepartmentGroupId = 9,
				Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "SOMEONE-ELSE", IsAdmin = true } }
			};

			PersonnelListVisibilityHelper.CanViewMember(Member(true, false), Department(), otherGroup, GroupAdmin).Should().BeFalse();
			PersonnelListVisibilityHelper.CanViewMember(Member(false, true), Department(), null, GroupAdmin).Should().BeFalse();
		}

		[Test]
		public void A_hidden_or_disabled_member_can_open_their_own_profile_but_not_find_themselves_on_the_list()
		{
			var hidden = new DepartmentMember { DepartmentId = 311, UserId = Viewer, IsHidden = true };
			var disabled = new DepartmentMember { DepartmentId = 311, UserId = Viewer, IsDisabled = true };

			PersonnelListVisibilityHelper.CanViewMemberProfile(hidden, Department(), Group(), Viewer).Should().BeTrue();
			PersonnelListVisibilityHelper.CanViewMemberProfile(disabled, Department(), Group(), Viewer).Should().BeTrue();
			PersonnelListVisibilityHelper.CanViewMember(hidden, Department(), Group(), Viewer).Should().BeFalse();
		}

		[Test]
		public void A_profile_follows_the_list_rule_for_everyone_but_the_person()
		{
			PersonnelListVisibilityHelper.CanViewMemberProfile(Member(true, false), Department(), Group(), Viewer).Should().BeFalse();
			PersonnelListVisibilityHelper.CanViewMemberProfile(Member(false, true), Department(), Group(), GroupAdmin).Should().BeTrue();
			PersonnelListVisibilityHelper.CanViewMemberProfile(Member(true, true), Department(), null, DepartmentAdmin).Should().BeTrue();
			PersonnelListVisibilityHelper.CanViewMemberProfile(Member(true, false), Department(), Group(), null).Should().BeFalse();
		}

		[TestCase(null)]
		[TestCase("")]
		public void No_viewer_sees_nothing_hidden_even_when_the_department_has_no_managing_user(string viewer)
		{
			var department = Department();
			department.ManagingUserId = null;

			PersonnelListVisibilityHelper.CanViewMember(Member(true, false), department, Group(), viewer).Should().BeFalse();
		}
	}
}
