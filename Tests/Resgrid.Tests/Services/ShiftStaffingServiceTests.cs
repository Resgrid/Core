using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ShiftStaffingServiceTests
	{
		private const int DepartmentId = 1;
		private const int ShiftDayId = 55;
		private const int TeamA = 10;
		private const int TeamB = 11;
		private const string Supervisor = "aaaaaaaa-0000-0000-0000-000000000001";
		private const string Colleague = "aaaaaaaa-0000-0000-0000-000000000002";
		private const string Other = "aaaaaaaa-0000-0000-0000-000000000003";
		private const string Late = "aaaaaaaa-0000-0000-0000-000000000004";

		private Mock<IShiftsService> _shifts;
		private Mock<IWorkShiftsService> _workshifts;
		private Mock<IShiftStaffingRepository> _staffingRepository;
		private Mock<IShiftStaffingPersonRepository> _staffingPersonRepository;
		private Mock<IWorkshiftFillsRepository> _fillsRepository;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private List<ShiftStaffing> _records;
		private List<ShiftStaffingPerson> _savedPeople;
		private ShiftStaffingService _service;
		private Department _department;

		[SetUp]
		public void SetUp()
		{
			_shifts = new Mock<IShiftsService>();
			_workshifts = new Mock<IWorkShiftsService>();
			_staffingRepository = new Mock<IShiftStaffingRepository>();
			_staffingPersonRepository = new Mock<IShiftStaffingPersonRepository>();
			_fillsRepository = new Mock<IWorkshiftFillsRepository>();
			_units = new Mock<IUnitsService>();
			_departments = new Mock<IDepartmentsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_records = new List<ShiftStaffing>();
			_savedPeople = new List<ShiftStaffingPerson>();

			_department = new Department { DepartmentId = DepartmentId, TimeZone = "UTC" };
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(_department);
			_departments.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new[] { Supervisor, Colleague, Other, Late }
				.Select(x => new DepartmentMember { DepartmentId = DepartmentId, UserId = x }).ToList());

			_staffingRepository.Setup(x => x.GetShiftStaffingsByDepartmentIdAndDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int _, DateTime start, DateTime end) => _records.Where(x => x.ShiftDay >= start && x.ShiftDay < end).ToList());
			_staffingRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ShiftStaffing>(), It.IsAny<CancellationToken>(), true))
				.ReturnsAsync((ShiftStaffing staffing, CancellationToken _, bool __) => { staffing.ShiftStaffingId = 900; return staffing; });
			_staffingPersonRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ShiftStaffingPerson>(), It.IsAny<CancellationToken>(), true))
				.ReturnsAsync((ShiftStaffingPerson person, CancellationToken _, bool __) => { _savedPeople.Add(person); return person; });

			_shifts.Setup(x => x.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()))
				.ReturnsAsync(new List<ShiftDaySchedule>());
			_shifts.Setup(x => x.AssignUserToShiftDayAsync(ShiftDayId, It.IsAny<string>(), It.IsAny<int?>(), Supervisor, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Ok(new ShiftSignup()));
			_shifts.Setup(x => x.RemoveUserFromShiftDayAsync(ShiftDayId, It.IsAny<string>(), Supervisor, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<bool>.Ok(true));
			_workshifts.Setup(x => x.GetAllWorkshiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Workshift>());

			_service = new ShiftStaffingService(_shifts.Object, _workshifts.Object, _staffingRepository.Object, _staffingPersonRepository.Object,
				_fillsRepository.Object, _units.Object, _departments.Object, _groups.Object);
		}

		private static Shift NewShift(int shiftId, ShiftAssignmentTypes type, params int[] groupIds)
		{
			return new Shift
			{
				ShiftId = shiftId,
				DepartmentId = DepartmentId,
				Name = "Shift " + shiftId,
				AssignmentType = (int)type,
				StartTime = "07:00",
				EndTime = "19:00",
				Groups = groupIds.Select(x => new ShiftGroup { DepartmentGroupId = x }).ToList(),
				Personnel = new List<ShiftPerson>()
			};
		}

		private ShiftDaySchedule SetupSchedule(Shift shift, DateTime day, params ShiftDayRosterEntry[] roster)
		{
			var schedule = new ShiftDaySchedule
			{
				Shift = shift,
				Day = new ShiftDay { ShiftDayId = ShiftDayId, ShiftId = shift.ShiftId, Day = day, Shift = shift },
				Roster = roster.ToList()
			};

			_shifts.Setup(x => x.GetShiftDayScheduleAsync(ShiftDayId)).ReturnsAsync(schedule);

			return schedule;
		}

		private static ShiftDayRosterEntry OnDuty(string userId, int? groupId, ShiftRosterSources source = ShiftRosterSources.Assigned)
		{
			return new ShiftDayRosterEntry { UserId = userId, DepartmentGroupId = groupId, Source = source };
		}

		private ShiftStaffing AddRecord(int shiftId, DateTime day, DateTime addedOn, params (string UserId, int? GroupId)[] people)
		{
			var record = new ShiftStaffing
			{
				ShiftStaffingId = 100 + _records.Count,
				ShiftId = shiftId,
				DepartmentId = DepartmentId,
				ShiftDay = day,
				AddedByUserId = Supervisor,
				AddedOn = addedOn,
				Personnel = people.Select(x => new ShiftStaffingPerson { UserId = x.UserId, GroupId = x.GroupId }).ToList()
			};

			_records.Add(record);

			return record;
		}

		[Test]
		public async Task A_shift_day_uses_the_latest_approved_staffing_and_otherwise_its_on_duty_roster()
		{
			var day = DateTime.UtcNow.Date.AddDays(-10);
			var nights = NewShift(5, ShiftAssignmentTypes.Assigned);
			var signups = NewShift(6, ShiftAssignmentTypes.Signup);

			_shifts.Setup(x => x.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()))
				.ReturnsAsync(new List<ShiftDaySchedule>
				{
					new ShiftDaySchedule
					{
						Shift = nights, Day = new ShiftDay { ShiftDayId = 1, ShiftId = 5, Day = day, Shift = nights },
						Roster = new List<ShiftDayRosterEntry> { OnDuty(Colleague, null), new ShiftDayRosterEntry { UserId = Late, ApprovalPending = true } }
					},
					new ShiftDaySchedule
					{
						Shift = signups, Day = new ShiftDay { ShiftDayId = 2, ShiftId = 6, Day = day, Shift = signups },
						Roster = new List<ShiftDayRosterEntry> { OnDuty(Other, null, ShiftRosterSources.Signup), new ShiftDayRosterEntry { UserId = Late, ApprovalPending = true } }
					}
				});

			AddRecord(5, day, day.AddDays(1), (Supervisor, null));
			AddRecord(5, day, day.AddDays(2), (Colleague, null), (Other, null));

			var staffing = await _service.GetStaffingForRangeAsync(DepartmentId, day, day.AddDays(1));

			var manual = staffing.Single(x => x.ShiftId == 5);
			manual.Source.Should().Be(ShiftStaffingSources.Manual);
			manual.ShiftType.Should().Be(StaffedShiftTypes.Assigned);
			manual.ShiftStaffingId.Should().Be(101);
			manual.UserIds().Should().BeEquivalentTo(new[] { Colleague, Other });
			manual.AddedUserIds().Should().BeEquivalentTo(new[] { Other });
			manual.RemovedUserIds().Should().BeEmpty();
			manual.Hours.Should().Be(12);
			manual.IsOver.Should().BeTrue();

			var automatic = staffing.Single(x => x.ShiftId == 6);
			automatic.Source.Should().Be(ShiftStaffingSources.Automatic);
			automatic.ShiftType.Should().Be(StaffedShiftTypes.Signup);
			// A signup still waiting for approval is not on duty, so it does not staff the day.
			automatic.UserIds().Should().BeEquivalentTo(new[] { Other });
			automatic.Personnel.Single().RosterSource.Should().Be(ShiftRosterSources.Signup);
		}

		[Test]
		public async Task A_static_shift_day_is_staffed_by_its_unit_crews_until_fills_are_approved()
		{
			var start = DateTime.UtcNow.Date.AddDays(-20).AddHours(7);
			var workshift = new Workshift
			{
				WorkshiftId = "ws",
				DepartmentId = DepartmentId,
				Name = "Engine Week",
				Start = start,
				End = start.AddDays(1).AddHours(12),
				Days = new List<WorkshiftDay>
				{
					new WorkshiftDay { WorkshiftDayId = "d1", WorkshiftId = "ws", Day = start },
					new WorkshiftDay { WorkshiftDayId = "d2", WorkshiftId = "ws", Day = start.AddDays(1) }
				},
				Entities = new List<WorkshiftEntity>
				{
					new WorkshiftEntity { WorkshiftEntityId = "e3", WorkshiftId = "ws", BackingId = "3" },
					new WorkshiftEntity { WorkshiftEntityId = "e4", WorkshiftId = "ws", BackingId = "4" }
				},
				Fills = new List<WorkshiftFill>
				{
					new WorkshiftFill { WorkshiftFillId = "f1", WorkshiftId = "ws", WorkshiftDayId = "d2", WorkshiftEntityId = "e3", FilledById = Supervisor, Approved = true, AddedById = Colleague, AddedOn = start }
				}
			};

			_workshifts.Setup(x => x.GetAllWorkshiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Workshift> { workshift });
			_workshifts.Setup(x => x.GetWorkshiftByIdAsync("ws")).ReturnsAsync(workshift);
			_units.Setup(x => x.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 3, StationGroupId = TeamA }, new Unit { UnitId = 4 }
			});
			_units.Setup(x => x.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = 3, UserId = Colleague, Role = "Driver" },
				new UnitActiveRole { UnitId = 4, UserId = Other, Role = "Officer" },
				new UnitActiveRole { UnitId = 9, UserId = Late, Role = "Driver" }
			});

			var staffing = await _service.GetStaffingForRangeAsync(DepartmentId, start.Date, start.Date.AddDays(3), workshiftId: "ws");

			staffing.Should().HaveCount(2);

			var first = staffing.Single(x => x.WorkshiftDayId == "d1");
			first.ShiftType.Should().Be(StaffedShiftTypes.Workshift);
			first.Source.Should().Be(ShiftStaffingSources.Automatic);
			first.Hours.Should().Be(24);
			first.Personnel.Select(x => (x.UserId, x.UnitId, x.DepartmentGroupId)).Should().BeEquivalentTo(new[] { (Colleague, (int?)3, (int?)TeamA), (Other, (int?)4, (int?)null) });

			var second = staffing.Single(x => x.WorkshiftDayId == "d2");
			second.Source.Should().Be(ShiftStaffingSources.Manual);
			// The workshift ends 12 hours into its second day.
			second.Hours.Should().Be(12);
			second.SetByUserId.Should().Be(Colleague);
			second.Personnel.Select(x => (x.UserId, x.UnitId)).Should().BeEquivalentTo(new[] { (Supervisor, (int?)3) });
		}

		[Test]
		public async Task A_group_supervisor_replaces_only_their_groups_and_the_roster_follows()
		{
			var shift = NewShift(5, ShiftAssignmentTypes.Assigned, TeamA, TeamB);
			SetupSchedule(shift, DateTime.UtcNow.Date.AddDays(10), OnDuty(Colleague, TeamA), OnDuty(Other, TeamB));
			var scope = new ShiftManagementScope { GroupIds = new HashSet<int> { TeamA } };

			var result = await _service.SetShiftDayStaffingAsync(DepartmentId, ShiftDayId,
				new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Supervisor.ToUpperInvariant(), DepartmentGroupId = TeamA } },
				"Swap", Supervisor, scope);

			result.Success.Should().BeTrue();
			result.Item.RosterAdded.Should().Be(1);
			result.Item.RosterRemoved.Should().Be(1);
			_shifts.Verify(x => x.AssignUserToShiftDayAsync(ShiftDayId, Supervisor, TeamA, Supervisor, It.IsAny<CancellationToken>()), Times.Once);
			_shifts.Verify(x => x.RemoveUserFromShiftDayAsync(ShiftDayId, Colleague, Supervisor, "Swap", It.IsAny<CancellationToken>()), Times.Once);
			_shifts.Verify(x => x.RemoveUserFromShiftDayAsync(ShiftDayId, Other, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			// The other team's slot is kept on the approved staffing as it was.
			_savedPeople.Select(x => (x.UserId, x.GroupId)).Should().BeEquivalentTo(new[] { (Other, (int?)TeamB), (Supervisor, (int?)TeamA) });
			_savedPeople.Should().OnlyContain(x => x.ShiftStaffingId == 900 && x.Assigned);
		}

		[Test]
		public async Task A_slot_outside_the_supervisors_groups_is_refused()
		{
			var shift = NewShift(5, ShiftAssignmentTypes.Assigned, TeamA, TeamB);
			SetupSchedule(shift, DateTime.UtcNow.Date.AddDays(10));
			var scope = new ShiftManagementScope { GroupIds = new HashSet<int> { TeamA } };

			var result = await _service.SetShiftDayStaffingAsync(DepartmentId, ShiftDayId,
				new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Other, DepartmentGroupId = TeamB } }, null, Supervisor, scope);

			result.Error.Should().Be(ShiftActionErrors.NotAllowed);
			_staffingRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<ShiftStaffing>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Another_departments_shift_day_is_refused()
		{
			var shift = NewShift(5, ShiftAssignmentTypes.Assigned);
			SetupSchedule(shift, DateTime.UtcNow.Date.AddDays(10));

			var result = await _service.SetShiftDayStaffingAsync(DepartmentId + 1, ShiftDayId, new List<ShiftDayStaffingPerson>(), null, Supervisor,
				new ShiftManagementScope { AllGroups = true });

			result.Error.Should().Be(ShiftActionErrors.NotAllowed);
		}

		[Test]
		public async Task An_ended_day_records_who_worked_it_without_touching_the_roster()
		{
			var shift = NewShift(5, ShiftAssignmentTypes.Assigned);
			SetupSchedule(shift, DateTime.UtcNow.Date.AddDays(-5), OnDuty(Colleague, null));

			var result = await _service.SetShiftDayStaffingAsync(DepartmentId, ShiftDayId,
				new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Colleague }, new ShiftDayStaffingPerson { UserId = Other } },
				null, Supervisor, new ShiftManagementScope { AllGroups = true });

			result.Success.Should().BeTrue();
			_shifts.Verify(x => x.AssignUserToShiftDayAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_shifts.Verify(x => x.RemoveUserFromShiftDayAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_savedPeople.Single(x => x.UserId == Colleague).Assigned.Should().BeTrue();
			_savedPeople.Single(x => x.UserId == Other).Assigned.Should().BeFalse();
		}

		[Test]
		public async Task Someone_who_joined_the_roster_after_staffing_was_approved_is_not_taken_off()
		{
			var day = DateTime.UtcNow.Date.AddDays(10);
			var shift = NewShift(5, ShiftAssignmentTypes.Signup, TeamA);
			SetupSchedule(shift, day, OnDuty(Colleague, TeamA, ShiftRosterSources.Signup), OnDuty(Late, TeamA, ShiftRosterSources.Signup));
			AddRecord(5, day, DateTime.UtcNow.AddDays(-1), (Colleague, TeamA));

			var result = await _service.SetShiftDayStaffingAsync(DepartmentId, ShiftDayId,
				new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Colleague, DepartmentGroupId = TeamA } },
				null, Supervisor, new ShiftManagementScope { AllGroups = true });

			result.Success.Should().BeTrue();
			_shifts.Verify(x => x.RemoveUserFromShiftDayAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_shifts.Verify(x => x.AssignUserToShiftDayAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_roster_change_that_fails_is_reported_but_the_staffing_is_still_saved()
		{
			var shift = NewShift(5, ShiftAssignmentTypes.Assigned);
			SetupSchedule(shift, DateTime.UtcNow.Date.AddDays(10));
			_shifts.Setup(x => x.AssignUserToShiftDayAsync(ShiftDayId, Other, null, Supervisor, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.NotAllowed));

			var result = await _service.SetShiftDayStaffingAsync(DepartmentId, ShiftDayId,
				new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Other }, new ShiftDayStaffingPerson { UserId = "not-a-member" } },
				null, Supervisor, new ShiftManagementScope { AllGroups = true });

			result.Success.Should().BeTrue();
			result.Item.RosterErrors.Should().ContainSingle(x => x.Key == Other && x.Value == ShiftActionErrors.NotAllowed);
			// Someone who is not a current member of the department never lands on the staffing.
			_savedPeople.Select(x => x.UserId).Should().BeEquivalentTo(new[] { Other });
			_savedPeople.Single().Assigned.Should().BeFalse();
		}

		[Test]
		public async Task Clearing_needs_the_whole_shift_and_deletes_every_record_for_the_day()
		{
			var day = DateTime.UtcNow.Date.AddDays(-3);
			var shift = NewShift(5, ShiftAssignmentTypes.Assigned, TeamA, TeamB);
			SetupSchedule(shift, day);
			var first = AddRecord(5, day, day, (Colleague, TeamA));
			var second = AddRecord(5, day, day.AddHours(1), (Other, TeamB));
			AddRecord(6, day, day, (Other, null));

			(await _service.ClearShiftDayStaffingAsync(DepartmentId, ShiftDayId, new ShiftManagementScope { GroupIds = new HashSet<int> { TeamA } }))
				.Error.Should().Be(ShiftActionErrors.NotAllowed);

			var result = await _service.ClearShiftDayStaffingAsync(DepartmentId, ShiftDayId, new ShiftManagementScope { AllGroups = true });

			result.Success.Should().BeTrue();
			_staffingPersonRepository.Verify(x => x.DeleteAsync(It.IsAny<ShiftStaffingPerson>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
			_staffingRepository.Verify(x => x.DeleteAsync(first, It.IsAny<CancellationToken>()), Times.Once);
			_staffingRepository.Verify(x => x.DeleteAsync(second, It.IsAny<CancellationToken>()), Times.Once);
			_staffingRepository.Verify(x => x.DeleteAsync(It.Is<ShiftStaffing>(s => s.ShiftId == 6), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Static_shift_staffing_replaces_that_days_fills_and_needs_one_of_its_units()
		{
			var start = DateTime.UtcNow.Date.AddDays(-2).AddHours(7);
			var workshift = new Workshift
			{
				WorkshiftId = "ws",
				DepartmentId = DepartmentId,
				Start = start,
				End = start.AddDays(2),
				Entities = new List<WorkshiftEntity> { new WorkshiftEntity { WorkshiftEntityId = "e3", WorkshiftId = "ws", BackingId = "3" } },
				Fills = new List<WorkshiftFill>
				{
					new WorkshiftFill { WorkshiftFillId = "old", WorkshiftDayId = "d1", WorkshiftEntityId = "e3", FilledById = Late },
					new WorkshiftFill { WorkshiftFillId = "keep", WorkshiftDayId = "d2", WorkshiftEntityId = "e3", FilledById = Late }
				}
			};
			_workshifts.Setup(x => x.GetWorkshiftDayByIdAsync("d1")).ReturnsAsync(new WorkshiftDay { WorkshiftDayId = "d1", WorkshiftId = "ws", Day = start });
			_workshifts.Setup(x => x.GetWorkshiftByIdAsync("ws")).ReturnsAsync(workshift);
			var inserted = new List<WorkshiftFill>();
			_fillsRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<WorkshiftFill>(), It.IsAny<CancellationToken>(), true))
				.ReturnsAsync((WorkshiftFill fill, CancellationToken _, bool __) => { inserted.Add(fill); return fill; });

			(await _service.SetWorkshiftDayStaffingAsync(DepartmentId, "d1",
				new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Colleague, UnitId = 99 } }, Supervisor))
				.Error.Should().Be(ShiftActionErrors.InvalidRequest);

			(await _service.SetWorkshiftDayStaffingAsync(DepartmentId + 1, "d1",
				new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Colleague, UnitId = 3 } }, Supervisor))
				.Error.Should().Be(ShiftActionErrors.NotAllowed);

			var result = await _service.SetWorkshiftDayStaffingAsync(DepartmentId, "d1", new List<ShiftDayStaffingPerson>
			{
				new ShiftDayStaffingPerson { UserId = Colleague, UnitId = 3 },
				new ShiftDayStaffingPerson { UserId = Colleague.ToUpperInvariant(), UnitId = 3 },
				new ShiftDayStaffingPerson { UserId = Other, UnitId = 3 }
			}, Supervisor);

			result.Item.Should().Be(2);
			_fillsRepository.Verify(x => x.DeleteAsync(It.Is<WorkshiftFill>(f => f.WorkshiftFillId == "old"), It.IsAny<CancellationToken>()), Times.Once);
			_fillsRepository.Verify(x => x.DeleteAsync(It.Is<WorkshiftFill>(f => f.WorkshiftFillId == "keep"), It.IsAny<CancellationToken>()), Times.Never);
			inserted.Select(x => x.FilledById).Should().BeEquivalentTo(new[] { Colleague, Other });
			inserted.Should().OnlyContain(x => x.Approved && x.WorkshiftEntityId == "e3" && x.WorkshiftDayId == "d1" && x.AddedById == Supervisor);
		}

		[Test]
		public async Task Group_hours_count_the_groups_slots_its_child_groups_and_its_members_on_slots_without_a_group()
		{
			var day = DateTime.UtcNow.Date.AddDays(-4);
			var shift = NewShift(5, ShiftAssignmentTypes.Assigned);
			_shifts.Setup(x => x.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()))
				.ReturnsAsync(new List<ShiftDaySchedule>
				{
					new ShiftDaySchedule
					{
						Shift = shift, Day = new ShiftDay { ShiftDayId = 1, ShiftId = 5, Day = day, Shift = shift },
						Roster = new List<ShiftDayRosterEntry> { OnDuty(Supervisor, 101), OnDuty(Colleague, 200), OnDuty(Other, null), OnDuty(Late, null) }
					}
				});
			_groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = 100 },
				new DepartmentGroup { DepartmentGroupId = 101, ParentDepartmentGroupId = 100 },
				new DepartmentGroup { DepartmentGroupId = 200 }
			});
			_groups.Setup(x => x.GetAllMembersForGroupAsync(100)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = Other } });
			_groups.Setup(x => x.GetAllMembersForGroupAsync(101)).ReturnsAsync(new List<DepartmentGroupMember>());

			var report = await _service.GetOnShiftHoursAsync(DepartmentId, day, day.AddDays(1), groupId: 100);

			report.People.Select(x => x.UserId).Should().BeEquivalentTo(new[] { Supervisor, Other });
			report.People.Should().OnlyContain(x => x.TotalHours == 12 && x.AutomaticHours == 12 && x.ManualHours == 0);
			report.Occurrences.Should().ContainSingle();
		}

		[Test]
		public void A_static_shift_day_is_stored_in_utc_and_shown_in_department_time()
		{
			// 07:00-19:00 Eastern on 2026-01-15 is 12:00-00:00 UTC.
			var startUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
			var workshift = new Workshift { WorkshiftId = "ws", Name = "Day", Start = startUtc, End = startUtc.AddHours(12) };
			var day = new WorkshiftDay { WorkshiftDayId = "d1", WorkshiftId = "ws", Day = startUtc };
			var eastern = new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" };

			var staffing = ShiftStaffingService.FromWorkshiftDay(workshift, day, null, null, new ShiftStaffingService.UnitLookups(), eastern, null);

			staffing.Start.Should().Be(new DateTime(2026, 1, 15, 7, 0, 0));
			staffing.End.Should().Be(new DateTime(2026, 1, 15, 19, 0, 0));
			staffing.Day.Should().Be(new DateTime(2026, 1, 15));
			staffing.Hours.Should().Be(12);
		}

		[Test]
		public void A_static_shift_day_runs_24_hours_from_its_day_cut_short_by_the_static_shifts_end()
		{
			var day = new DateTime(2026, 10, 1, 7, 0, 0);

			ShiftTimeWindow.GetWorkshiftDayWindow(day.AddDays(3), day).Should().Be((day, day.AddDays(1)));
			ShiftTimeWindow.GetWorkshiftDayWindow(day.AddHours(10), day).Should().Be((day, day.AddHours(10)));
			// An end that is not after the day (bad data) does not produce a negative window.
			ShiftTimeWindow.GetWorkshiftDayWindow(day.AddHours(-1), day).Should().Be((day, day.AddDays(1)));
		}
	}
}
