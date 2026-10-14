using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class StatusOptionIdsTests
	{
		// A fresh install: the department's first custom options have small ids that overlap the built-in values.
		private static List<CustomState> FreshInstallStates() => new List<CustomState>
		{
			new CustomState
			{
				CustomStateId = 1, Type = (int)CustomStateTypes.Unit,
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = 5, CustomStateId = 1, ButtonText = "In Quarters", BaseType = (int)ActionBaseTypes.InQuarters },
					new CustomStateDetail { CustomStateDetailId = 6, CustomStateId = 1, ButtonText = "Old Rolling", BaseType = (int)ActionBaseTypes.Responding, IsDeleted = true }
				}
			},
			new CustomState
			{
				CustomStateId = 2, Type = (int)CustomStateTypes.Personnel,
				Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = 7, CustomStateId = 2, ButtonText = "At Hall", BaseType = (int)ActionBaseTypes.Available } }
			}
		};

		[Test]
		public void a_small_custom_id_is_the_departments_option_not_a_built_in_status()
		{
			var baseTypes = CallStatusLinkage.BuildUnitBaseTypeMap(FreshInstallStates());

			// 5 is built-in Responding, but here it is the department's In Quarters option.
			UnitCallInvolvement.Classify(5, baseTypes).Should().Be(AvailabilityClass.Available);
			UnitCallInvolvement.KeepsStatusOnRelease(5, baseTypes).Should().BeTrue();
			CallStatusLinkage.ResolveUnitStateKind(5, baseTypes).Should().Be(UnitStateTypes.Available);
		}

		[Test]
		public void deleted_options_still_resolve_so_history_keeps_its_meaning()
		{
			var states = FreshInstallStates();

			StatusOptionIds.FindCustom(states, CustomStateTypes.Unit, 6).ButtonText.Should().Be("Old Rolling");
			UnitCallInvolvement.Classify(6, CallStatusLinkage.BuildUnitBaseTypeMap(states)).Should().Be(AvailabilityClass.Committed);
		}

		[Test]
		public void a_value_that_is_no_option_of_that_kind_is_built_in()
		{
			var states = FreshInstallStates();

			// 7 is a personnel option, so for a unit it is the built-in value (none: 7 is Staging).
			StatusOptionIds.FindCustom(states, CustomStateTypes.Unit, 7).Should().BeNull();
			UnitCallInvolvement.Classify(7, CallStatusLinkage.BuildUnitBaseTypeMap(states)).Should().Be(AvailabilityClass.Committed);
			CallStatusLinkage.ResolveUnitStateKind((int)UnitStateTypes.Returning, CallStatusLinkage.BuildUnitBaseTypeMap(states)).Should().Be(UnitStateTypes.Returning);
		}

		[Test]
		public void a_small_custom_personnel_id_clears_by_its_base_type()
		{
			var baseTypes = CallStatusLinkage.BuildBaseTypeMap(FreshInstallStates(), CustomStateTypes.Personnel);

			// 7 is built-in On Unit (not clearing), but here it is the department's available option.
			CallStatusLinkage.IsClearingPersonnelStatus(7, baseTypes).Should().BeTrue();
			CallStatusLinkage.IsClearingPersonnelStatus((int)ActionTypes.StandingBy, baseTypes).Should().BeTrue();
			CallStatusLinkage.IsClearingPersonnelStatus((int)ActionTypes.Responding, baseTypes).Should().BeFalse();
		}

		[Test]
		public void an_on_unit_status_follows_its_unit_while_the_link_is_the_units_latest_status()
		{
			var latest = PersonnelOnUnit.LatestUnitAvailability(new[]
			{
				new UnitState { UnitStateId = 100, UnitId = 1, State = (int)UnitStateTypes.Responding },
				new UnitState { UnitStateId = 101, UnitId = 2, State = (int)UnitStateTypes.Available }
			}, null);

			PersonnelOnUnit.FollowUnit(new ActionLog { ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 100 }, false, latest).Should().Be(AvailabilityClass.Committed);
			PersonnelOnUnit.FollowUnit(new ActionLog { ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 101 }, false, latest).Should().Be(AvailabilityClass.Available);

			// The unit moved on since (stale link), a custom option with the same id, or another status: no opinion.
			PersonnelOnUnit.FollowUnit(new ActionLog { ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 50 }, false, latest).Should().BeNull();
			PersonnelOnUnit.FollowUnit(new ActionLog { ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 100 }, true, latest).Should().BeNull();
			PersonnelOnUnit.FollowUnit(new ActionLog { ActionTypeId = (int)ActionTypes.Responding, UnitStateId = 100 }, false, latest).Should().BeNull();
		}
	}
}
