using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class StaffingEvidenceTests
	{
		private static readonly AdminAssistActor Actor = new(7, "admin");
		private static readonly DateTime Now = new(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc);
		private static ShiftDaySchedule Schedule(int id, string start, string end, params ShiftDayRosterEntry[] roster)
		{
			var shift = new Shift { ShiftId = id, DepartmentId = 7, StartTime = start, EndTime = end };
			return new ShiftDaySchedule { Shift = shift, Day = new ShiftDay { ShiftDayId = id, ShiftId = id, Shift = shift, Day = new DateTime(2026, 10, 31) }, Roster = roster.ToList() };
		}
		private static (StaffingEvidenceSource Source, Mock<IAuthorizationService> Authorization) Create(List<ShiftDaySchedule> schedules)
		{
			var shifts = new Mock<IShiftsService>();
			shifts.Setup(s => s.ReadSchedulesForAdministrationAsync(7, It.IsAny<DateTime>(), It.IsAny<DateTime>(), Now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(schedules);
			var departments = new Mock<IDepartmentsService>(); departments.Setup(d => d.GetDepartmentByIdAsync(7, true)).ReturnsAsync(new Department { DepartmentId = 7, TimeZone = "America/Los_Angeles" });
			var groups = new Mock<IDepartmentGroupsRepository>();
			groups.Setup(g => g.GetAllGroupsByDepartmentIdAsync(7)).ReturnsAsync(new[] {
				new DepartmentGroup { DepartmentId = 7, DepartmentGroupId = 1, Members = new List<DepartmentGroupMember> { new() { DepartmentId = 7, UserId = "a" } } },
				new DepartmentGroup { DepartmentId = 7, DepartmentGroupId = 2, Members = new List<DepartmentGroupMember>() }
			});
			var authorization = new Mock<IAuthorizationService>(); authorization.Setup(a => a.CanUserViewPersonAsync("admin", It.IsAny<string>(), 7)).ReturnsAsync(true);
			var membership = new Mock<IRecordsAuthorizationService>(); membership.Setup(m => m.IsAssignableMemberAsync(It.IsAny<string>(), 7)).ReturnsAsync(true);
			return (new StaffingEvidenceSource(shifts.Object, departments.Object, groups.Object, authorization.Object, membership.Object), authorization);
		}
		[Test]
		public async Task Overnight_DST_roster_counts_unique_people_and_ignores_pending_signups()
		{
			var first = Schedule(1, "19:00", "07:00", new() { UserId = "a" }, new() { UserId = "b", DepartmentGroupId = 2, ApprovalPending = true });
			var second = Schedule(2, "23:00", "03:00", new ShiftDayRosterEntry() { UserId = "a", DepartmentGroupId = 1 });
			first.Needs[1] = new() { [1] = 2 };
			first.Trades.Add(new ShiftSignupTrade { ShiftSignupTradeId = 10, ApprovalPending = true, UserId = "b" });
			second.Trades.Add(first.Trades[0]);
			var (source, _) = Create(new() { first, second });
			var result = (await source.ReadAsync(Actor, Now, CancellationToken.None)).ToDictionary(e => e.Id);
			Assert.That(result["groupsWithoutShiftCoverage"].Number, Is.EqualTo(1));
			Assert.That(result["singlePersonShiftGroups"].Number, Is.EqualTo(1));
			Assert.That(result["overlappingShiftPersonnel"].Number, Is.EqualTo(1));
			Assert.That(result["upcomingOpenShiftSlots"].Number, Is.EqualTo(2));
			Assert.That(result["unfilledShiftTrades"].Number, Is.EqualTo(1));
		}
		[Test]
		public void Hidden_roster_member_prevents_a_department_wide_pass()
		{
			var (source, authorization) = Create(new());
			authorization.Setup(a => a.CanUserViewPersonAsync("admin", "a", 7)).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadAsync(Actor, Now, CancellationToken.None));
		}
		[Test]
		public void Missing_schedule_source_is_not_an_empty_roster()
		{
			var (source, _) = Create(null);
			Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(Actor, Now, CancellationToken.None));
		}
	}
}
