using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ShiftRosterBuilderTests
	{
		private static readonly DateTime Day1 = new DateTime(2026, 10, 5);
		private static readonly DateTime Day2 = new DateTime(2026, 10, 6);

		private const int ShiftId = 10;
		private const int CrisisTeam = 100;
		private const int PeerTeam = 200;
		private const int Clinician = 1;
		private const int PeerSpecialist = 2;

		private static Shift MakeShift(params ShiftPerson[] personnel)
		{
			return new Shift
			{
				ShiftId = ShiftId,
				Personnel = personnel.ToList(),
				Groups = new List<ShiftGroup>
				{
					new ShiftGroup
					{
						DepartmentGroupId = CrisisTeam,
						Roles = new List<ShiftGroupRole>
						{
							new ShiftGroupRole { PersonnelRoleId = Clinician, Required = 1 },
							new ShiftGroupRole { PersonnelRoleId = PeerSpecialist, Required = 1 }
						}
					},
					new ShiftGroup { DepartmentGroupId = PeerTeam, Roles = new List<ShiftGroupRole>() }
				}
			};
		}

		private static ShiftSignup Signup(int id, string userId, DateTime day, int? groupId = CrisisTeam)
		{
			return new ShiftSignup { ShiftSignupId = id, ShiftId = ShiftId, UserId = userId, ShiftDay = day, DepartmentGroupId = groupId };
		}

		private static Dictionary<string, List<PersonnelRole>> Roles(params (string UserId, int[] RoleIds)[] users)
		{
			return users.ToDictionary(x => x.UserId, x => x.RoleIds.Select(r => new PersonnelRole { PersonnelRoleId = r }).ToList());
		}

		[Test]
		public void Build_puts_standing_roster_on_every_day()
		{
			var shift = MakeShift(new ShiftPerson { UserId = "alice", GroupId = CrisisTeam });

			var day1 = ShiftRosterBuilder.Build(shift, Day1, null, null);
			var day2 = ShiftRosterBuilder.Build(shift, Day2, null, null);

			day1.Should().ContainSingle(x => x.UserId == "alice" && x.Source == ShiftRosterSources.Assigned && x.DepartmentGroupId == CrisisTeam);
			day2.Should().ContainSingle(x => x.UserId == "alice");
		}

		[Test]
		public void Build_lists_pending_signups_but_leaves_out_denied_ones()
		{
			var shift = MakeShift();
			var pending = Signup(1, "bob", Day1);
			pending.ApprovalPending = true;
			var denied = Signup(2, "carol", Day1);
			denied.Denied = true;

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { pending, denied, Signup(3, "dave", Day1) }, null);

			roster.Should().ContainSingle(x => x.UserId == "bob" && x.ApprovalPending && !x.IsOnDuty());
			roster.Should().ContainSingle(x => x.UserId == "dave" && x.Source == ShiftRosterSources.Signup && x.IsOnDuty());
			roster.Should().NotContain(x => x.UserId == "carol");
		}

		[Test]
		public void Build_ignores_signups_for_other_days_and_shifts()
		{
			var shift = MakeShift();
			var otherShift = Signup(1, "bob", Day1);
			otherShift.ShiftId = 99;

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { otherShift, Signup(2, "carol", Day2) }, null);

			roster.Should().BeEmpty();
		}

		[Test]
		public void Build_marks_supervisor_added_people()
		{
			var shift = MakeShift();
			var added = Signup(1, "erin", Day1, PeerTeam);
			added.AssignedByUserId = "supervisor";

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { added }, null);

			roster.Should().ContainSingle(x => x.UserId == "erin" && x.Source == ShiftRosterSources.SupervisorAssigned && x.DepartmentGroupId == PeerTeam);
		}

		[Test]
		public void Build_takes_a_standing_roster_person_off_only_the_day_they_were_removed()
		{
			var shift = MakeShift(new ShiftPerson { UserId = "alice", GroupId = CrisisTeam });
			var removed = Signup(1, "alice", Day1);
			removed.Denied = true;

			ShiftRosterBuilder.Build(shift, Day1, new[] { removed }, null).Should().BeEmpty();
			ShiftRosterBuilder.Build(shift, Day2, new[] { removed }, null).Should().ContainSingle(x => x.UserId == "alice");
		}

		[Test]
		public void Build_shows_a_standing_roster_persons_own_day_slot_once_as_assigned()
		{
			var shift = MakeShift(new ShiftPerson { UserId = "alice", GroupId = CrisisTeam });

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { Signup(5, "alice", Day1) }, null);

			roster.Should().ContainSingle();
			roster[0].Source.Should().Be(ShiftRosterSources.Assigned);
			roster[0].ShiftSignupId.Should().Be(5);
		}

		[Test]
		public void Build_keeps_a_standing_roster_person_in_their_group_when_they_sign_up_for_another()
		{
			var shift = MakeShift(new ShiftPerson { UserId = "alice", GroupId = CrisisTeam });

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { Signup(5, "ALICE", Day1, PeerTeam) }, null);

			roster.Should().ContainSingle(x => x.UserId == "alice" && x.DepartmentGroupId == CrisisTeam && x.Source == ShiftRosterSources.Assigned && x.IsOnDuty());
			roster.Should().ContainSingle(x => x.DepartmentGroupId == PeerTeam && x.ShiftSignupId == 5);
		}

		[Test]
		public void Build_keeps_a_standing_roster_person_on_duty_while_a_signup_waits_for_approval()
		{
			var shift = MakeShift(new ShiftPerson { UserId = "alice", GroupId = CrisisTeam });
			var pending = Signup(5, "alice", Day1, PeerTeam);
			pending.ApprovalPending = true;

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { pending }, null);

			roster.Should().ContainSingle(x => x.DepartmentGroupId == CrisisTeam && x.IsOnDuty());
			roster.Should().ContainSingle(x => x.DepartmentGroupId == PeerTeam && x.ApprovalPending);
		}

		[Test]
		public void Build_gives_the_slot_to_the_taker_of_a_completed_give_away_trade()
		{
			var shift = MakeShift(new ShiftPerson { UserId = "alice", GroupId = CrisisTeam });
			var slot = Signup(5, "alice", Day1);
			var trade = new ShiftSignupTrade { ShiftSignupTradeId = 50, SourceShiftSignupId = 5, SourceShiftSignup = slot, UserId = "bob" };

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { slot }, new[] { trade });

			roster.Should().ContainSingle();
			roster[0].UserId.Should().Be("bob");
			roster[0].Source.Should().Be(ShiftRosterSources.Trade);
			roster[0].TradedFromUserId.Should().Be("alice");
			roster[0].ShiftSignupTradeId.Should().Be(50);
			roster[0].DepartmentGroupId.Should().Be(CrisisTeam);
		}

		[Test]
		public void Build_swaps_both_days_of_a_completed_swap_back_trade()
		{
			var shift = MakeShift();
			var aliceDay1 = Signup(5, "alice", Day1);
			var bobDay2 = Signup(6, "bob", Day2);
			var trade = new ShiftSignupTrade
			{
				ShiftSignupTradeId = 50,
				SourceShiftSignupId = 5,
				SourceShiftSignup = aliceDay1,
				TargetShiftSignupId = 6,
				TargetShiftSignup = bobDay2
			};

			var day1 = ShiftRosterBuilder.Build(shift, Day1, new[] { aliceDay1, bobDay2 }, new[] { trade });
			var day2 = ShiftRosterBuilder.Build(shift, Day2, new[] { aliceDay1, bobDay2 }, new[] { trade });

			day1.Should().ContainSingle(x => x.UserId == "bob" && x.TradedFromUserId == "alice");
			day2.Should().ContainSingle(x => x.UserId == "alice" && x.TradedFromUserId == "bob");
		}

		[Test]
		public void Build_ignores_trades_waiting_for_approval_or_denied()
		{
			var shift = MakeShift();
			var slot = Signup(5, "alice", Day1);
			var pending = new ShiftSignupTrade { SourceShiftSignupId = 5, SourceShiftSignup = slot, UserId = "bob", ApprovalPending = true };
			var denied = new ShiftSignupTrade { SourceShiftSignupId = 5, SourceShiftSignup = slot, UserId = "carol", Denied = true };

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { slot }, new[] { pending, denied });

			roster.Should().ContainSingle(x => x.UserId == "alice" && x.Source == ShiftRosterSources.Signup);
		}

		[Test]
		public void Build_leaves_a_removed_traded_slot_open()
		{
			var shift = MakeShift(new ShiftPerson { UserId = "alice", GroupId = CrisisTeam });
			var slot = Signup(5, "alice", Day1);
			slot.Denied = true;
			var trade = new ShiftSignupTrade { SourceShiftSignupId = 5, SourceShiftSignup = slot, UserId = "bob" };

			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { slot }, new[] { trade });

			roster.Should().BeEmpty();
		}

		[Test]
		public void CalculateNeeds_counts_each_person_against_one_role_only()
		{
			var shift = MakeShift();
			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { Signup(1, "both", Day1) }, null);
			var roles = Roles(("both", new[] { Clinician, PeerSpecialist }));

			var needs = ShiftRosterBuilder.CalculateNeeds(shift, roster, roles);

			// One person holding both roles covers one of the two slots, not both.
			needs[CrisisTeam].Values.Sum().Should().Be(1);
		}

		[Test]
		public void CalculateNeeds_places_single_role_people_first()
		{
			var shift = MakeShift();
			var signups = new[] { Signup(1, "both", Day1), Signup(2, "clinicianOnly", Day1) };
			var roster = ShiftRosterBuilder.Build(shift, Day1, signups, null);
			var roles = Roles(("both", new[] { Clinician, PeerSpecialist }), ("clinicianOnly", new[] { Clinician }));

			var needs = ShiftRosterBuilder.CalculateNeeds(shift, roster, roles);

			needs[CrisisTeam][Clinician].Should().Be(0);
			needs[CrisisTeam][PeerSpecialist].Should().Be(0);
		}

		[Test]
		public void CalculateNeeds_does_not_count_pending_people_or_other_groups()
		{
			var shift = MakeShift();
			var pending = Signup(1, "clinician", Day1);
			pending.ApprovalPending = true;
			var otherGroup = Signup(2, "peer", Day1, PeerTeam);
			var roster = ShiftRosterBuilder.Build(shift, Day1, new[] { pending, otherGroup }, null);
			var roles = Roles(("clinician", new[] { Clinician }), ("peer", new[] { PeerSpecialist }));

			var needs = ShiftRosterBuilder.CalculateNeeds(shift, roster, roles);

			needs[CrisisTeam][Clinician].Should().Be(1);
			needs[CrisisTeam][PeerSpecialist].Should().Be(1);
		}

		[Test]
		public void CalculateNeeds_counts_assigned_staff_and_never_goes_below_zero()
		{
			var shift = MakeShift(
				new ShiftPerson { UserId = "c1", GroupId = CrisisTeam },
				new ShiftPerson { UserId = "c2", GroupId = CrisisTeam });
			var roster = ShiftRosterBuilder.Build(shift, Day1, null, null);
			var roles = Roles(("C1", new[] { Clinician }), ("c2", new[] { Clinician }));

			var needs = ShiftRosterBuilder.CalculateNeeds(shift, roster, roles);

			needs[CrisisTeam][Clinician].Should().Be(0);
			needs[CrisisTeam][PeerSpecialist].Should().Be(1);
		}

		[Test]
		public void CalculateNeeds_includes_groups_without_role_requirements()
		{
			var shift = MakeShift();

			var needs = ShiftRosterBuilder.CalculateNeeds(shift, new List<ShiftDayRosterEntry>(), null);

			needs.Should().ContainKey(PeerTeam);
			needs[PeerTeam].Should().BeEmpty();
		}

		[Test]
		public void Schedule_reports_filled_and_open_slots()
		{
			var schedule = new ShiftDaySchedule
			{
				Needs = new Dictionary<int, Dictionary<int, int>>
				{
					{ CrisisTeam, new Dictionary<int, int> { { Clinician, 0 }, { PeerSpecialist, 2 } } },
					{ PeerTeam, new Dictionary<int, int>() }
				}
			};

			schedule.IsFilled().Should().BeFalse();
			schedule.OpenSlots().Should().Be(2);

			schedule.Needs[CrisisTeam][PeerSpecialist] = 0;
			schedule.IsFilled().Should().BeTrue();
		}
	}

	[TestFixture]
	public class ShiftManagementScopeTests
	{
		private static Shift ShiftWithGroups(params int[] groupIds)
		{
			return new Shift { Groups = groupIds.Select(x => new ShiftGroup { DepartmentGroupId = x }).ToList() };
		}

		[Test]
		public void Department_wide_scope_manages_everything()
		{
			var scope = new ShiftManagementScope { AllGroups = true };

			scope.IsSupervisor.Should().BeTrue();
			scope.CanManageShift(ShiftWithGroups()).Should().BeTrue();
			scope.CanManageShiftGroup(ShiftWithGroups(1), null).Should().BeTrue();
		}

		[Test]
		public void Group_admin_manages_only_their_groups()
		{
			var scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 1 } };

			scope.CanManageGroup(1).Should().BeTrue();
			scope.CanManageGroup(2).Should().BeFalse();
			scope.CanSuperviseShift(ShiftWithGroups(1, 2)).Should().BeTrue();
			scope.CanManageShift(ShiftWithGroups(1, 2)).Should().BeFalse();
			scope.CanManageShift(ShiftWithGroups(1)).Should().BeTrue();
		}

		[Test]
		public void A_slot_with_no_group_needs_the_whole_shift()
		{
			var scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 1 } };

			scope.CanManageShiftGroup(ShiftWithGroups(1, 2), null).Should().BeFalse();
			scope.CanManageShiftGroup(ShiftWithGroups(1), null).Should().BeTrue();
			scope.CanManageShift(ShiftWithGroups()).Should().BeFalse();
		}

		[Test]
		public void No_scope_is_not_a_supervisor()
		{
			var scope = ShiftManagementScope.None();

			scope.IsSupervisor.Should().BeFalse();
			scope.CanSuperviseShift(ShiftWithGroups(1)).Should().BeFalse();
		}
	}
}

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ShiftSignupTradeStateTests
	{
		private static ShiftSignupTrade Trade()
		{
			return new ShiftSignupTrade
			{
				Users = new List<ShiftSignupTradeUser>
				{
					new ShiftSignupTradeUser { UserId = "AAAA-1111", Offered = true },
					new ShiftSignupTradeUser { UserId = "bbbb-2222", Declined = true }
				}
			};
		}

		[Test]
		public void A_pick_waiting_for_a_supervisor_is_pending_for_the_taker_and_not_complete()
		{
			var trade = Trade();
			trade.UserId = "AAAA-1111";
			trade.ApprovalPending = true;

			trade.HasSelection().Should().BeTrue();
			trade.IsTradeComplete().Should().BeFalse();
			trade.GetState("aaaa-1111").Should().Be(ShiftSignupTradeStates.PendingApproval);
		}

		[Test]
		public void A_denied_trade_never_completes()
		{
			var trade = Trade();
			trade.UserId = "AAAA-1111";
			trade.Denied = true;

			trade.IsTradeComplete().Should().BeFalse();
			trade.GetState("aaaa-1111").Should().Be(ShiftSignupTradeStates.Denied);
		}

		[Test]
		public void States_ignore_user_id_case()
		{
			var trade = Trade();

			trade.GetState("aaaa-1111").Should().Be(ShiftSignupTradeStates.Proposed);
			trade.GetState("BBBB-2222").Should().Be(ShiftSignupTradeStates.Declined);

			trade.UserId = "AAAA-1111";
			trade.IsTradeComplete().Should().BeTrue();
			trade.GetState("aaaa-1111").Should().Be(ShiftSignupTradeStates.Accepted);
			trade.GetState("cccc-3333").Should().Be(ShiftSignupTradeStates.Filled);
		}
	}
}
