using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class StatusTargetTests
	{
		[TestCase(null, StatusTargetKind.Default)]
		[TestCase("-1", StatusTargetKind.Default)]
		[TestCase("", StatusTargetKind.Default)]
		[TestCase("junk", StatusTargetKind.Default)]
		[TestCase("-2", StatusTargetKind.LeaveUnchanged)]
		[TestCase("0", StatusTargetKind.BuiltIn)]
		[TestCase("8", StatusTargetKind.BuiltIn)]
		[TestCase("1006", StatusTargetKind.BaseType)]
		[TestCase("999", StatusTargetKind.BuiltIn)]
		[TestCase("1999", StatusTargetKind.Default)] // not a base type
		public void stored_values_read_back(string stored, StatusTargetKind expected)
		{
			StatusTarget.FromStoredValue(stored).Kind.Should().Be(expected);
		}

		[Test]
		public void targets_round_trip_through_the_stored_integer()
		{
			foreach (var target in new[] { StatusTarget.Default, StatusTarget.LeaveUnchanged, StatusTarget.ForBuiltIn(8), StatusTarget.ForBaseType(ActionBaseTypes.Dispatched) })
			{
				var read = StatusTarget.FromStoredValue(target.ToStoredValue());
				read.Kind.Should().Be(target.Kind);
				read.Value.Should().Be(target.Value);
			}
		}

		[Test]
		public void built_in_units_keep_the_values_resgrid_always_set()
		{
			StatusTargetResolver.ResolveUnit(StatusTarget.Default, null, StatusTargetResolver.DispatchDefaultChain, (int)UnitStateTypes.Responding)
				.Value.Should().Be((int)UnitStateTypes.Responding);
			StatusTargetResolver.ResolveUnit(StatusTarget.ForBuiltIn((int)UnitStateTypes.Unavailable), null, StatusTargetResolver.ReleaseDefaultChain, (int)UnitStateTypes.Released)
				.Value.Should().Be((int)UnitStateTypes.Unavailable);
		}

		[TestCase(ActionBaseTypes.Dispatched, UnitStateTypes.Committed)]
		[TestCase(ActionBaseTypes.InQuarters, UnitStateTypes.Available)]
		[TestCase(ActionBaseTypes.Cleared, UnitStateTypes.Released)]
		[TestCase(ActionBaseTypes.Maintenance, UnitStateTypes.OutOfService)]
		public void a_base_type_target_on_built_in_units_is_the_built_in_status_with_that_meaning(ActionBaseTypes baseType, UnitStateTypes expected)
		{
			StatusTargetResolver.ResolveUnit(StatusTarget.ForBaseType(baseType), null, StatusTargetResolver.DispatchDefaultChain, 5)
				.Value.Should().Be((int)expected);
		}

		[Test]
		public void a_base_type_with_no_built_in_equivalent_leaves_built_in_units_alone_and_says_so()
		{
			var resolution = StatusTargetResolver.ResolveUnit(StatusTarget.ForBaseType(ActionBaseTypes.Transporting), null, StatusTargetResolver.DispatchDefaultChain, 5);

			resolution.Value.Should().BeNull();
			resolution.Issue.Should().Be(StatusTargetIssue.NoMatchingOption);
		}

		[Test]
		public void several_matching_options_use_the_first_by_order_and_say_so()
		{
			var set = new CustomState
			{
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = 30, Order = 3, BaseType = (int)ActionBaseTypes.Cleared },
					new CustomStateDetail { CustomStateDetailId = 20, Order = 2, BaseType = (int)ActionBaseTypes.Cleared },
					new CustomStateDetail { CustomStateDetailId = 10, Order = 1, BaseType = (int)ActionBaseTypes.Available }
				}
			};

			var resolution = StatusTargetResolver.ResolveUnit(StatusTarget.Default, set, StatusTargetResolver.ReleaseDefaultChain, 10);

			resolution.Value.Should().Be(20);
			resolution.BaseType.Should().Be(ActionBaseTypes.Cleared);
			resolution.Issue.Should().Be(StatusTargetIssue.SeveralMatchingOptions);
			resolution.Matches.Should().HaveCount(2);
		}

		[Test]
		public void the_default_chain_falls_through_to_the_next_base_type()
		{
			var set = new CustomState { Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = 10, BaseType = (int)ActionBaseTypes.InQuarters } } };

			StatusTargetResolver.ResolveUnit(StatusTarget.Default, set, StatusTargetResolver.ReleaseDefaultChain, 10).Value.Should().Be(10);
		}

		[TestCase(DestinationEntityTypes.Call, ActionTypes.RespondingToScene)]
		[TestCase(DestinationEntityTypes.Station, ActionTypes.RespondingToStation)]
		[TestCase(DestinationEntityTypes.None, ActionTypes.Responding)]
		public void personnel_responding_on_built_in_statuses_keeps_the_destination_specific_status(DestinationEntityTypes destination, ActionTypes expected)
		{
			StatusTargetResolver.ResolvePersonnel(StatusTarget.ForBaseType(ActionBaseTypes.Responding), null, StatusTargetResolver.DispatchDefaultChain, 6, destination)
				.Value.Should().Be((int)expected);
		}
	}
}
