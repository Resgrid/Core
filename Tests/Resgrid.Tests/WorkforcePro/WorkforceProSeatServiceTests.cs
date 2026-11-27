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
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Wellness;

namespace Resgrid.Tests.WorkforcePro
{
	/// <summary>
	/// Workforce Pro seats (§7.2): department administrators switch Workforce Pro on per member within packs × 25; enabling past the purchased
	/// seats is refused with the packs to add; only active members hold a seat; switching a seat off is allowed even while lapsed and touches
	/// nothing but the seat row; every change is audited.
	/// </summary>
	[TestFixture]
	public class WorkforceProSeatServiceTests
	{
		private const int DepartmentId = 52;
		private const string Admin = "admin-user";
		private const string Member = "member-user";
		private const string Other = "other-user";
		private const string Hidden = "hidden-user";

		private FakeWorkforcePro _access;
		private Mock<IWorkforceProSeatsRepository> _seats;
		private Mock<IDepartmentsService> _departments;
		private Mock<IEventAggregator> _events;
		private List<WorkforceProSeat> _rows;
		private List<AuditEvent> _audits;

		[SetUp]
		public void SetUp()
		{
			_access = new FakeWorkforcePro { Packs = 1 };
			_seats = new Mock<IWorkforceProSeatsRepository>(MockBehavior.Strict);
			_departments = new Mock<IDepartmentsService>();
			_events = new Mock<IEventAggregator>();
			_rows = new List<WorkforceProSeat>();
			_audits = new List<AuditEvent>();

			_seats.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _rows.ToList());
			_seats.Setup(s => s.AddAsync(It.IsAny<WorkforceProSeat>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkforceProSeat seat, CancellationToken _) => { seat.WorkforceProSeatId = _rows.Count + 1; _rows.Add(seat); return true; });
			_seats.Setup(s => s.RemoveAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, IEnumerable<string> ids, CancellationToken _) => _rows.RemoveAll(r => ids.Contains(r.UserId, StringComparer.OrdinalIgnoreCase)));

