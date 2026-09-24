using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ShiftsServiceSchedulingTests
	{
		private const int DepartmentId = 1;
		private const int ShiftId = 10;
		private const int ShiftDayId = 55;
		private const int CrisisTeam = 100;
		private const int PeerTeam = 200;
		private const int Clinician = 1;

		private Mock<IShiftsRepository> _shiftsRepository;
		private Mock<IShiftPersonRepository> _shiftPersonRepository;
		private Mock<IShiftDaysRepository> _shiftDaysRepository;
		private Mock<IShiftGroupsRepository> _shiftGroupsRepository;
		private Mock<IShiftSignupRepository> _shiftSignupRepository;
		private Mock<IShiftSignupTradeRepository> _shiftSignupTradeRepository;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<IShiftSignupTradeUserRepository> _shiftSignupTradeUserRepository;
		private Mock<IShiftSignupTradeUserShiftsRepository> _shiftSignupTradeUserShiftsRepository;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IShiftGroupRolesRepository> _shiftGroupRolesRepository;
		private Mock<IEventAggregator> _eventAggregator;
		private Mock<IDepartmentSettingsService> _departmentSettingsService;

		private Department _department;
		private Shift _shift;
		private ShiftDay _day;
		private List<ShiftPerson> _personnel;
		private List<ShiftSignup> _signups;
		private List<ShiftSignupTrade> _trades;
		private List<ShiftSignup> _saved;
		private ShiftsService _service;

		[SetUp]
		public void SetUp()
		{
			_shiftsRepository = new Mock<IShiftsRepository>();
			_shiftPersonRepository = new Mock<IShiftPersonRepository>();
			_shiftDaysRepository = new Mock<IShiftDaysRepository>();
			_shiftGroupsRepository = new Mock<IShiftGroupsRepository>();
			_shiftSignupRepository = new Mock<IShiftSignupRepository>();
			_shiftSignupTradeRepository = new Mock<IShiftSignupTradeRepository>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();
			_shiftSignupTradeUserRepository = new Mock<IShiftSignupTradeUserRepository>();
			_shiftSignupTradeUserShiftsRepository = new Mock<IShiftSignupTradeUserShiftsRepository>();
			_departmentsService = new Mock<IDepartmentsService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_shiftGroupRolesRepository = new Mock<IShiftGroupRolesRepository>();
			_eventAggregator = new Mock<IEventAggregator>();
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();

			_department = new Department { DepartmentId = DepartmentId, TimeZone = "UTC" };
			_personnel = new List<ShiftPerson>();
			_signups = new List<ShiftSignup>();
			_trades = new List<ShiftSignupTrade>();
			_saved = new List<ShiftSignup>();

			_day = new ShiftDay { ShiftDayId = ShiftDayId, ShiftId = ShiftId, Day = DateTime.UtcNow.Date.AddDays(3) };
			_shift = new Shift
			{
				ShiftId = ShiftId,
				DepartmentId = DepartmentId,
				Name = "MCOT Day",
				StartTime = "07:00",
				EndTime = "19:00",
				AssignmentType = (int)ShiftAssignmentTypes.Signup,
				Days = new List<ShiftDay> { _day },
				// The department-wide load reads personnel straight off the shift (the JSON projection carries it).
				Personnel = _personnel
			};

			var groups = new List<ShiftGroup>
			{
				new ShiftGroup { ShiftGroupId = 1, ShiftId = ShiftId, DepartmentGroupId = CrisisTeam },
				new ShiftGroup { ShiftGroupId = 2, ShiftId = ShiftId, DepartmentGroupId = PeerTeam }
			};

			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(_department);
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int _, bool __) => new DepartmentMember { UserId = userId, DepartmentId = DepartmentId });
			_shiftsRepository.Setup(x => x.GetShiftAndDaysByShiftIdAsync(ShiftId)).ReturnsAsync(() => _shift);
			_shiftsRepository.Setup(x => x.GetByIdAsync(ShiftId)).ReturnsAsync(() => _shift);
			_shiftsRepository.Setup(x => x.GetShiftAndDaysByDepartmentIdAsync(DepartmentId)).ReturnsAsync(() => new List<Shift> { _shift });
			_shiftPersonRepository.Setup(x => x.GetAllShiftPersonsByShiftIdAsync(ShiftId)).ReturnsAsync(() => _personnel);
			_shiftGroupsRepository.Setup(x => x.GetShiftGroupsByShiftIdAsync(ShiftId)).ReturnsAsync(groups);
			_shiftGroupRolesRepository.Setup(x => x.GetShiftGroupRolesByGroupIdAsync(1))
				.ReturnsAsync(new List<ShiftGroupRole> { new ShiftGroupRole { ShiftGroupId = 1, PersonnelRoleId = Clinician, Required = 1 } });
			_shiftDaysRepository.Setup(x => x.GetShiftDayByIdAsync(ShiftDayId)).ReturnsAsync(() => _day);
			_shiftSignupRepository.Setup(x => x.GetAllShiftSignupsByShiftIdAndDateAsync(ShiftId, It.IsAny<DateTime>()))
				.ReturnsAsync(() => _signups.ToList());
			_shiftSignupRepository.Setup(x => x.GetShiftSignupsByDepartmentIdAndDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(() => _signups.ToList());
			_shiftSignupRepository.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync((object id) => _signups.FirstOrDefault(x => x.ShiftSignupId == (int)id));
			_shiftSignupRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ShiftSignup>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ShiftSignup signup, CancellationToken _, bool __) =>
				{
					if (signup.ShiftSignupId == 0)
						signup.ShiftSignupId = 900 + _saved.Count;

					_saved.Add(signup);
					return signup;
				});
			_shiftSignupTradeRepository.Setup(x => x.GetShiftSignupTradesByDepartmentIdAsync(DepartmentId, It.IsAny<DateTime>()))
				.ReturnsAsync(() => _trades.ToList());
			_personnelRolesService.Setup(x => x.GetAllRolesForUsersInDepartmentAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<string, List<PersonnelRole>>
				{
					{ "clinician", new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = Clinician, Name = "Clinician" } } }
				});
			_departmentSettingsService.Setup(x => x.GetTextToCallNumberForDepartmentAsync(DepartmentId)).ReturnsAsync("");

			_service = new ShiftsService(_shiftsRepository.Object, _shiftPersonRepository.Object, _shiftDaysRepository.Object, _shiftGroupsRepository.Object,
				_shiftSignupRepository.Object, _shiftSignupTradeRepository.Object, _personnelRolesService.Object, _shiftSignupTradeUserRepository.Object,
				_shiftSignupTradeUserShiftsRepository.Object, new Mock<IShiftStaffingRepository>().Object, new Mock<IShiftStaffingPersonRepository>().Object,
				_departmentsService.Object, _departmentGroupsService.Object, new Mock<IShiftGroupAssignmentsRepository>().Object, _shiftGroupRolesRepository.Object,
				_eventAggregator.Object, _departmentSettingsService.Object);
		}

		[Test]
		public async Task Signup_on_a_shift_that_requires_approval_waits_for_a_supervisor()
		{
			_shift.RequireApproval = true;

			var result = await _service.SignupUserForShiftDayAsync(ShiftDayId, CrisisTeam, "clinician");

			result.Success.Should().BeTrue();
			result.Item.ApprovalPending.Should().BeTrue();
			result.Item.DepartmentGroupId.Should().Be(CrisisTeam);
			_eventAggregator.Verify(x => x.SendMessage(It.Is<ShiftRosterChangedEvent>(e =>
				e.ChangeType == ShiftQueueTypes.SignupPendingApproval && e.ShiftSignupId == result.Item.ShiftSignupId)), Times.Once);
		}

		[Test]
		public async Task Signup_without_approval_goes_straight_on_the_roster()
		{
			var result = await _service.SignupUserForShiftDayAsync(ShiftDayId, PeerTeam, "someone");

			result.Success.Should().BeTrue();
			result.Item.ApprovalPending.Should().BeFalse();
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<ShiftRosterChangedEvent>()), Times.Never);
		}

		[Test]
		public async Task Signup_must_pick_a_group_that_is_on_the_shift()
		{
			(await _service.SignupUserForShiftDayAsync(ShiftDayId, 999, "someone")).Error.Should().Be(ShiftActionErrors.InvalidGroup);
			(await _service.SignupUserForShiftDayAsync(ShiftDayId, null, "someone")).Error.Should().Be(ShiftActionErrors.InvalidGroup);
			(await _service.SignupUserForShiftDayAsync(ShiftDayId, 0, "someone")).Error.Should().Be(ShiftActionErrors.InvalidGroup);
		}

		[Test]
		public async Task Signup_is_refused_for_someone_already_on_the_day()
		{
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "someone", GroupId = CrisisTeam });

			var result = await _service.SignupUserForShiftDayAsync(ShiftDayId, PeerTeam, "someone");

			result.Error.Should().Be(ShiftActionErrors.AlreadySignedUp);
		}

		[Test]
		public async Task Signup_is_refused_once_the_day_is_over()
		{
			_day.Day = DateTime.UtcNow.Date.AddDays(-3);

			var result = await _service.SignupUserForShiftDayAsync(ShiftDayId, CrisisTeam, "someone");

			result.Error.Should().Be(ShiftActionErrors.DayInPast);
		}

		[Test]
		public async Task Removing_a_standing_roster_person_takes_them_off_that_day_only()
		{
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "alice", GroupId = CrisisTeam });

			var result = await _service.RemoveUserFromShiftDayAsync(ShiftDayId, "alice", "supervisor", "sick");

			result.Success.Should().BeTrue();
			var marker = _saved.Single();
			marker.UserId.Should().Be("alice");
			marker.Denied.Should().BeTrue();
			marker.ShiftDay.Should().Be(_day.Day);
			marker.ReviewNote.Should().Be("sick");
			_eventAggregator.Verify(x => x.SendMessage(It.Is<ShiftRosterChangedEvent>(e => e.ChangeType == ShiftQueueTypes.DayRemoved)), Times.Once);

			// The same data now resolves to an empty day, while the standing roster itself is untouched.
			_signups.Add(marker);
			var schedule = await _service.GetShiftDayScheduleAsync(ShiftDayId);
			schedule.Roster.Should().BeEmpty();
			_personnel.Should().ContainSingle();
		}

		[Test]
		public async Task Removing_someone_who_is_not_on_the_day_fails()
		{
			var result = await _service.RemoveUserFromShiftDayAsync(ShiftDayId, "nobody", "supervisor", null);

			result.Error.Should().Be(ShiftActionErrors.NotOnShift);
		}

		[Test]
		public async Task Assigning_brings_back_a_person_who_was_removed_for_the_day()
		{
			var removed = new ShiftSignup { ShiftSignupId = 7, ShiftId = ShiftId, UserId = "alice", ShiftDay = _day.Day, DepartmentGroupId = CrisisTeam, Denied = true };
			_signups.Add(removed);

			var result = await _service.AssignUserToShiftDayAsync(ShiftDayId, "alice", PeerTeam, "supervisor");

			result.Success.Should().BeTrue();
			result.Item.ShiftSignupId.Should().Be(7);
			result.Item.Denied.Should().BeFalse();
			result.Item.DepartmentGroupId.Should().Be(PeerTeam);
			result.Item.AssignedByUserId.Should().Be("supervisor");
		}

		[Test]
		public async Task Assigning_someone_already_on_duty_in_that_group_fails()
		{
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "alice", GroupId = CrisisTeam });

			var result = await _service.AssignUserToShiftDayAsync(ShiftDayId, "alice", CrisisTeam, "supervisor");

			result.Error.Should().Be(ShiftActionErrors.AlreadyOnRoster);
		}

		[Test]
		public async Task Reviewing_a_signup_that_is_not_pending_fails()
		{
			_signups.Add(new ShiftSignup { ShiftSignupId = 8, ShiftId = ShiftId, UserId = "bob", ShiftDay = _day.Day });

			var result = await _service.ReviewShiftSignupAsync(8, true, "supervisor", null);

			result.Error.Should().Be(ShiftActionErrors.NotPending);
		}

		[Test]
		public async Task Denying_a_pending_signup_records_the_review()
		{
			_signups.Add(new ShiftSignup { ShiftSignupId = 8, ShiftId = ShiftId, UserId = "bob", ShiftDay = _day.Day, ApprovalPending = true });

			var result = await _service.ReviewShiftSignupAsync(8, false, "supervisor", "full");

			result.Success.Should().BeTrue();
			result.Item.ApprovalPending.Should().BeFalse();
			result.Item.Denied.Should().BeTrue();
			result.Item.ReviewedByUserId.Should().Be("supervisor");
			result.Item.ReviewNote.Should().Be("full");
			_eventAggregator.Verify(x => x.SendMessage(It.Is<ShiftRosterChangedEvent>(e => e.ChangeType == ShiftQueueTypes.SignupReviewed)), Times.Once);
		}

		[Test]
		public async Task Needs_count_the_resolved_roster_for_assigned_shifts_too()
		{
			_shift.AssignmentType = (int)ShiftAssignmentTypes.Assigned;
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "clinician", GroupId = CrisisTeam });

			var needs = await _service.GetShiftDayNeedsAsync(ShiftDayId);

			needs.Should().NotBeNull();
			needs[CrisisTeam][Clinician].Should().Be(0);
			(await _service.IsShiftDayFilledAsync(ShiftDayId)).Should().BeTrue();
		}

		[Test]
		public async Task On_duty_follows_a_night_shift_past_midnight_and_skips_pending_people()
		{
			_shift.StartTime = "19:00";
			_shift.EndTime = "07:00";
			_day.Day = new DateTime(2026, 10, 5);
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "alice", GroupId = CrisisTeam });
			_signups.Add(new ShiftSignup { ShiftSignupId = 3, ShiftId = ShiftId, UserId = "bob", ShiftDay = _day.Day, DepartmentGroupId = CrisisTeam, ApprovalPending = true });

			var afterMidnight = new DateTime(2026, 10, 6, 2, 0, 0, DateTimeKind.Utc);
			var nextMorning = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);

			(await _service.GetOnDutyUserIdsForGroupAsync(DepartmentId, CrisisTeam, afterMidnight)).Should().BeEquivalentTo(new[] { "alice" });
			(await _service.GetOnDutyUserIdsForGroupAsync(DepartmentId, CrisisTeam, nextMorning)).Should().BeEmpty();
		}

		[Test]
		public async Task On_duty_includes_ungrouped_standing_roster_members_of_the_group()
		{
			_day.Day = new DateTime(2026, 10, 5);
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "member" });
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "outsider" });
			_departmentGroupsService.Setup(x => x.GetAllMembersForGroupAsync(PeerTeam))
				.ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "member" } });

			var onDuty = await _service.GetOnDutyUserIdsForGroupsAsync(DepartmentId, new[] { PeerTeam }, new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));

			onDuty[PeerTeam].Should().BeEquivalentTo(new[] { "member" });
		}

		[Test]
		public async Task Finishing_a_trade_on_an_approval_shift_waits_for_a_supervisor()
		{
			_shift.RequireApproval = true;
			var source = new ShiftSignup { ShiftSignupId = 5, ShiftId = ShiftId, UserId = "alice", ShiftDay = _day.Day, DepartmentGroupId = CrisisTeam };
			_signups.Add(source);
			var trade = new ShiftSignupTrade { ShiftSignupTradeId = 50, SourceShiftSignupId = 5 };
			SetupTrade(trade, new ShiftSignupTradeUser { ShiftSignupTradeId = 50, UserId = "bob", Offered = true });

			var result = await _service.FinishTradeAsync(50, "alice", "bob", null);

			result.Success.Should().BeTrue();
			trade.UserId.Should().Be("bob");
			trade.ApprovalPending.Should().BeTrue();
			trade.IsTradeComplete().Should().BeFalse();
			_eventAggregator.Verify(x => x.SendMessage(It.Is<ShiftRosterChangedEvent>(e => e.ChangeType == ShiftQueueTypes.TradePendingApproval)), Times.Once);
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<ShiftTradeFilledEvent>()), Times.Never);
		}

		[Test]
		public async Task Finishing_a_trade_only_accepts_someone_who_offered()
		{
			_signups.Add(new ShiftSignup { ShiftSignupId = 5, ShiftId = ShiftId, UserId = "alice", ShiftDay = _day.Day });
			SetupTrade(new ShiftSignupTrade { ShiftSignupTradeId = 50, SourceShiftSignupId = 5 },
				new ShiftSignupTradeUser { ShiftSignupTradeId = 50, UserId = "bob", Offered = false },
				new ShiftSignupTradeUser { ShiftSignupTradeId = 50, UserId = "carol", Offered = true, Declined = true });

			(await _service.FinishTradeAsync(50, "alice", "bob", null)).Error.Should().Be(ShiftActionErrors.InvalidOffer);
			(await _service.FinishTradeAsync(50, "alice", "carol", null)).Error.Should().Be(ShiftActionErrors.InvalidOffer);
			(await _service.FinishTradeAsync(50, "mallory", "bob", null)).Error.Should().Be(ShiftActionErrors.NotAllowed);
		}

		[Test]
		public async Task Approving_a_pending_trade_makes_it_take_effect()
		{
			_signups.Add(new ShiftSignup { ShiftSignupId = 5, ShiftId = ShiftId, UserId = "alice", ShiftDay = _day.Day });
			var trade = new ShiftSignupTrade { ShiftSignupTradeId = 50, SourceShiftSignupId = 5, UserId = "bob", ApprovalPending = true };
			SetupTrade(trade);

			var result = await _service.ReviewTradeAsync(50, true, "supervisor", null);

			result.Success.Should().BeTrue();
			trade.ApprovalPending.Should().BeFalse();
			trade.IsTradeComplete().Should().BeTrue();
			trade.ReviewedByUserId.Should().Be("supervisor");
		}

		[Test]
		public async Task Requesting_a_trade_as_a_standing_roster_person_creates_their_day_slot()
		{
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "alice", GroupId = CrisisTeam });
			_departmentsService.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { UserId = "alice" }, new DepartmentMember { UserId = "bob" } });
			_shiftSignupTradeRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ShiftSignupTrade>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ShiftSignupTrade t, CancellationToken _, bool __) => { t.ShiftSignupTradeId = 77; return t; });
			_shiftSignupRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ShiftSignup>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ShiftSignup s, CancellationToken _, bool __) => { s.ShiftSignupId = 901; _signups.Add(s); return s; });

			var result = await _service.RequestTradeAsync(ShiftDayId, "alice", new List<string> { "bob", "alice", "" }, "family");

			result.Success.Should().BeTrue();
			var slot = _signups.Single();
			slot.UserId.Should().Be("alice");
			slot.DepartmentGroupId.Should().Be(CrisisTeam);
			result.Item.SourceShiftSignupId.Should().Be(901);
			result.Item.Users.Select(x => x.UserId).Should().BeEquivalentTo(new[] { "bob" });
			_eventAggregator.Verify(x => x.SendMessage(It.Is<ShiftTradeRequestedEvent>(e => e.ShiftSignupTradeId == 77)), Times.Once);
		}

		[Test]
		public async Task Requesting_a_trade_with_nobody_to_ask_fails()
		{
			_personnel.Add(new ShiftPerson { ShiftId = ShiftId, UserId = "alice", GroupId = CrisisTeam });

			var result = await _service.RequestTradeAsync(ShiftDayId, "alice", new List<string> { "alice" }, null);

			result.Error.Should().Be(ShiftActionErrors.NoUsers);
			_signups.Should().BeEmpty();
		}

		private void SetupTrade(ShiftSignupTrade trade, params ShiftSignupTradeUser[] users)
		{
			_shiftSignupTradeRepository.Setup(x => x.GetByIdAsync(trade.ShiftSignupTradeId)).ReturnsAsync(trade);
			_shiftSignupTradeRepository.Setup(x => x.SaveOrUpdateAsync(trade, It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(trade);
			_shiftSignupTradeUserRepository.Setup(x => x.GetShiftSignupTradeUsersByTradeIdAsync(trade.ShiftSignupTradeId)).ReturnsAsync(users.ToList());
		}
	}
}
