using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace RunCardsServiceTests
	{
		[TestFixture]
		public class when_saving_a_run_card
		{
			private const int DepartmentId = 1;
			private const int OwnedUnitTypeId = 100;
			private const int OwnedStationId = 10;

			private Mock<IUnitsService> _unitsService;
			private Mock<IDepartmentGroupsService> _departmentGroupsService;
			private Mock<IRunCardsRepository> _runCardsRepository;
			private Mock<IRunCardTriggersRepository> _runCardTriggersRepository;

			private RunCardsService BuildService()
			{
				_unitsService = new Mock<IUnitsService>();
				_departmentGroupsService = new Mock<IDepartmentGroupsService>();
				_runCardsRepository = new Mock<IRunCardsRepository>();
				_runCardTriggersRepository = new Mock<IRunCardTriggersRepository>();

				_unitsService.Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId))
					.ReturnsAsync(new List<UnitType> { new UnitType { UnitTypeId = OwnedUnitTypeId, DepartmentId = DepartmentId, Type = "Engine" } });
				_departmentGroupsService.Setup(x => x.GetAllStationGroupsForDepartmentAsync(DepartmentId))
					.ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = OwnedStationId, DepartmentId = DepartmentId } });

				return new RunCardsService(
					_runCardsRepository.Object,
					_runCardTriggersRepository.Object,
					Mock.Of<IRunCardAlarmLevelsRepository>(),
					Mock.Of<IRunCardUnitRequirementsRepository>(),
					Mock.Of<IRunCardRoleRequirementsRepository>(),
					Mock.Of<IRunCardAvailabilitySelectionsRepository>(),
					Mock.Of<IStationCoverageRequirementsRepository>(),
					Mock.Of<ICallTypesRepository>(),
					Mock.Of<ICacheProvider>(),
					Mock.Of<IUnitOfWork>(),
					_unitsService.Object,
					Mock.Of<IPersonnelRolesService>(),
					_departmentGroupsService.Object,
					Mock.Of<ICustomStateService>());
			}

			[Test]
			public async Task should_clear_child_identifiers_when_creating_a_new_card()
			{
				// SaveOrUpdateAsync treats a non-zero child id as an update keyed on that id
				// alone, so a new card carrying one would rewrite another card's row.
				var card = CardWithLevels(1);
				card.Triggers = new List<RunCardTrigger> { new RunCardTrigger { RunCardTriggerId = 777, TriggerType = 0, Priority = 3 } };
				card.AlarmLevels.First().RunCardAlarmLevelId = 888;
				card.AlarmLevels.First().UnitRequirements = new List<RunCardUnitRequirement>
				{
					new RunCardUnitRequirement { RunCardUnitRequirementId = 999, UnitTypeId = OwnedUnitTypeId, RequiredCount = 1 }
				};

				await BuildService().SaveRunCardAsync(card);

				card.Triggers.First().RunCardTriggerId.Should().Be(0);
				card.AlarmLevels.First().RunCardAlarmLevelId.Should().Be(0);
				card.AlarmLevels.First().UnitRequirements.First().RunCardUnitRequirementId.Should().Be(0);
			}

			[Test]
			public void should_reject_a_child_identifier_from_another_run_card()
			{
				var service = BuildService();

				// The stored card owns trigger 1; the submission claims trigger 777.
				_runCardsRepository
					.Setup(x => x.GetByIdAsync(5))
					.ReturnsAsync(new RunCard { RunCardId = 5, DepartmentId = DepartmentId, Name = "Stored" });
				_runCardTriggersRepository
					.Setup(x => x.GetTriggersByRunCardIdAsync(5))
					.ReturnsAsync(new List<RunCardTrigger> { new RunCardTrigger { RunCardTriggerId = 1, RunCardId = 5 } });

				var card = CardWithLevels(1);
				card.RunCardId = 5;
				card.Triggers = new List<RunCardTrigger> { new RunCardTrigger { RunCardTriggerId = 777, TriggerType = 0, Priority = 3 } };

				Assert.ThrowsAsync<ArgumentException>(async () => await service.SaveRunCardAsync(card));
			}

			[Test]
			public void should_reject_a_home_station_from_another_department()
			{
				// The engine anchors its station cascade on this id without a department
				// check of its own, so a foreign station would steer selection.
				var card = CardWithLevels(1);
				card.HomeStationGroupId = 999;

				Assert.ThrowsAsync<ArgumentException>(async () => await BuildService().SaveRunCardAsync(card));
			}

			[Test]
			public void should_reject_a_unit_type_from_another_department()
			{
				var card = CardWithLevels(1);
				card.AlarmLevels.First().UnitRequirements = new List<RunCardUnitRequirement>
				{
					new RunCardUnitRequirement { UnitTypeId = 999, RequiredCount = 1 }
				};

				Assert.ThrowsAsync<ArgumentException>(async () => await BuildService().SaveRunCardAsync(card));
			}

			[Test]
			public void should_accept_references_owned_by_the_department()
			{
				var card = CardWithLevels(1);
				card.HomeStationGroupId = OwnedStationId;
				card.AlarmLevels.First().UnitRequirements = new List<RunCardUnitRequirement>
				{
					new RunCardUnitRequirement { UnitTypeId = OwnedUnitTypeId, RequiredCount = 1 }
				};

				Assert.DoesNotThrowAsync(async () => await BuildService().SaveRunCardAsync(card));
			}

			private static RunCard CardWithLevels(params int[] levels)
			{
				return new RunCard
				{
					RunCardId = 0,
					DepartmentId = DepartmentId,
					Name = "Structure Fire",
					AlarmLevels = levels.Select(l => new RunCardAlarmLevel { AlarmLevel = l }).ToList()
				};
			}

			[Test]
			public void should_reject_alarm_levels_below_one()
			{
				// Escalation starts at 1, so a level below it could never be matched.
				Assert.ThrowsAsync<ArgumentException>(async () =>
					await BuildService().SaveRunCardAsync(CardWithLevels(0, 1)));
			}

			[Test]
			public void should_reject_duplicate_alarm_levels()
			{
				// Otherwise this surfaces as a UX_RunCardAlarmLevels_Card_Level violation.
				Assert.ThrowsAsync<ArgumentException>(async () =>
					await BuildService().SaveRunCardAsync(CardWithLevels(1, 1)));
			}
		}

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
