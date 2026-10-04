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
		public class when_removing_detached_run_card_references
		{
			// Sentry 3573: a unit type deleted after the card was built left its id on the
			// card, the editor could neither show nor remove it, and every save threw
			// "A unit type does not belong to this department."
			private const int DepartmentId = 1;
			private const int OwnedUnitTypeId = 100;
			private const int DeletedUnitTypeId = 101;
			private const int OwnedStationId = 10;
			private const int OwnedCallTypeId = 20;
			private const int DeletedCallTypeId = 21;
			private const int OwnedRoleId = 30;
			private const int DeletedRoleId = 31;
			private const int ActiveDetailId = 40;
			private const int DeletedDetailId = 41;

			private RunCardsService BuildService()
			{
				var unitsService = new Mock<IUnitsService>();
				unitsService.Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId))
					.ReturnsAsync(new List<UnitType> { new UnitType { UnitTypeId = OwnedUnitTypeId, DepartmentId = DepartmentId, Type = "Engine" } });

				var departmentGroupsService = new Mock<IDepartmentGroupsService>();
				departmentGroupsService.Setup(x => x.GetAllStationGroupsForDepartmentAsync(DepartmentId))
					.ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = OwnedStationId, DepartmentId = DepartmentId } });

				var callTypesRepository = new Mock<ICallTypesRepository>();
				callTypesRepository.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId))
					.ReturnsAsync(new List<CallType> { new CallType { CallTypeId = OwnedCallTypeId, DepartmentId = DepartmentId, Type = "Fire" } });

				var personnelRolesService = new Mock<IPersonnelRolesService>();
				personnelRolesService.Setup(x => x.GetRolesForDepartmentAsync(DepartmentId))
					.ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = OwnedRoleId, DepartmentId = DepartmentId, Name = "Driver" } });

				// Status details are soft-deleted, so a removed status button stays on the
				// state with IsDeleted set and drops out of GetActiveDetails.
				var customStateService = new Mock<ICustomStateService>();
				customStateService.Setup(x => x.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId))
					.ReturnsAsync(new List<CustomState>
					{
						new CustomState
						{
							CustomStateId = 1,
							DepartmentId = DepartmentId,
							Details = new List<CustomStateDetail>
							{
								new CustomStateDetail { CustomStateDetailId = ActiveDetailId, ButtonText = "Available" },
								new CustomStateDetail { CustomStateDetailId = DeletedDetailId, ButtonText = "Old", IsDeleted = true }
							}
						}
					});

				return new RunCardsService(
					Mock.Of<IRunCardsRepository>(),
					Mock.Of<IRunCardTriggersRepository>(),
					Mock.Of<IRunCardAlarmLevelsRepository>(),
					Mock.Of<IRunCardUnitRequirementsRepository>(),
					Mock.Of<IRunCardRoleRequirementsRepository>(),
					Mock.Of<IRunCardAvailabilitySelectionsRepository>(),
					Mock.Of<IStationCoverageRequirementsRepository>(),
					callTypesRepository.Object,
					Mock.Of<ICacheProvider>(),
					Mock.Of<IUnitOfWork>(),
					unitsService.Object,
					personnelRolesService.Object,
					departmentGroupsService.Object,
					customStateService.Object);
			}

			private static RunCard StoredCard()
			{
				return new RunCard
				{
					RunCardId = 0,
					DepartmentId = DepartmentId,
					Name = "Structure Fire",
					HomeStationGroupId = OwnedStationId,
					Triggers = new List<RunCardTrigger>
					{
						new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallPriority, Priority = 3 },
						new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallType, CallTypeId = OwnedCallTypeId }
					},
					AlarmLevels = new List<RunCardAlarmLevel>
					{
						new RunCardAlarmLevel
						{
							AlarmLevel = 1,
							UnitRequirements = new List<RunCardUnitRequirement> { new RunCardUnitRequirement { UnitTypeId = OwnedUnitTypeId, RequiredCount = 2 } },
							RoleRequirements = new List<RunCardRoleRequirement> { new RunCardRoleRequirement { PersonnelRoleId = OwnedRoleId, RequiredCount = 1 } }
						}
					},
					AvailabilitySelections = new List<RunCardAvailabilitySelection>
					{
						new RunCardAvailabilitySelection { SelectionType = 1, UnitTypeId = OwnedUnitTypeId, IsCustomState = true, StateId = ActiveDetailId },
						new RunCardAvailabilitySelection { SelectionType = 2, IsCustomState = false, StateId = 0 }
					}
				};
			}

			[Test]
			public async Task should_leave_a_card_with_only_owned_references_untouched()
			{
				var card = StoredCard();

				var removed = await BuildService().RemoveDetachedReferencesAsync(card);

				removed.Should().Be(0);
				card.HomeStationGroupId.Should().Be(OwnedStationId);
				card.Triggers.Should().HaveCount(2);
				card.AlarmLevels.First().UnitRequirements.Should().HaveCount(1);
				card.AlarmLevels.First().RoleRequirements.Should().HaveCount(1);
				card.AvailabilitySelections.Should().HaveCount(2);
			}

			[Test]
			public async Task should_drop_requirements_and_selections_for_a_deleted_unit_type()
			{
				var card = StoredCard();
				card.AlarmLevels.First().UnitRequirements.Add(new RunCardUnitRequirement { UnitTypeId = DeletedUnitTypeId, RequiredCount = 1 });
				card.AvailabilitySelections.Add(new RunCardAvailabilitySelection { SelectionType = 1, UnitTypeId = DeletedUnitTypeId, IsCustomState = false, StateId = 0 });

				var removed = await BuildService().RemoveDetachedReferencesAsync(card);

				removed.Should().Be(2);
				card.AlarmLevels.First().UnitRequirements.Select(r => r.UnitTypeId).Should().Equal(OwnedUnitTypeId);
				card.AvailabilitySelections.Should().NotContain(s => s.UnitTypeId == DeletedUnitTypeId);
			}

			[Test]
			public async Task should_drop_role_requirements_for_a_deleted_role()
			{
				var card = StoredCard();
				card.AlarmLevels.First().RoleRequirements.Add(new RunCardRoleRequirement { PersonnelRoleId = DeletedRoleId, RequiredCount = 1 });

				var removed = await BuildService().RemoveDetachedReferencesAsync(card);

				removed.Should().Be(1);
				card.AlarmLevels.First().RoleRequirements.Select(r => r.PersonnelRoleId).Should().Equal(OwnedRoleId);
			}

			[Test]
			public async Task should_drop_selections_for_a_deleted_status()
			{
				var card = StoredCard();
				card.AvailabilitySelections.Add(new RunCardAvailabilitySelection { SelectionType = 1, UnitTypeId = OwnedUnitTypeId, IsCustomState = true, StateId = DeletedDetailId });

				var removed = await BuildService().RemoveDetachedReferencesAsync(card);

				removed.Should().Be(1);
				card.AvailabilitySelections.Should().NotContain(s => s.StateId == DeletedDetailId);
			}

			[Test]
			public async Task should_drop_a_type_trigger_but_keep_a_priority_trigger_for_a_deleted_call_type()
			{
				// A priority-only trigger never reads its call type, so it must keep matching.
				var card = StoredCard();
				card.Triggers = new List<RunCardTrigger>
				{
					new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallPriority, Priority = 3, CallTypeId = DeletedCallTypeId },
					new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallType, CallTypeId = DeletedCallTypeId },
					new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallPriorityAndType, Priority = 2, CallTypeId = DeletedCallTypeId },
					new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallType, CallTypeId = OwnedCallTypeId }
				};

				var removed = await BuildService().RemoveDetachedReferencesAsync(card);

				removed.Should().Be(3);
				card.Triggers.Should().HaveCount(2);
				card.Triggers.Should().ContainSingle(t => t.TriggerType == (int)RunCardTriggerTypes.CallPriority && t.Priority == 3 && t.CallTypeId == null);
				card.Triggers.Should().ContainSingle(t => t.CallTypeId == OwnedCallTypeId);
			}

			[Test]
			public async Task should_clear_a_deleted_home_station()
			{
				var card = StoredCard();
				card.HomeStationGroupId = 999;

				var removed = await BuildService().RemoveDetachedReferencesAsync(card);

				removed.Should().Be(1);
				card.HomeStationGroupId.Should().BeNull();
			}

			[Test]
			public async Task should_leave_a_card_that_saves_after_every_kind_of_deletion()
			{
				// The pruning and the save validation must agree exactly, or the editor
				// still hands the user a card that cannot be saved.
				var card = StoredCard();
				card.HomeStationGroupId = 999;
				card.Triggers.Add(new RunCardTrigger { TriggerType = (int)RunCardTriggerTypes.CallType, CallTypeId = DeletedCallTypeId });
				card.AlarmLevels.First().UnitRequirements.Add(new RunCardUnitRequirement { UnitTypeId = DeletedUnitTypeId, RequiredCount = 1 });
				card.AlarmLevels.First().RoleRequirements.Add(new RunCardRoleRequirement { PersonnelRoleId = DeletedRoleId, RequiredCount = 1 });
				card.AvailabilitySelections.Add(new RunCardAvailabilitySelection { SelectionType = 1, UnitTypeId = DeletedUnitTypeId, IsCustomState = false, StateId = 0 });
				card.AvailabilitySelections.Add(new RunCardAvailabilitySelection { SelectionType = 1, UnitTypeId = OwnedUnitTypeId, IsCustomState = true, StateId = DeletedDetailId });

				var service = BuildService();

				Assert.ThrowsAsync<ArgumentException>(async () => await service.SaveRunCardAsync(card));

				var removed = await service.RemoveDetachedReferencesAsync(card);

				removed.Should().Be(6);
				Assert.DoesNotThrowAsync(async () => await service.SaveRunCardAsync(card));
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
