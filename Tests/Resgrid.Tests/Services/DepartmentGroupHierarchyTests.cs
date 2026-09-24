using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DepartmentGroupHierarchyTests
	{
		// Service Area 1 (org) -> Station A (station), Office B (org) -> Office B Night (org)
		// Service Area 2 (org) -> Station C (station)
		private const int ServiceArea1 = 1;
		private const int StationA = 11;
		private const int OfficeB = 12;
		private const int OfficeBNight = 121;
		private const int ServiceArea2 = 2;
		private const int StationC = 21;

		private static List<DepartmentGroup> Groups()
		{
			return new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = ServiceArea1, Name = "Service Area 1", Type = (int)DepartmentGroupTypes.Orginizational },
				new DepartmentGroup { DepartmentGroupId = StationA, Name = "Station A", Type = (int)DepartmentGroupTypes.Station, ParentDepartmentGroupId = ServiceArea1 },
				new DepartmentGroup { DepartmentGroupId = OfficeB, Name = "Office B", Type = (int)DepartmentGroupTypes.Orginizational, ParentDepartmentGroupId = ServiceArea1 },
				new DepartmentGroup { DepartmentGroupId = OfficeBNight, Name = "Office B Night", Type = (int)DepartmentGroupTypes.Orginizational, ParentDepartmentGroupId = OfficeB },
				new DepartmentGroup { DepartmentGroupId = ServiceArea2, Name = "Service Area 2", Type = (int)DepartmentGroupTypes.Orginizational },
				new DepartmentGroup { DepartmentGroupId = StationC, Name = "Station C", Type = (int)DepartmentGroupTypes.Station, ParentDepartmentGroupId = ServiceArea2 }
			};
		}

		[Test]
		public void descendants_include_the_root_and_every_level_beneath_it()
		{
			DepartmentGroupHierarchy.GetSelfAndDescendantIds(Groups(), ServiceArea1)
				.Should().BeEquivalentTo(new[] { ServiceArea1, StationA, OfficeB, OfficeBNight });
		}

		[Test]
		public void descendants_of_a_leaf_are_just_the_leaf()
		{
			DepartmentGroupHierarchy.GetSelfAndDescendantIds(Groups(), StationC).Should().BeEquivalentTo(new[] { StationC });
		}

		[Test]
		public void descendants_of_an_unknown_group_are_empty()
		{
			DepartmentGroupHierarchy.GetSelfAndDescendantIds(Groups(), 999).Should().BeEmpty();
		}

		[Test]
		public void ancestors_are_nearest_first()
		{
			DepartmentGroupHierarchy.GetAncestorIds(Groups(), OfficeBNight).Should().Equal(OfficeB, ServiceArea1);
		}

		[Test]
		public void a_parent_cycle_terminates()
		{
			var groups = new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = 1, ParentDepartmentGroupId = 2 },
				new DepartmentGroup { DepartmentGroupId = 2, ParentDepartmentGroupId = 1 }
			};

			DepartmentGroupHierarchy.GetSelfAndDescendantIds(groups, 1).Should().BeEquivalentTo(new[] { 1, 2 });
			DepartmentGroupHierarchy.GetAncestorIds(groups, 1).Should().Equal(2);
		}

		[Test]
		public void a_station_group_can_sit_under_an_organizational_group()
		{
			DepartmentGroupHierarchy.IsValidParent(Groups(), StationC, ServiceArea1).Should().BeTrue();
		}

		[Test]
		public void a_group_cannot_be_its_own_parent()
		{
			DepartmentGroupHierarchy.IsValidParent(Groups(), StationA, StationA).Should().BeFalse();
		}

		[Test]
		public void a_group_cannot_move_under_its_own_descendant()
		{
			DepartmentGroupHierarchy.IsValidParent(Groups(), ServiceArea1, OfficeBNight).Should().BeFalse();
		}

		[Test]
		public void a_parent_outside_the_department_list_is_rejected()
		{
			DepartmentGroupHierarchy.IsValidParent(Groups(), StationA, 999).Should().BeFalse();
		}

		[Test]
		public void a_new_group_can_take_any_existing_parent()
		{
			DepartmentGroupHierarchy.IsValidParent(Groups(), 0, ServiceArea2).Should().BeTrue();
		}
	}
}
