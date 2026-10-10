using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CallGroupAssignerTests
	{
		// Two side-by-side station boxes: Station 1 west of -104.9, Station 2 east of it.
		private const string Station1Fence = "[{\"lat\":39.70,\"lng\":-105.00},{\"lat\":39.80,\"lng\":-105.00},{\"lat\":39.80,\"lng\":-104.90},{\"lat\":39.70,\"lng\":-104.90}]";
		private const string Station2Fence = "[{\"lat\":39.70,\"lng\":-104.90},{\"lat\":39.80,\"lng\":-104.90},{\"lat\":39.80,\"lng\":-104.80},{\"lat\":39.70,\"lng\":-104.80}]";
		// A battalion boundary covering both stations.
		private const string BattalionFence = "[{\"lat\":39.60,\"lng\":-105.10},{\"lat\":39.90,\"lng\":-105.10},{\"lat\":39.90,\"lng\":-104.70},{\"lat\":39.60,\"lng\":-104.70}]";

		private const string InStation1 = "39.75,-104.95";
		private const string InStation2 = "39.75,-104.85";
		private const string InBattalionOnly = "39.65,-105.05";
		private const string OutsideEverything = "40.50,-104.00";

		private static DepartmentGroup Group(int id, string fence = null, int? parentId = null, DepartmentGroupTypes type = DepartmentGroupTypes.Station,
			string latitude = null, string longitude = null, params string[] members)
		{
			return new DepartmentGroup
			{
				DepartmentGroupId = id,
				DepartmentId = 1,
				Name = "Group " + id,
				Type = (int)type,
				Geofence = fence,
				ParentDepartmentGroupId = parentId,
				Latitude = latitude,
				Longitude = longitude,
				Members = members.Select(m => new DepartmentGroupMember { DepartmentGroupId = id, UserId = m }).ToList()
			};
		}

		private static Unit Unit(int id, int? stationGroupId) => new Unit { UnitId = id, DepartmentId = 1, StationGroupId = stationGroupId };

		private static CallGroupResources Resources(IEnumerable<int> units = null, IEnumerable<string> users = null, IEnumerable<int> groups = null)
		{
			var resources = new CallGroupResources();
			resources.UnitIds.UnionWith(units ?? Enumerable.Empty<int>());
			resources.UserIds.UnionWith(users ?? Enumerable.Empty<string>());
			resources.GroupIds.UnionWith(groups ?? Enumerable.Empty<int>());
			return resources;
		}

		[Test]
		public void call_inside_a_geofence_goes_to_that_group_even_when_another_group_was_dispatched()
		{
			var assigner = new CallGroupAssigner(new[] { Group(1, Station1Fence), Group(2, Station2Fence) }, new[] { Unit(20, 2), Unit(21, 2) });

			var result = assigner.Assign(100, InStation1, Resources(units: new[] { 20, 21 }));

			result.CallId.Should().Be(100);
			result.DepartmentGroupId.Should().Be(1);
			result.Method.Should().Be(CallGroupAssignmentMethods.Geofence);
		}

		[Test]
		public void nested_boundaries_resolve_to_the_innermost_group()
		{
			var groups = new[] { Group(10, BattalionFence, type: DepartmentGroupTypes.Orginizational), Group(1, Station1Fence, 10), Group(2, Station2Fence, 10) };
			var assigner = new CallGroupAssigner(groups, Enumerable.Empty<Unit>());

			assigner.Assign(1, InStation2, null).DepartmentGroupId.Should().Be(2);
			assigner.Assign(2, InBattalionOnly, null).DepartmentGroupId.Should().Be(10);
		}

		[Test]
		public void overlapping_unrelated_boundaries_prefer_the_station_then_the_smaller_area()
		{
			// The organizational boundary is listed first and is not the station's parent.
			var orgOverlap = new CallGroupAssigner(new[] { Group(10, BattalionFence, type: DepartmentGroupTypes.Orginizational), Group(1, Station1Fence) }, null);
			orgOverlap.Assign(1, InStation1, null).DepartmentGroupId.Should().Be(1);

			var stationOverlap = new CallGroupAssigner(new[] { Group(10, BattalionFence), Group(1, Station1Fence) }, null);
			stationOverlap.Assign(1, InStation1, null).DepartmentGroupId.Should().Be(1);
		}

		[Test]
		public void call_without_a_location_goes_to_the_plurality_of_dispatched_units_and_personnel()
		{
			var groups = new[] { Group(1, Station1Fence, members: new[] { "a", "b" }), Group(2, members: new[] { "c" }) };
			var units = new[] { Unit(10, 1), Unit(20, 2), Unit(21, 2) };
			var assigner = new CallGroupAssigner(groups, units);

			// Group 1: unit 10 + users a, b = 3 votes. Group 2: units 20, 21 + user c = 3 votes, but more unit votes.
			var tiedOnTotal = assigner.Assign(1, null, Resources(new[] { 10, 20, 21 }, new[] { "a", "b", "c" }));
			tiedOnTotal.DepartmentGroupId.Should().Be(2);
			tiedOnTotal.Method.Should().Be(CallGroupAssignmentMethods.Dispatch);

			var plurality = assigner.Assign(2, "", Resources(new[] { 10 }, new[] { "a", "b", "c" }));
			plurality.DepartmentGroupId.Should().Be(1);
			plurality.Method.Should().Be(CallGroupAssignmentMethods.Dispatch);
		}

		[Test]
		public void call_outside_every_boundary_falls_back_to_the_dispatch_plurality()
		{
			var assigner = new CallGroupAssigner(new[] { Group(1, Station1Fence), Group(2, Station2Fence) }, new[] { Unit(20, 2) });

			var result = assigner.Assign(1, OutsideEverything, Resources(units: new[] { 20 }));

			result.DepartmentGroupId.Should().Be(2);
			result.Method.Should().Be(CallGroupAssignmentMethods.Dispatch);
		}

		[Test]
		public void a_dispatched_group_counts_through_its_members_or_once_when_it_has_none()
		{
			var groups = new[] { Group(1, members: new[] { "a", "b", "c" }), Group(2, members: new[] { "d" }), Group(3) };
			var assigner = new CallGroupAssigner(groups, null);

			// Dispatching group 1 votes its three members; user d is one vote for group 2.
			assigner.Assign(1, null, Resources(users: new[] { "d" }, groups: new[] { 1 })).DepartmentGroupId.Should().Be(1);

			// A member dispatched directly and through their group still votes once.
			assigner.Assign(2, null, Resources(users: new[] { "a", "d" }, groups: new[] { 2 })).DepartmentGroupId.Should().BeNull();

			// An empty group dispatched alone still says where the call is.
			assigner.Assign(3, null, Resources(groups: new[] { 3 })).DepartmentGroupId.Should().Be(3);
		}

		[Test]
		public void user_ids_match_regardless_of_case()
		{
			var assigner = new CallGroupAssigner(new[] { Group(1, members: new[] { "ABC-123" }) }, null);

			assigner.Assign(1, null, Resources(users: new[] { "abc-123" })).DepartmentGroupId.Should().Be(1);
		}

		[Test]
		public void a_tie_is_broken_by_the_nearest_station_when_every_tied_group_has_a_location()
		{
			var groups = new[]
			{
				Group(1, latitude: "39.75", longitude: "-104.95", members: new[] { "a" }),
				Group(2, latitude: "39.75", longitude: "-104.60", members: new[] { "b" })
			};
			var assigner = new CallGroupAssigner(groups, null);

			assigner.Assign(1, "39.76,-104.94", Resources(users: new[] { "a", "b" })).DepartmentGroupId.Should().Be(1);
			assigner.Assign(2, "39.76,-104.61", Resources(users: new[] { "a", "b" })).DepartmentGroupId.Should().Be(2);
		}

		[Test]
		public void a_tie_that_cannot_be_broken_leaves_the_call_unassigned()
		{
			var groups = new[] { Group(1, latitude: "39.75", longitude: "-104.95", members: new[] { "a" }), Group(2, members: new[] { "b" }) };
			var assigner = new CallGroupAssigner(groups, null);

			// No call location, and group 2 has no station location to measure from.
			var noLocation = assigner.Assign(1, null, Resources(users: new[] { "a", "b" }));
			noLocation.DepartmentGroupId.Should().BeNull();
			noLocation.Method.Should().Be(CallGroupAssignmentMethods.None);

			assigner.Assign(2, "39.76,-104.94", Resources(users: new[] { "a", "b" })).DepartmentGroupId.Should().BeNull();
		}

		[Test]
		public void responders_vote_only_when_nothing_dispatched_has_a_group()
		{
			var groups = new[] { Group(1, members: new[] { "a" }), Group(2, members: new[] { "b", "c" }) };
			var units = new[] { Unit(10, 1), Unit(99, null) };
			var assigner = new CallGroupAssigner(groups, units);

			var dispatchWins = assigner.Assign(1, null, Resources(units: new[] { 10 }), Resources(users: new[] { "b", "c" }));
			dispatchWins.DepartmentGroupId.Should().Be(1);
			dispatchWins.Method.Should().Be(CallGroupAssignmentMethods.Dispatch);

			// Unit 99 has no station, so the dispatch casts no vote and the responders decide.
			var responders = assigner.Assign(2, null, Resources(units: new[] { 99 }), Resources(users: new[] { "b", "c" }));
			responders.DepartmentGroupId.Should().Be(2);
			responders.Method.Should().Be(CallGroupAssignmentMethods.Response);
		}

		[Test]
		public void nothing_with_a_group_leaves_the_call_unassigned()
		{
			var assigner = new CallGroupAssigner(new[] { Group(1, Station1Fence) }, new[] { Unit(10, null) });

			var result = assigner.Assign(1, OutsideEverything, Resources(units: new[] { 10 }, users: new[] { "nobody" }), new CallGroupResources());

			result.DepartmentGroupId.Should().BeNull();
			result.Method.Should().Be(CallGroupAssignmentMethods.None);
		}

		[Test]
		public void unusable_geofences_and_units_of_unknown_groups_are_ignored()
		{
			var groups = new[] { Group(1, "not json", members: new[] { "a" }) };
			// Unit 20 points at a group from another department (not in the list).
			var assigner = new CallGroupAssigner(groups, new[] { Unit(20, 555), Unit(21, 555) });

			var result = assigner.Assign(1, InStation1, Resources(units: new[] { 20, 21 }, users: new[] { "a" }));

			result.DepartmentGroupId.Should().Be(1);
			result.Method.Should().Be(CallGroupAssignmentMethods.Dispatch);
		}
	}
}
