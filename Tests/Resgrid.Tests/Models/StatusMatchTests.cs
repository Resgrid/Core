using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class StatusMatchTests
	{
		private static readonly List<CustomState> States = new List<CustomState>
		{
			new CustomState
			{
				CustomStateId = 1, Type = (int)CustomStateTypes.Unit,
				Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = 12707, BaseType = (int)ActionBaseTypes.InQuarters } }
			},
			new CustomState
			{
				CustomStateId = 2, Type = (int)CustomStateTypes.Staffing,
				Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = 5001, BaseType = (int)StaffingBaseTypes.OffDuty } }
			}
		};

		[TestCase(null, true)]
		[TestCase("", true)]
		[TestCase("-1", true)]
		[TestCase("12707", true)]
		[TestCase("12706", false)]
		[TestCase("b23", true)] // In Quarters
		[TestCase("b0", false)] // Available
		[TestCase("0,b23", true)]
		public void unit_tokens_match_by_id_or_meaning(string token, bool expected)
		{
			var baseTypes = StatusMatch.UnitBaseTypes(States);

			(StatusMatch.IsAny(token) || StatusMatch.MatchesAny(token, 12707, baseTypes)).Should().Be(expected);
		}

		[Test]
		public void a_meaning_matches_built_in_statuses_too()
		{
			var baseTypes = StatusMatch.UnitBaseTypes(States);

			StatusMatch.MatchesAny("b2", (int)UnitStateTypes.Responding, baseTypes).Should().BeTrue();
			StatusMatch.MatchesAny("b2", (int)UnitStateTypes.OnScene, baseTypes).Should().BeFalse();
		}

		[Test]
		public void staffing_meanings_use_staffing_base_types()
		{
			var baseTypes = StatusMatch.StaffingBaseTypes(States);

			StatusMatch.MatchesAny("b5", 5001, baseTypes).Should().BeTrue(); // Off Duty
			StatusMatch.MatchesAny("b4", (int)UserStateTypes.OnShift, baseTypes).Should().BeTrue();
		}

		[Test]
		public void list_meaning_values_never_collide_with_status_values()
		{
			StatusMatch.MeaningListValue((int)StaffingBaseTypes.OffDuty).Should().Be(-105);
			StatusMatch.MeaningFromListValue(-105).Should().Be((int)StaffingBaseTypes.OffDuty);
			StatusMatch.MeaningFromListValue(1002).Should().BeNull();
			StatusMatch.MeaningFromListValue(0).Should().BeNull();
		}

		[Test]
		public void run_card_meaning_selections_match_by_base_type_and_status_selections_by_id()
		{
			var meaning = new RunCardAvailabilitySelection { StateId = RunCardSelectionMeaning.StateIdFor((int)ActionBaseTypes.InQuarters), IsCustomState = false };
			var exact = new RunCardAvailabilitySelection { StateId = 12707, IsCustomState = true };

			RunCardSelectionMeaning.Matches(meaning, 12707, true, (int)ActionBaseTypes.InQuarters).Should().BeTrue();
			RunCardSelectionMeaning.Matches(meaning, (int)UnitStateTypes.Available, false, (int)ActionBaseTypes.Available).Should().BeFalse();
			RunCardSelectionMeaning.Matches(exact, 12707, true, (int)ActionBaseTypes.InQuarters).Should().BeTrue();
			RunCardSelectionMeaning.Matches(exact, 12707, false, (int)ActionBaseTypes.InQuarters).Should().BeFalse();
		}
	}
}
