using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class CallStatusLinkageTests
	{
		private static CustomState UnitCustomState(params CustomStateDetail[] details) =>
			new CustomState { CustomStateId = 1, Type = (int)CustomStateTypes.Unit, Details = new List<CustomStateDetail>(details) };

		private static CustomState PersonnelCustomState(params CustomStateDetail[] details) =>
			new CustomState { CustomStateId = 2, Type = (int)CustomStateTypes.Personnel, Details = new List<CustomStateDetail>(details) };

		private static Dictionary<int, CustomStateDetail> UnitLookup(params CustomState[] customStates) =>
			CallStatusLinkage.BuildStatusLookup(new CustomStateService(null, null, null, null).GetDefaultUnitStatuses(), customStates, CustomStateTypes.Unit);

		private static Dictionary<int, CustomStateDetail> PersonnelLookup(params CustomState[] customStates) =>
			CallStatusLinkage.BuildStatusLookup(new CustomStateService(null, null, null, null).GetDefaultPersonStatuses(), customStates, CustomStateTypes.Personnel);

		[Test]
		public void an_explicit_call_destination_always_belongs_to_the_call_whatever_the_status()
		{
			var lookup = UnitLookup();

			new UnitState { State = (int)UnitStateTypes.Available, DestinationId = 5, DestinationType = (int)DestinationEntityTypes.Call }.BelongsToCall(lookup).Should().BeTrue();
			new UnitState { State = (int)UnitStateTypes.Cancelled, DestinationId = 5, DestinationType = (int)DestinationEntityTypes.Call }.BelongsToCall(lookup).Should().BeTrue();
			new UnitState { State = (int)UnitStateTypes.OutOfService, DestinationId = 5, DestinationType = (int)DestinationEntityTypes.Call }.BelongsToCall(lookup).Should().BeTrue();
		}

		[Test]
		public void a_custom_status_edited_to_no_destination_keeps_its_explicit_call_history()
		{
			var lookup = UnitLookup(UnitCustomState(new CustomStateDetail { CustomStateDetailId = 500, DetailType = (int)CustomStateDetailTypes.None }));

			new UnitState { State = 500, DestinationId = 5, DestinationType = (int)DestinationEntityTypes.Call }.BelongsToCall(lookup).Should().BeTrue();
		}

		[Test]
		public void station_and_poi_typed_rows_never_belong_to_a_call()
		{
			var lookup = UnitLookup();

			new UnitState { State = (int)UnitStateTypes.OnScene, DestinationId = 5, DestinationType = (int)DestinationEntityTypes.Station }.BelongsToCall(lookup).Should().BeFalse();
			new UnitState { State = (int)UnitStateTypes.OnScene, DestinationId = 5, DestinationType = (int)DestinationEntityTypes.Poi }.BelongsToCall(lookup).Should().BeFalse();
		}

		[Test]
		public void untyped_legacy_unit_rows_are_kept_unless_the_status_only_targets_stations()
		{
			var lookup = UnitLookup(UnitCustomState(
				new CustomStateDetail { CustomStateDetailId = 501, DetailType = (int)CustomStateDetailTypes.Stations },
				new CustomStateDetail { CustomStateDetailId = 502, DetailType = (int)CustomStateDetailTypes.CallsAndStations }));

			new UnitState { State = (int)UnitStateTypes.OnScene, DestinationId = 5 }.BelongsToCall(lookup).Should().BeTrue();
			new UnitState { State = (int)UnitStateTypes.Released, DestinationId = 5 }.BelongsToCall(lookup).Should().BeTrue("Released has no default button, so it can't be a station status");
			new UnitState { State = (int)UnitStateTypes.Available, DestinationId = 5 }.BelongsToCall(lookup).Should().BeFalse("the default Available status targets stations");
			new UnitState { State = (int)UnitStateTypes.Returning, DestinationId = 5 }.BelongsToCall(lookup).Should().BeFalse("the default Returning status targets stations");
			new UnitState { State = 501, DestinationId = 5 }.BelongsToCall(lookup).Should().BeFalse();
			new UnitState { State = 502, DestinationId = 5 }.BelongsToCall(lookup).Should().BeTrue();
		}

		[Test]
		public void untyped_personnel_rows_follow_the_default_personnel_destination_settings()
		{
			var lookup = PersonnelLookup();

			new ActionLog { ActionTypeId = (int)ActionTypes.Responding, DestinationId = 5 }.BelongsToCall(lookup).Should().BeTrue();
			new ActionLog { ActionTypeId = (int)ActionTypes.OnUnit, DestinationId = 5 }.BelongsToCall(lookup).Should().BeTrue();
			new ActionLog { ActionTypeId = (int)ActionTypes.RespondingToScene, DestinationId = 5 }.BelongsToCall(lookup).Should().BeTrue();
			new ActionLog { ActionTypeId = (int)ActionTypes.NotResponding, DestinationId = 5 }.BelongsToCall(lookup).Should().BeTrue("a status with no destination setting can't be a station status");
			new ActionLog { ActionTypeId = (int)ActionTypes.RespondingToStation, DestinationId = 5 }.BelongsToCall(lookup).Should().BeFalse();
			new ActionLog { ActionTypeId = (int)ActionTypes.AvailableStation, DestinationId = 5 }.BelongsToCall(lookup).Should().BeFalse();
		}

		[Test]
		public void personnel_lookup_ignores_unit_custom_states_and_vice_versa()
		{
			var unitState = UnitCustomState(new CustomStateDetail { CustomStateDetailId = 600, DetailType = (int)CustomStateDetailTypes.Stations });
			var personnelState = PersonnelCustomState(new CustomStateDetail { CustomStateDetailId = 601, DetailType = (int)CustomStateDetailTypes.Stations });

			var personnelLookup = PersonnelLookup(unitState, personnelState);

			personnelLookup.Should().ContainKey(601);
			personnelLookup.Should().NotContainKey(600);
		}

		[Test]
		public void built_in_unit_states_resolve_to_themselves()
		{
			CallStatusLinkage.ResolveUnitStateKind((int)UnitStateTypes.OnScene, null).Should().Be(UnitStateTypes.OnScene);
			CallStatusLinkage.ResolveUnitStateKind((int)UnitStateTypes.Enroute, new Dictionary<int, int>()).Should().Be(UnitStateTypes.Enroute);
			CallStatusLinkage.ResolveUnitStateKind(20, null).Should().BeNull("20 is inside the built-in range but not a UnitStateTypes value");
		}

		[Test]
		public void custom_unit_states_resolve_through_their_base_type()
		{
			var baseTypes = CallStatusLinkage.BuildUnitBaseTypeMap(new[]
			{
				UnitCustomState(
					new CustomStateDetail { CustomStateDetailId = 700, BaseType = (int)ActionBaseTypes.OnScene },
					new CustomStateDetail { CustomStateDetailId = 701, BaseType = (int)ActionBaseTypes.Responding },
					new CustomStateDetail { CustomStateDetailId = 702, BaseType = (int)ActionBaseTypes.Cleared },
					new CustomStateDetail { CustomStateDetailId = 703, BaseType = (int)ActionBaseTypes.None },
					new CustomStateDetail { CustomStateDetailId = 704, BaseType = (int)ActionBaseTypes.Staging, IsDeleted = true })
			});

			CallStatusLinkage.ResolveUnitStateKind(700, baseTypes).Should().Be(UnitStateTypes.OnScene);
			CallStatusLinkage.ResolveUnitStateKind(701, baseTypes).Should().Be(UnitStateTypes.Responding);
			CallStatusLinkage.ResolveUnitStateKind(702, baseTypes).Should().Be(UnitStateTypes.Released);
			CallStatusLinkage.ResolveUnitStateKind(703, baseTypes).Should().BeNull();
			CallStatusLinkage.ResolveUnitStateKind(704, baseTypes).Should().Be(UnitStateTypes.Staging, "deleted statuses still resolve for history");
			CallStatusLinkage.ResolveUnitStateKind(799, baseTypes).Should().BeNull();
		}
	}
}