			var members = new Dictionary<string, DepartmentMember>(StringComparer.OrdinalIgnoreCase)
			{
				[Admin] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Admin, IsAdmin = true },
				[Member] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Member },
				[Other] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Other },
				[Hidden] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Hidden, IsHidden = true }
			};
			for (var i = 0; i < 30; i++)
				members["filler-" + i] = new DepartmentMember { DepartmentId = DepartmentId, UserId = "filler-" + i };
			_departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int _, bool _) => userId != null && members.TryGetValue(userId, out var m) ? m : null);
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			_departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(() => new HashSet<string>(members.Values.Where(m => m.IsHidden != true).Select(m => m.UserId), StringComparer.OrdinalIgnoreCase));
			_events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => _audits.Add(e));
		}

		private WorkforceProSeatService Service() => new WorkforceProSeatService(_seats.Object, _access, _departments.Object, _events.Object);

		private void Fill(int count)
		{
			for (var i = 0; i < count; i++)
				_rows.Add(new WorkforceProSeat { WorkforceProSeatId = _rows.Count + 1, DepartmentId = DepartmentId, UserId = "filler-" + i, EnabledOn = DateTime.UtcNow.AddMinutes(1 + i) });
		}

		[Test]
		public async Task An_admin_switches_a_member_on_within_the_purchased_seats_and_it_is_audited()
		{
			var result = await Service().SetSeatAsync(DepartmentId, Member, true, Admin);

			result.Outcome.Should().Be(WorkforceProSeatOutcomes.Enabled);
			result.Summary.EnabledSeats.Should().Be(1);
			result.Summary.PurchasedSeats.Should().Be(25);
			result.Summary.AvailableSeats.Should().Be(24);
			_rows.Should().ContainSingle(r => r.UserId == Member && r.EnabledByUserId == Admin);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.WorkforceProSeatsChanged && a.UserId == Admin && a.After.Contains("true"));
		}

		[Test]
		public async Task Enabling_past_the_purchased_seats_is_refused_with_the_packs_to_add()
		{
			Fill(25);
			var result = await Service().SetSeatAsync(DepartmentId, Member, true, Admin);

			result.Outcome.Should().Be(WorkforceProSeatOutcomes.SeatsFull);
			result.PacksNeeded.Should().Be(1);
			_rows.Should().NotContain(r => r.UserId == Member);
			_audits.Should().BeEmpty();
		}

		[Test]
		public async Task Only_an_active_member_can_hold_a_seat()
		{
			(await Service().SetSeatAsync(DepartmentId, Hidden, true, Admin)).Outcome.Should().Be(WorkforceProSeatOutcomes.NotActiveMember);
			(await Service().SetSeatAsync(DepartmentId, "stranger", true, Admin)).Outcome.Should().Be(WorkforceProSeatOutcomes.NotActiveMember);
			_rows.Should().BeEmpty();
		}

		[Test]
		public async Task Only_a_department_administrator_switches_seats()
		{
			(await Service().SetSeatAsync(DepartmentId, Other, true, Member)).Outcome.Should().Be(WorkforceProSeatOutcomes.NotAllowed);
			(await Service().SetSeatAsync(DepartmentId, Other, true, "owner-not-a-member")).Outcome.Should().Be(WorkforceProSeatOutcomes.NotAllowed);
			_rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_failed_renewal_inside_its_dunning_grace_keeps_the_packs_and_seats_can_still_be_switched_on()
		{
			_access.FailRenewal();

			var result = await Service().SetSeatAsync(DepartmentId, Member, true, Admin);

			result.Outcome.Should().Be(WorkforceProSeatOutcomes.Enabled);
			result.Summary.State.Should().Be(WorkforceProSubscriptionStates.DunningGrace);
			result.Summary.Licensed.Should().BeTrue();
			result.Summary.InDunningGrace.Should().BeTrue();
			result.Summary.GraceEndsUtc.Should().NotBeNull();
			result.Summary.PurchasedSeats.Should().Be(25, "the packs last paid for stay in force during the grace");
			result.Summary.LicensedUserIds.Should().Contain(Member);
		}

		[Test]
		public async Task Without_a_running_add_on_nobody_is_switched_on_but_a_seat_can_still_be_switched_off()
		{
			_rows.Add(new WorkforceProSeat { WorkforceProSeatId = 1, DepartmentId = DepartmentId, UserId = Other, EnabledOn = DateTime.UtcNow.AddDays(-3) });
			_access.Lapse();

			(await Service().SetSeatAsync(DepartmentId, Member, true, Admin)).Outcome.Should().Be(WorkforceProSeatOutcomes.AddonInactive);
			var off = await Service().SetSeatAsync(DepartmentId, Other, false, Admin);

			off.Outcome.Should().Be(WorkforceProSeatOutcomes.Disabled);
			_rows.Should().BeEmpty();
			// Only the seat row goes; nothing else is touched (the strict repository would throw on any other call).
			_seats.Verify(s => s.RemoveAsync(DepartmentId, It.Is<IEnumerable<string>>(ids => ids.Single() == Other), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Workforce_Pro_switched_off_offers_no_seat_change()
		{
			_access.SwitchOff();
			(await Service().SetSeatAsync(DepartmentId, Member, true, Admin)).Outcome.Should().Be(WorkforceProSeatOutcomes.NotOffered);
			(await Service().GetSummaryAsync(DepartmentId)).Offered.Should().BeFalse();
			_seats.Verify(s => s.GetForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Switching_twice_is_unchanged_and_the_summary_counts_licensed_members_in_enable_order()
		{
			await Service().SetSeatAsync(DepartmentId, Member, true, Admin);
			(await Service().SetSeatAsync(DepartmentId, Member, true, Admin)).Outcome.Should().Be(WorkforceProSeatOutcomes.Unchanged);
			(await Service().SetSeatAsync(DepartmentId, Other, false, Admin)).Outcome.Should().Be(WorkforceProSeatOutcomes.Unchanged);

			Fill(26);
			var summary = await Service().GetSummaryAsync(DepartmentId);
			summary.EnabledSeats.Should().Be(27);
			summary.LicensedUserIds.Should().HaveCount(25);
			summary.LicensedUserIds.Should().Contain(Member, "the member was switched on first");
			summary.MinimumPacks.Should().Be(2, "a pack decrease never strands an enabled member");
			(await Service().GetMinimumPacksAsync(DepartmentId)).Should().Be(2);
		}
	}
}
