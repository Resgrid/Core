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
	public class ShiftApprovalsWidgetProviderTests
	{
		private static readonly DateTime Today = new DateTime(2026, 10, 31);

		private Mock<IShiftsService> _shifts;
		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;

		private ShiftApprovalsWidgetProvider Provider(ShiftManagementScope scope, ShiftTradeApproverSettings approvers)
		{
			_shifts = new Mock<IShiftsService>(MockBehavior.Strict);
			_authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
			_authorization.Setup(x => x.GetShiftManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(scope);
			_settings = new Mock<IDepartmentSettingsService>(MockBehavior.Strict);
			_settings.Setup(x => x.GetShiftTradeApproverSettingsAsync(DepartmentId)).ReturnsAsync(approvers);
			_departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(ShiftsTodayWidgetProviderTests.PacificDepartment());
			_departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = "signer", FirstName = "Sam", LastName = "Signer" },
				new PersonName { UserId = "taker", FirstName = "Tia", LastName = "Taker" },
				new PersonName { UserId = "giver", FirstName = "Gus", LastName = "Giver" }
			});
			_groups = new Mock<IDepartmentGroupsService>(MockBehavior.Strict);
			_groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			return new ShiftApprovalsWidgetProvider(_shifts.Object, _authorization.Object, _settings.Object, _departments.Object, _groups.Object);
		}

		private void Pending()
		{
			var shift = new Shift { ShiftId = 1, DepartmentId = DepartmentId, Name = "A shift", StartTime = "07:00", EndTime = "19:00", Groups = new List<ShiftGroup>() };
			_shifts.Setup(x => x.GetAllShiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Shift> { shift });
			_shifts.Setup(x => x.GetPendingShiftSignupsAsync(DepartmentId)).ReturnsAsync(new List<ShiftSignup>
			{
				new ShiftSignup { ShiftSignupId = 1, ShiftId = 1, DepartmentGroupId = 10, UserId = "signer", ShiftDay = Today.AddDays(2) },
				new ShiftSignup { ShiftSignupId = 2, ShiftId = 1, DepartmentGroupId = 20, UserId = "signer", ShiftDay = Today.AddDays(1) },
				new ShiftSignup { ShiftSignupId = 3, ShiftId = 99, DepartmentGroupId = 10, UserId = "signer", ShiftDay = Today }
			});
			_shifts.Setup(x => x.GetPendingTradesAsync(DepartmentId)).ReturnsAsync(new List<ShiftSignupTrade>
			{
				new ShiftSignupTrade { ShiftSignupTradeId = 5, UserId = "taker", SourceShiftSignup = new ShiftSignup { ShiftId = 1, DepartmentGroupId = 10, UserId = "giver", ShiftDay = Today.AddDays(1) } },
				new ShiftSignupTrade { ShiftSignupTradeId = 6, UserId = "taker" }
			});
		}

		[Test]
		public async Task A_member_who_approves_nothing_reads_no_queues()
		{
			var provider = Provider(ShiftManagementScope.None(), ShiftTradeApproverSettings.Default());

			var data = (ShiftApprovalsWidgetData)await provider.GetDataAsync(For("shifts.approvals"), CancellationToken.None);

			data.HasApprovals.Should().BeFalse();
			_shifts.Verify(x => x.GetPendingShiftSignupsAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_group_supervisor_sees_signups_for_their_group_and_trades_they_approve()
		{
			var provider = Provider(new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } }, new ShiftTradeApproverSettings { GroupAdmins = true });
			Pending();

			var data = (ShiftApprovalsWidgetData)await provider.GetDataAsync(For("shifts.approvals"), CancellationToken.None);

			data.HasApprovals.Should().BeTrue();
			data.PendingSignupCount.Should().Be(1, "group 20 is not theirs and shift 99 is not the department's");
			data.PendingTradeCount.Should().Be(1, "a trade with no source signup never waits for approval");
			data.Items.Select(i => (i.Kind, i.Id)).Should().Equal(("trade", 5), ("signup", 1));
			data.Items[0].Should().BeEquivalentTo(new { UserName = "Tia Taker", RequesterName = "Gus Giver" });
		}

		[Test]
		public async Task A_trade_approver_role_decides_trades_without_supervising_signups()
		{
			var provider = Provider(ShiftManagementScope.None(), new ShiftTradeApproverSettings { GroupAdmins = false, RoleIds = new List<int> { 7 } });
			Pending();

			var data = (ShiftApprovalsWidgetData)await provider.GetDataAsync(For("shifts.approvals", roleIds: new[] { 7 }), CancellationToken.None);

			data.HasApprovals.Should().BeTrue();
			data.PendingSignupCount.Should().Be(0);
			data.PendingTradeCount.Should().Be(1);
		}
	}
}
