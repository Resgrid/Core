using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class ShiftsMineWidgetProviderTests
	{
		private static readonly DateTime Today = new DateTime(2026, 10, 31);

		private static Shift Shift(int id, int departmentId = DepartmentId) => new Shift
		{
			ShiftId = id, DepartmentId = departmentId, Name = "Shift " + id, Color = "#abcdef", StartTime = "07:00", EndTime = "19:00",
			Days = new List<ShiftDay> { new ShiftDay { ShiftDayId = id * 100, ShiftId = id, Day = Today.AddDays(1) } }
		};

		[Test]
		public async Task Lists_the_members_upcoming_signups_here_and_trades_sent_to_them()
		{
			var shifts = new Mock<IShiftsService>(MockBehavior.Strict);
			shifts.Setup(x => x.GetShiftSignupsForUserAsync(UserId)).ReturnsAsync(new List<ShiftSignup>
			{
				new ShiftSignup { ShiftSignupId = 1, ShiftId = 1, Shift = Shift(1), ShiftDay = Today.AddDays(1), UserId = UserId, DepartmentGroupId = 10, ApprovalPending = true },
				new ShiftSignup { ShiftSignupId = 2, ShiftId = 1, Shift = Shift(1), ShiftDay = Today.AddDays(-1), UserId = UserId },
				new ShiftSignup { ShiftSignupId = 3, ShiftId = 1, Shift = Shift(1), ShiftDay = Today.AddDays(2), UserId = UserId, Denied = true },
				new ShiftSignup { ShiftSignupId = 4, ShiftId = 2, Shift = Shift(2, OtherDepartmentId), ShiftDay = Today.AddDays(1), UserId = UserId },
				new ShiftSignup { ShiftSignupId = 5, ShiftId = 1, Shift = Shift(1), ShiftDay = Today.AddDays(60), UserId = UserId }
			});
			shifts.Setup(x => x.GetOpenTradeRequestsForUserAsync(UserId)).ReturnsAsync(new List<ShiftSignupTrade>
			{
				new ShiftSignupTrade { ShiftSignupTradeId = 7, SourceShiftSignup = new ShiftSignup { ShiftSignupId = 70, Shift = Shift(1), ShiftDay = Today.AddDays(3), UserId = "giver" } },
				new ShiftSignupTrade { ShiftSignupTradeId = 8, SourceShiftSignup = new ShiftSignup { ShiftSignupId = 80, Shift = Shift(2, OtherDepartmentId), ShiftDay = Today.AddDays(3), UserId = "giver" } }
			});
			var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(ShiftsTodayWidgetProviderTests.PacificDepartment());
			departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<PersonName> { new PersonName { UserId = "giver", FirstName = "Grace", LastName = "Hopper" } });
			var groups = new Mock<IDepartmentGroupsService>(MockBehavior.Strict);
			groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = DepartmentId, Name = "Station 10" } });
			var provider = new ShiftsMineWidgetProvider(shifts.Object, departments.Object, groups.Object);

			var data = (ShiftsMineWidgetData)await provider.GetDataAsync(For("shifts.mine"), CancellationToken.None);

			data.Signups.Should().ContainSingle("past, denied, other-department and beyond-window signups are left out");
			var signup = data.Signups.Single();
			signup.Should().BeEquivalentTo(new { ShiftSignupId = 1, ShiftDayId = (int?)100, GroupName = "Station 10", ApprovalPending = true });
			signup.StartUtc.Should().Be(new DateTime(2026, 11, 1, 15, 0, 0), "07:00 on 2026-11-01 is after the DST change (PST, UTC-8)");
			signup.Url.Should().Be("/User/Shifts/ViewShift?shiftDayId=100");
			data.TradeRequests.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ShiftSignupTradeId = 7, RequesterName = "Grace Hopper" });
		}
	}
}
