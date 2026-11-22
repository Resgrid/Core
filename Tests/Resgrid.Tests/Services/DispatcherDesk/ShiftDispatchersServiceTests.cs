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
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>The shift dispatch roster (M0324): signups, approval, standing dispatchers taken off one day, eligibility.</summary>
	[TestFixture]
	public class ShiftDispatchersServiceTests
	{
		private const int Dept = 7;
		private static readonly DateTime Now = new DateTime(2026, 11, 19, 13, 0, 0, DateTimeKind.Utc); // 08:00 EST

		private List<ShiftDispatcher> _rows;
		private List<DispatcherBreak> _breaks;
		private ShiftDispatchSetting _settings;
		private ShiftDayTimesRow _day;
		private HashSet<string> _eligible;
		private ShiftDispatchersService _service;

		[SetUp]
		public void SetUp()
		{
			_rows = new List<ShiftDispatcher>();
			_breaks = new List<DispatcherBreak>();
			_eligible = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ann", "bob" };
			_settings = new ShiftDispatchSetting { ShiftDispatchSettingId = "s1", DepartmentId = Dept, ShiftId = 3, IsEnabled = true };
			_day = new ShiftDayTimesRow
			{
				ShiftId = 3, ShiftDayId = 100, Day = new DateTime(2026, 11, 19), StartTime = "7:00 AM", EndTime = "7:00 PM", ShiftName = "Dispatch A",
				AssignmentType = (int)ShiftAssignmentTypes.Signup
			};

			var settingsRepo = new Mock<IShiftDispatchSettingsRepository>();
			settingsRepo.Setup(x => x.GetByShiftIdAsync(3)).ReturnsAsync(() => _settings);
			settingsRepo.Setup(x => x.GetByDepartmentIdAsync(Dept)).ReturnsAsync(() => new[] { _settings });

			var rowsRepo = new Mock<IShiftDispatchersRepository>();
			rowsRepo.Setup(x => x.GetByShiftIdAsync(3)).ReturnsAsync(() => _rows.ToList());
			rowsRepo.Setup(x => x.GetByDepartmentIdAsync(Dept)).ReturnsAsync(() => _rows.ToList());
			rowsRepo.Setup(x => x.GetShiftDayTimesByIdAsync(100)).ReturnsAsync(() => _day);
			rowsRepo.Setup(x => x.GetShiftDayTimesAsync(Dept, It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(() => new[] { _day });
			rowsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ShiftDispatcher>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ShiftDispatcher row, CancellationToken c, bool f) =>
				{
					if (string.IsNullOrWhiteSpace(row.ShiftDispatcherId))
					{
						row.ShiftDispatcherId = Guid.NewGuid().ToString();
						_rows.Add(row);
					}
					return row;
				});
			rowsRepo.Setup(x => x.DeleteAsync(It.IsAny<ShiftDispatcher>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ShiftDispatcher row, CancellationToken c) => _rows.Remove(row));
			rowsRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _rows.FirstOrDefault(x => x.ShiftDispatcherId == (string)id));

			var breaksRepo = new Mock<IDispatcherBreaksRepository>();
			breaksRepo.Setup(x => x.GetForShiftDayAsync(100)).ReturnsAsync(() => _breaks.ToList());
			breaksRepo.Setup(x => x.TryTransitionAsync(It.IsAny<DispatcherBreak>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var shifts = new Mock<IShiftsService>();
			shifts.Setup(x => x.GetShiftByIdAsync(3)).ReturnsAsync(new Shift { ShiftId = 3, DepartmentId = Dept, Name = "Dispatch A" });

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, TimeZone = "Eastern Standard Time" });
			departments.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), Dept, It.IsAny<bool>()))
				.ReturnsAsync((string u, int d, bool b) => new DepartmentMember { UserId = u, DepartmentId = Dept });

			var access = new Mock<IDispatchAccessService>();
			access.Setup(x => x.CanUseDispatchAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string u) => _eligible.Contains(u));

			_rowsRepo = rowsRepo;
			_events = new Mock<IEventAggregator>();
			_unitOfWork = new Mock<IUnitOfWork>();
			_service = new ShiftDispatchersService(settingsRepo.Object, rowsRepo.Object, breaksRepo.Object, shifts.Object, departments.Object, access.Object,
				_events.Object, _unitOfWork.Object)
			{ UtcNow = () => Now };
		}

		private Mock<IShiftDispatchersRepository> _rowsRepo;
		private Mock<IEventAggregator> _events;
		private Mock<IUnitOfWork> _unitOfWork;

		private ShiftDispatcher Standing(string userId, bool supervisor = false)
		{
			var row = new ShiftDispatcher
			{
				ShiftDispatcherId = Guid.NewGuid().ToString(), DepartmentId = Dept, ShiftId = 3, UserId = userId, IsSupervisor = supervisor,
				Source = (int)ShiftDispatcherSources.Assigned, Status = (int)ShiftDispatcherStatuses.Active, AddedOn = Now
			};
			_rows.Add(row);
			return row;
		}

		private IEnumerable<(string UserId, bool IsSupervisor)> StandingRoster() =>
			_rows.Where(x => x.ShiftDayId == null).Select(x => (x.UserId, x.IsSupervisor)).OrderBy(x => x.UserId).ToList();

		// ------------------------------------------------------------------ PR 589

		[Test]
		public async Task A_roster_replacement_that_fails_part_way_is_rolled_back_and_announces_nothing()
		{
			Standing("ann");
			_rowsRepo.Setup(x => x.SaveOrUpdateAsync(It.Is<ShiftDispatcher>(r => r.UserId == "bob"), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("write failed"));

			Func<Task> replace = () => _service.SetStandingRosterAsync(Dept, 3, new[] { ("bob", false) }, "boss");

			await replace.Should().ThrowAsync<InvalidOperationException>();
			_unitOfWork.Verify(x => x.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
			_unitOfWork.Verify(x => x.DiscardChanges(), Times.Once, "the delete of ann rolls back with the failed insert of bob");
			_unitOfWork.Verify(x => x.CommitChanges(), Times.Never);
			_events.Verify(x => x.SendMessageAsync(It.IsAny<DispatcherDeskUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async Task A_roster_replacement_commits_before_announcing_the_change()
		{
			var order = new List<string>();
			_unitOfWork.Setup(x => x.CommitChanges()).Callback(() => order.Add("commit"));
			_events.Setup(x => x.SendMessageAsync(It.IsAny<DispatcherDeskUpdatedEvent>())).Callback(() => order.Add("event")).Returns(Task.CompletedTask);

			(await _service.SetStandingRosterAsync(Dept, 3, new[] { ("ann", false) }, "boss")).Success.Should().BeTrue();

			order.Should().Equal(new[] { "commit", "event" });
		}

		[Test]
		public async Task Two_editors_adding_the_same_dispatcher_meet_the_unique_index_and_the_loser_saves_again()
		{
			var first = true;
			_rowsRepo.Setup(x => x.SaveOrUpdateAsync(It.Is<ShiftDispatcher>(r => r.UserId == "ann"), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ShiftDispatcher row, CancellationToken c, bool f) =>
				{
					if (first)
					{
						// The other editor committed ann first: this insert hits UX_ShiftDispatchers_Standing.
						first = false;
						Standing("ann", supervisor: true);
						throw new Npgsql.PostgresException("duplicate key", "ERROR", "ERROR", "23505");
					}

					return row;
				});

			var result = await _service.SetStandingRosterAsync(Dept, 3, new[] { ("ann", false) }, "boss");

			result.Success.Should().BeTrue();
			_rows.Where(x => x.ShiftDayId == null).Should().ContainSingle(x => x.UserId == "ann");
			_unitOfWork.Verify(x => x.DiscardChanges(), Times.Once);
			_unitOfWork.Verify(x => x.CommitChanges(), Times.Once);
		}

		[Test]
		public async Task An_editor_opened_before_someone_was_added_keeps_them()
		{
			// Manager A opened the shift with ann; manager B added bob since. A changes only the shift name.
			Standing("ann");
			Standing("bob");

			var result = await _service.UpdateStandingRosterAsync(Dept, 3, new[] { ("ann", false) }, new[] { ("ann", false) }, "boss-a");

			result.Success.Should().BeTrue();
			StandingRoster().Should().Equal(new[] { ("ann", false), ("bob", false) });
		}

		[Test]
		public async Task An_editor_removes_and_adds_only_what_it_changed_and_flags_only_where_it_changed_them()
		{
			Standing("ann", supervisor: true); // B made ann a supervisor after A opened
			Standing("bob");
			_eligible.Add("cat");

			// A showed ann (not supervisor) and bob; it takes bob off and puts cat on, leaving ann's box alone.
			var result = await _service.UpdateStandingRosterAsync(Dept, 3, new[] { ("ann", false), ("bob", false) }, new[] { ("ann", false), ("cat", true) }, "boss-a");

			result.Success.Should().BeTrue();
			StandingRoster().Should().Equal(new[] { ("ann", true), ("cat", true) });
		}

		[Test]
		public async Task Standing_dispatchers_the_editor_did_not_show_stay_unless_they_lost_the_dispatch_login()
		{
			Standing("ann");
			Standing("hidden-dispatcher");
			Standing("revoked");
			_eligible.Add("hidden-dispatcher");

			var result = await _service.UpdateStandingRosterAsync(Dept, 3, new[] { ("ann", false) }, new[] { ("ann", false) }, "boss");

			result.Success.Should().BeTrue("someone kept on the roster is not re-validated as a new addition");
			StandingRoster().Should().Equal(new[] { ("ann", false), ("hidden-dispatcher", false) }, "the page says a lost login leaves the roster on save");
		}

		[Test]
		public async Task An_editor_still_cannot_add_someone_without_the_dispatch_login()
		{
			Standing("ann");

			var result = await _service.UpdateStandingRosterAsync(Dept, 3, new[] { ("ann", false) }, new[] { ("ann", false), ("zed", false) }, "boss");

			result.Error.Should().Be(DispatcherDeskErrors.NotADispatcher);
			StandingRoster().Should().Equal(new[] { ("ann", false) });
		}

		[Test]
		public async Task Self_signup_is_refused_on_an_assigned_shift()
		{
			_day.AssignmentType = (int)ShiftAssignmentTypes.Assigned;

			(await _service.SignupForDayAsync(Dept, 100, "ann")).Error.Should().Be(DispatcherDeskErrors.NotPermitted);
			_rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_withdrawal_is_refused_when_the_row_is_no_longer_a_signup()
		{
			_breaks.Add(new DispatcherBreak { DispatcherBreakId = "b1", UserId = "ann", ShiftDayId = 100, State = (int)DispatcherBreakStates.Scheduled });
			(await _service.SignupForDayAsync(Dept, 100, "ann")).Success.Should().BeTrue();
			// A manager turned the signup into an assignment after the dispatcher's page read it.
			_rows.Single().Source = (int)ShiftDispatcherSources.Assigned;

			var result = await _service.RemoveFromDayAsync(Dept, 100, "ann", "ann", null, (int)ShiftDispatcherSources.Signup);

			result.Error.Should().Be(DispatcherDeskErrors.NotPermitted);
			_rows.Single().Status.Should().Be((int)ShiftDispatcherStatuses.Active);
			_breaks.Single().State.Should().Be((int)DispatcherBreakStates.Scheduled, "nothing is written or cancelled");
		}

		[Test]
		public async Task A_withdrawal_of_a_signup_still_goes_through()
		{
			(await _service.SignupForDayAsync(Dept, 100, "ann")).Success.Should().BeTrue();

			(await _service.RemoveFromDayAsync(Dept, 100, "ann", "ann", null, (int)ShiftDispatcherSources.Signup)).Value.Should().BeTrue();
			_rows.Single().Status.Should().Be((int)ShiftDispatcherStatuses.Denied);
		}

		[Test]
		public async Task A_signup_on_a_shift_requiring_approval_is_pending_until_reviewed()
		{
			_day.RequireApproval = true;

			var signup = await _service.SignupForDayAsync(Dept, 100, "ann");

			signup.Success.Should().BeTrue();
			signup.Value.Status.Should().Be((int)ShiftDispatcherStatuses.PendingApproval);
			(await _service.GetOnDutyAsync(Dept, Now.AddHours(2))).Should().BeEmpty();

			(await _service.ReviewSignupAsync(Dept, signup.Value.ShiftDispatcherId, true, "boss", null)).Success.Should().BeTrue();
			(await _service.GetOnDutyAsync(Dept, Now.AddHours(2))).Should().ContainSingle(x => x.UserId == "ann");
		}

		[Test]
		public async Task Signing_up_twice_or_without_the_dispatch_login_is_refused()
		{
			(await _service.SignupForDayAsync(Dept, 100, "ann")).Success.Should().BeTrue();
			(await _service.SignupForDayAsync(Dept, 100, "ann")).Error.Should().Be(DispatcherDeskErrors.AlreadyOnRoster);
			(await _service.SignupForDayAsync(Dept, 100, "zed")).Error.Should().Be(DispatcherDeskErrors.NotADispatcher);
		}

		[Test]
		public async Task Signup_is_refused_when_the_shift_takes_no_dispatchers()
		{
			_settings.IsEnabled = false;

			(await _service.SignupForDayAsync(Dept, 100, "ann")).Error.Should().Be(DispatcherDeskErrors.DispatchersNotEnabled);
		}

		[Test]
		public async Task Taking_a_standing_dispatcher_off_one_day_writes_a_denied_day_row_and_cancels_their_breaks()
		{
			await _service.SetStandingRosterAsync(Dept, 3, new[] { ("bob", true) }, "boss");
			_breaks.Add(new DispatcherBreak { DispatcherBreakId = "b1", UserId = "bob", ShiftDayId = 100, State = (int)DispatcherBreakStates.Scheduled });
			(await _service.GetOnDutyAsync(Dept, Now.AddHours(1))).Should().ContainSingle(x => x.UserId == "bob" && x.IsSupervisor);

			(await _service.RemoveFromDayAsync(Dept, 100, "bob", "boss", "Sick")).Value.Should().BeTrue();

			_rows.Should().Contain(x => x.UserId == "bob" && x.ShiftDayId == 100 && x.Status == (int)ShiftDispatcherStatuses.Denied);
			_rows.Should().Contain(x => x.UserId == "bob" && x.ShiftDayId == null && x.Status == (int)ShiftDispatcherStatuses.Active, "the standing roster is untouched");
			_breaks.Single().State.Should().Be((int)DispatcherBreakStates.Cancelled);
			(await _service.GetOnDutyAsync(Dept, Now.AddHours(1))).Should().BeEmpty();
		}

		[Test]
		public async Task The_standing_roster_is_replaced_but_day_rows_are_kept()
		{
			await _service.SetStandingRosterAsync(Dept, 3, new[] { ("ann", false), ("bob", false) }, "boss");
			await _service.AssignToDayAsync(Dept, 100, "ann", true, "boss");

			var result = await _service.SetStandingRosterAsync(Dept, 3, new[] { ("bob", true) }, "boss");

			result.Success.Should().BeTrue();
			_rows.Where(x => x.ShiftDayId == null).Select(x => x.UserId).Should().Equal("bob");
			_rows.Should().Contain(x => x.UserId == "ann" && x.ShiftDayId == 100);
		}

		[Test]
		public async Task The_standing_roster_refuses_people_without_the_dispatch_login()
		{
			var result = await _service.SetStandingRosterAsync(Dept, 3, new[] { ("ann", false), ("zed", false) }, "boss");

			result.Error.Should().Be(DispatcherDeskErrors.NotADispatcher);
			_rows.Should().BeEmpty();
		}
	}
}
