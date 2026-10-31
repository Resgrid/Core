using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ShiftTradeAuthorityTests
	{
		private const int Team = 100;
		private const int OtherTeam = 200;
		private const int Captain = 7;

		private Shift _shift;
		private ShiftSignupTrade _trade;

		[SetUp]
		public void SetUp()
		{
			_shift = new Shift { ShiftId = 1, Groups = new List<ShiftGroup> { new ShiftGroup { DepartmentGroupId = Team } } };
			_trade = new ShiftSignupTrade
			{
				ShiftSignupTradeId = 1,
				IsOpen = true,
				SourceShiftSignup = new ShiftSignup { ShiftSignupId = 5, UserId = "alice", DepartmentGroupId = Team, Shift = _shift }
			};
		}

		private static ShiftTradeAuthority As(string userId, ShiftManagementScope scope = null, ShiftTradeApproverSettings settings = null, params int[] roles)
		{
			return new ShiftTradeAuthority
			{
				UserId = userId,
				Scope = scope ?? ShiftManagementScope.None(),
				Settings = settings ?? ShiftTradeApproverSettings.Default(),
				RoleIds = new List<int>(roles)
			};
		}

		[Test]
		public void By_default_the_requester_and_the_slots_group_admins_pick()
		{
			As("alice").CanPick(_trade, _shift).Should().BeTrue();
			As("alice").IsApprover(_shift, Team).Should().BeFalse();

			var groupAdmin = As("gary", new ShiftManagementScope { GroupIds = new HashSet<int> { Team } });
			groupAdmin.CanPick(_trade, _shift).Should().BeTrue();
			groupAdmin.IsApprover(_shift, Team).Should().BeTrue();

			As("ollie", new ShiftManagementScope { GroupIds = new HashSet<int> { OtherTeam } }).CanPick(_trade, _shift).Should().BeFalse();
			As("bob").CanPick(_trade, _shift).Should().BeFalse();
		}

		[Test]
		public void Department_admins_always_decide()
		{
			var settings = new ShiftTradeApproverSettings { GroupAdmins = false, Requester = false };
			var admin = As("dana", new ShiftManagementScope { AllGroups = true }, settings);

			admin.CanPick(_trade, _shift).Should().BeTrue();
			admin.IsApprover(_shift, Team).Should().BeTrue();
			admin.HasApprovals.Should().BeTrue();
		}

		[Test]
		public void Turning_off_requesters_and_group_admins_leaves_only_admins_and_approver_roles()
		{
			var settings = new ShiftTradeApproverSettings { GroupAdmins = false, Requester = false, RoleIds = new List<int> { Captain } };

			As("alice", settings: settings).CanPick(_trade, _shift).Should().BeFalse();
			As("gary", new ShiftManagementScope { GroupIds = new HashSet<int> { Team } }, settings).CanPick(_trade, _shift).Should().BeFalse();

			var captain = As("carl", settings: settings, roles: Captain);
			captain.CanPick(_trade, _shift).Should().BeTrue();
			captain.IsApprover(_shift, Team).Should().BeTrue();
			captain.HasApprovals.Should().BeTrue();
		}

		[Test]
		public void A_picked_trade_waits_for_review_and_cannot_be_picked_again()
		{
			_trade.UserId = "bob";
			_trade.ApprovalPending = true;

			As("alice").CanPick(_trade, _shift).Should().BeFalse();
			As("alice").CanReview(_trade, _shift).Should().BeFalse();
			As("gary", new ShiftManagementScope { GroupIds = new HashSet<int> { Team } }).CanReview(_trade, _shift).Should().BeTrue();
		}

		[Test]
		public void The_requester_or_a_supervisor_cancels_until_the_trade_takes_effect()
		{
			As("alice").CanCancel(_trade, _shift).Should().BeTrue();
			As("bob").CanCancel(_trade, _shift).Should().BeFalse();
			As("gary", new ShiftManagementScope { GroupIds = new HashSet<int> { Team } }).CanCancel(_trade, _shift).Should().BeTrue();

			_trade.UserId = "bob";

			As("alice").CanCancel(_trade, _shift).Should().BeFalse();
			As("dana", new ShiftManagementScope { AllGroups = true }).CanCancel(_trade, _shift).Should().BeFalse();
		}

		[Test]
		public void Static_shift_trades_are_supervised_through_the_units_station()
		{
			var trade = new ShiftSignupTrade { ShiftSignupTradeId = 2, IsOpen = true, WorkshiftDayId = "wd", SourceUserId = "alice", DepartmentGroupId = Team };

			trade.IsWorkshiftTrade().Should().BeTrue();
			trade.GetSourceUserId().Should().Be("alice");
			As("gary", new ShiftManagementScope { GroupIds = new HashSet<int> { Team } }).CanPick(trade, null).Should().BeTrue();
			As("ollie", new ShiftManagementScope { GroupIds = new HashSet<int> { OtherTeam } }).CanCancel(trade, null).Should().BeFalse();
		}

		[Test]
		public void A_completed_static_shift_trade_puts_the_taker_on_the_requesters_unit()
		{
			var staffing = new ShiftDayStaffing
			{
				ShiftType = StaffedShiftTypes.Workshift,
				WorkshiftDayId = "wd",
				Personnel = new List<ShiftDayStaffingPerson>
				{
					new ShiftDayStaffingPerson { UserId = "alice", UnitId = 3 },
					new ShiftDayStaffingPerson { UserId = "alice", UnitId = 4 },
					new ShiftDayStaffingPerson { UserId = "carol", UnitId = 3 }
				},
				AutomaticPersonnel = new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = "alice", UnitId = 3 } }
			};
			var trades = new List<ShiftSignupTrade>
			{
				new ShiftSignupTrade { WorkshiftDayId = "wd", SourceUserId = "alice", UnitId = 3, UserId = "bob" },
				// Not taken yet, and a different day: neither changes anything.
				new ShiftSignupTrade { WorkshiftDayId = "wd", SourceUserId = "carol", UnitId = 3 },
				new ShiftSignupTrade { WorkshiftDayId = "other", SourceUserId = "alice", UnitId = 4, UserId = "dave" }
			};

			ShiftStaffingService.ApplyWorkshiftTrades(staffing, trades);

			staffing.Personnel.Should().ContainSingle(x => x.UserId == "bob" && x.UnitId == 3 && x.TradedFromUserId == "alice" && x.RosterSource == ShiftRosterSources.Trade);
			staffing.Personnel.Should().ContainSingle(x => x.UserId == "alice" && x.UnitId == 4);
			staffing.Personnel.Should().ContainSingle(x => x.UserId == "carol");
			staffing.AutomaticPersonnel.Should().ContainSingle(x => x.UserId == "bob");
		}
	}
}
