using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace RunCardsServiceTests
	{
		[TestFixture]
		public class when_evaluating_run_card_trigger_specificity
		{
			private static readonly DateTime Now = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

			private static RunCard CardWithTriggers(params RunCardTrigger[] triggers)
			{
				return new RunCard
				{
					RunCardId = 1,
					DepartmentId = 1,
					Name = "Test Card",
					Triggers = new List<RunCardTrigger>(triggers)
				};
			}

			[Test]
			public void should_be_null_for_null_card()
			{
				RunCardsService.GetTriggerMatchSpecificity(null, 3, 10, Now).Should().BeNull();
			}

			[Test]
			public void should_be_null_for_card_without_triggers()
			{
				var card = new RunCard { RunCardId = 1, Triggers = new List<RunCardTrigger>() };

				RunCardsService.GetTriggerMatchSpecificity(card, 3, 10, Now).Should().BeNull();
			}

			[Test]
			public void should_match_priority_only_trigger()
			{
				var card = CardWithTriggers(new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallPriority, Priority = 3 });

				RunCardsService.GetTriggerMatchSpecificity(card, 3, null, Now).Should().Be(1);
			}

			[Test]
			public void should_not_match_different_priority()
			{
				var card = CardWithTriggers(new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallPriority, Priority = 3 });

				RunCardsService.GetTriggerMatchSpecificity(card, 2, null, Now).Should().BeNull();
			}

			[Test]
			public void should_match_department_priority_above_system_range()
			{
				var card = CardWithTriggers(new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallPriority, Priority = 17 });

				RunCardsService.GetTriggerMatchSpecificity(card, 17, null, Now).Should().Be(1);
			}

			[Test]
			public void should_match_call_type_trigger()
			{
				var card = CardWithTriggers(new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallType, CallTypeId = 10 });

				RunCardsService.GetTriggerMatchSpecificity(card, 0, 10, Now).Should().Be(2);
			}

			[Test]
			public void should_not_match_call_type_trigger_when_call_has_no_type()
			{
				var card = CardWithTriggers(new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallType, CallTypeId = 10 });

				RunCardsService.GetTriggerMatchSpecificity(card, 0, null, Now).Should().BeNull();
			}

			[Test]
			public void should_match_priority_and_type_trigger_only_when_both_match()
			{
				var card = CardWithTriggers(new RunCardTrigger
				{
					TriggerType = (int)RunCardTriggerTypes.CallPriorityAndType,
					Priority = 3,
					CallTypeId = 10
				});

				RunCardsService.GetTriggerMatchSpecificity(card, 3, 10, Now).Should().Be(3);
				RunCardsService.GetTriggerMatchSpecificity(card, 3, 11, Now).Should().BeNull();
				RunCardsService.GetTriggerMatchSpecificity(card, 2, 10, Now).Should().BeNull();
			}

			[Test]
			public void should_return_strongest_specificity_when_multiple_triggers_match()
			{
				var card = CardWithTriggers(
					new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallPriority, Priority = 3 },
					new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallPriorityAndType, Priority = 3, CallTypeId = 10 });

				RunCardsService.GetTriggerMatchSpecificity(card, 3, 10, Now).Should().Be(3);
			}

			[Test]
			public void should_ignore_trigger_before_its_window_starts()
			{
				var card = CardWithTriggers(new RunCardTrigger
				{
					TriggerType = (int)RunCardTriggerTypes.CallPriority,
					Priority = 3,
					StartsOn = Now.AddHours(1)
				});

				RunCardsService.GetTriggerMatchSpecificity(card, 3, null, Now).Should().BeNull();
			}

			[Test]
			public void should_ignore_trigger_after_its_window_ends()
			{
				var card = CardWithTriggers(new RunCardTrigger
				{
					TriggerType = (int)RunCardTriggerTypes.CallPriority,
					Priority = 3,
					EndsOn = Now.AddHours(-1)
				});

				RunCardsService.GetTriggerMatchSpecificity(card, 3, null, Now).Should().BeNull();
			}

			[Test]
			public void should_match_trigger_inside_its_window()
			{
				var card = CardWithTriggers(new RunCardTrigger
				{
					TriggerType = (int)RunCardTriggerTypes.CallPriority,
					Priority = 3,
					StartsOn = Now.AddHours(-1),
					EndsOn = Now.AddHours(1)
				});

				RunCardsService.GetTriggerMatchSpecificity(card, 3, null, Now).Should().Be(1);
			}

			[Test]
			public void should_treat_null_window_bounds_as_open_ended()
			{
				var card = CardWithTriggers(new RunCardTrigger
				{
					TriggerType = (int)RunCardTriggerTypes.CallPriority,
					Priority = 3,
					StartsOn = null,
					EndsOn = null
				});

				RunCardsService.GetTriggerMatchSpecificity(card, 3, null, Now).Should().Be(1);
			}
		}
	}
}
