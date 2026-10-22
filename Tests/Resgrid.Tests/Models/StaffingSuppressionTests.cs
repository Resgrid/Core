using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// "Suppress (Mute) For These Staffing Levels": a saved level mutes that level only, a saved meaning mutes every level
	/// with that staffing base type, so a department on its own levels can mute "Unavailable" once instead of per level.
	/// </summary>
	[TestFixture]
	public class StaffingSuppressionTests
	{
		private const int OffLevelId = 19802;
		private const int OnLeaveLevelId = 19804;
		private const int OnDutyLevelId = 19801;

		private static readonly List<CustomState> Levels = new List<CustomState>
		{
			new CustomState
			{
				CustomStateId = 4548,
				DepartmentId = 14856,
				Type = (int)CustomStateTypes.Staffing,
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = OnDutyLevelId, CustomStateId = 4548, BaseType = (int)StaffingBaseTypes.Available },
					new CustomStateDetail { CustomStateDetailId = OffLevelId, CustomStateId = 4548, BaseType = (int)StaffingBaseTypes.Unavailable },
					new CustomStateDetail { CustomStateDetailId = OnLeaveLevelId, CustomStateId = 4548, BaseType = (int)StaffingBaseTypes.Unavailable }
				}
			}
		};

		private static DepartmentSuppressStaffingInfo Rule(bool enabled, params int[] levels) =>
			new DepartmentSuppressStaffingInfo { EnableSupressStaffing = enabled, StaffingLevelsToSupress = new List<int>(levels) };

		private static int UnavailableMeaning => StatusMatch.MeaningListValue((int)StaffingBaseTypes.Unavailable);

		[Test]
		public void A_saved_level_mutes_that_level_only()
		{
			var rule = Rule(true, OffLevelId);
			var baseTypes = StatusMatch.StaffingBaseTypes(Levels);

			StaffingSuppression.IsSuppressed(rule, OffLevelId, baseTypes).Should().BeTrue();
			StaffingSuppression.IsSuppressed(rule, OnLeaveLevelId, baseTypes).Should().BeFalse("it shares the meaning but was not picked");
			StaffingSuppression.IsSuppressed(rule, OnDutyLevelId, baseTypes).Should().BeFalse();
		}

		[Test]
		public void A_saved_meaning_mutes_every_custom_level_with_that_meaning()
		{
			var rule = Rule(true, UnavailableMeaning);
			var baseTypes = StatusMatch.StaffingBaseTypes(Levels);

			StaffingSuppression.HasMeanings(rule).Should().BeTrue();
			StaffingSuppression.IsSuppressed(rule, OffLevelId, baseTypes).Should().BeTrue();
			StaffingSuppression.IsSuppressed(rule, OnLeaveLevelId, baseTypes).Should().BeTrue();
			StaffingSuppression.IsSuppressed(rule, OnDutyLevelId, baseTypes).Should().BeFalse();
		}

		[Test]
		public void A_saved_meaning_mutes_the_built_in_level_with_that_meaning()
		{
			var rule = Rule(true, UnavailableMeaning);
			var baseTypes = StatusMatch.StaffingBaseTypes(new List<CustomState>());

			StaffingSuppression.IsSuppressed(rule, (int)UserStateTypes.Unavailable, baseTypes).Should().BeTrue();
			StaffingSuppression.IsSuppressed(rule, (int)UserStateTypes.Available, baseTypes).Should().BeFalse();
		}

		[Test]
		public void A_saved_built_in_level_keeps_muting_on_the_built_in_levels()
		{
			var rule = Rule(true, (int)UserStateTypes.Committed);

			StaffingSuppression.HasMeanings(rule).Should().BeFalse();
			StaffingSuppression.IsSuppressed(rule, (int)UserStateTypes.Committed, null).Should().BeTrue("exact levels need no base type lookup");
			StaffingSuppression.IsSuppressed(rule, (int)UserStateTypes.Unavailable, null).Should().BeFalse();
		}

		[Test]
		public void Nothing_is_muted_while_the_rule_is_off_or_empty()
		{
			var baseTypes = StatusMatch.StaffingBaseTypes(Levels);

			StaffingSuppression.IsSuppressed(Rule(false, OffLevelId, UnavailableMeaning), OffLevelId, baseTypes).Should().BeFalse();
			StaffingSuppression.IsSuppressed(Rule(true), OffLevelId, baseTypes).Should().BeFalse();
			StaffingSuppression.IsSuppressed(null, OffLevelId, baseTypes).Should().BeFalse();
		}

		[Test]
		public void A_meaning_without_a_base_type_lookup_matches_nothing()
		{
			StaffingSuppression.IsSuppressed(Rule(true, UnavailableMeaning), OffLevelId, null).Should().BeFalse();
		}

		[Test]
		public void A_level_with_no_meaning_is_never_muted_by_a_meaning()
		{
			var levels = new List<CustomState>
			{
				new CustomState
				{
					CustomStateId = 9,
					DepartmentId = 14856,
					Type = (int)CustomStateTypes.Staffing,
					Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = 901, CustomStateId = 9, BaseType = (int)StaffingBaseTypes.None } }
				}
			};

			StaffingSuppression.IsSuppressed(Rule(true, UnavailableMeaning, StatusMatch.MeaningListValue((int)StaffingBaseTypes.Available)), 901,
				StatusMatch.StaffingBaseTypes(levels)).Should().BeFalse();
		}
	}
}
