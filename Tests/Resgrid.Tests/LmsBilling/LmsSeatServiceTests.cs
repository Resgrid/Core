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

namespace Resgrid.Tests.LmsBilling
{
	/// <summary>
	/// The seat rules (§17.4): purchased seats = packs × 25, only active members hold a seat, enabling beyond the purchased seats is
	/// refused with the packs to add, the group switch stops at the cap, seats are per department, and nothing is enabled while the
	/// LMS is off or the add-on is not working.
	/// </summary>
	[TestFixture]
	public class LmsSeatServiceTests
	{
		private const int DepartmentId = 61;
		private const int GroupId = 7;

		private Mock<ILmsMemberSeatsRepository> _seats;
		private Mock<ILmsAccessService> _access;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IEventAggregator> _events;
		private List<LmsMemberSeat> _rows;
		private HashSet<string> _active;
		private Dictionary<string, DepartmentMember> _members;
		private LmsSubscriptionSnapshot _subscription;
		private LmsSeatService _service;

		[SetUp]
		public void SetUp()
		{
			_rows = new List<LmsMemberSeat>();
			_active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_members = new Dictionary<string, DepartmentMember>(StringComparer.OrdinalIgnoreCase);
			_subscription = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Active, Packs = 1 };

			_seats = new Mock<ILmsMemberSeatsRepository>();
			_seats.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _rows.ToList());
			_seats.Setup(s => s.AddAsync(It.IsAny<LmsMemberSeat>(), It.IsAny<CancellationToken>()))
				.Callback<LmsMemberSeat, CancellationToken>((seat, _) => { seat.LmsMemberSeatId = _rows.Count + 1; _rows.Add(seat); }).ReturnsAsync(true);
			_seats.Setup(s => s.RemoveAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
				.Callback<int, IEnumerable<string>, CancellationToken>((_, ids, _) => _rows.RemoveAll(r => ids.Contains(r.UserId, StringComparer.OrdinalIgnoreCase)))
				.ReturnsAsync(1);

			_access = new Mock<ILmsAccessService>();
			_access.Setup(a => a.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(true);
			_access.Setup(a => a.GetSubscriptionAsync(DepartmentId)).ReturnsAsync(() => _subscription);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId)).ReturnsAsync(() => new HashSet<string>(_active, StringComparer.OrdinalIgnoreCase));
			_departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int _, bool _) => _members.TryGetValue(userId, out var member) ? member : null);
			_departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(() =>
				_members.Keys.Select(id => new PersonName { UserId = id, FirstName = "Name", LastName = id }).ToList());

			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(g => g.GetGroupByIdAsync(GroupId, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = GroupId, DepartmentId = DepartmentId, Name = "Career" });
			_events = new Mock<IEventAggregator>();

			_service = new LmsSeatService(_seats.Object, _access.Object, _departments.Object, _groups.Object, _events.Object);
		}

		private void Member(string userId, bool hidden = false, bool disabled = false, bool deleted = false)
		{
			_members[userId] = new DepartmentMember { DepartmentId = DepartmentId, UserId = userId, IsHidden = hidden, IsDisabled = disabled, IsDeleted = deleted };
			if (!hidden && !disabled && !deleted)
				_active.Add(userId);
		}

		private void Enabled(string userId, int minutesAgo = 60)
		{
			Member(userId);
			_rows.Add(new LmsMemberSeat { LmsMemberSeatId = _rows.Count + 1, DepartmentId = DepartmentId, UserId = userId, EnabledOn = DateTime.UtcNow.AddMinutes(-minutesAgo) });
		}

		private void GroupHas(params string[] userIds) =>
			_groups.Setup(g => g.GetAllMembersForGroupAsync(GroupId)).ReturnsAsync(userIds.Select(id => new DepartmentGroupMember { DepartmentGroupId = GroupId, UserId = id }).ToList());

		[Test]
		public async Task The_counter_shows_enabled_active_members_against_purchased_seats()
		{
			for (var i = 0; i < 23; i++)
				Enabled("m" + i, 100 - i);
			_rows.Add(new LmsMemberSeat { LmsMemberSeatId = 99, DepartmentId = DepartmentId, UserId = "left", EnabledOn = DateTime.UtcNow.AddDays(-9) });

			var summary = await _service.GetSummaryAsync(DepartmentId);

			summary.EnabledSeats.Should().Be(23);
			summary.PurchasedSeats.Should().Be(25, "23 of 25 seats");
			summary.AvailableSeats.Should().Be(2);
			summary.DormantSeats.Should().Be(1, "a member who left keeps the row but not a counted seat");
			summary.LicensedUserIds.Should().HaveCount(23).And.NotContain("left");
			summary.MinimumPacks.Should().Be(1);
		}

		[Test]
		public async Task Enabling_beyond_the_purchased_seats_is_refused_with_an_offer_to_add_a_pack()
		{
			for (var i = 0; i < 25; i++)
				Enabled("m" + i);
			Member("next");

			var result = await _service.SetSeatAsync(DepartmentId, "next", true, "admin");

			result.Outcome.Should().Be(LmsSeatOutcomes.SeatsFull);
			result.PacksNeeded.Should().Be(1);
			_seats.Verify(s => s.AddAsync(It.IsAny<LmsMemberSeat>(), It.IsAny<CancellationToken>()), Times.Never);
			(await _service.CanEnableAsync(DepartmentId)).Outcome.Should().Be(LmsSeatOutcomes.SeatsFull);
		}

		[Test]
		public async Task A_free_seat_is_taken_by_an_active_member_and_audited()
		{
			Member("pat");

			var result = await _service.SetSeatAsync(DepartmentId, "pat", true, "admin");

			result.Outcome.Should().Be(LmsSeatOutcomes.Enabled);
			_rows.Should().ContainSingle(r => r.UserId == "pat" && r.EnabledByUserId == "admin");
			_events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.LmsSeatsChanged && a.DepartmentId == DepartmentId && a.UserId == "admin")), Times.Once);
		}

		[TestCase("hidden")]
		[TestCase("disabled")]
		[TestCase("deleted")]
		[TestCase("stranger")]
		public async Task Only_active_members_can_hold_a_seat(string kind)
		{
			if (kind != "stranger")
				Member("x", hidden: kind == "hidden", disabled: kind == "disabled", deleted: kind == "deleted");

			(await _service.SetSeatAsync(DepartmentId, "x", true, "admin")).Outcome.Should().Be(LmsSeatOutcomes.NotActiveMember);
			_seats.Verify(s => s.AddAsync(It.IsAny<LmsMemberSeat>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_member_who_already_holds_a_seat_is_unchanged_and_disabling_frees_it()
		{
			Enabled("pat");

			(await _service.SetSeatAsync(DepartmentId, "PAT", true, "admin")).Outcome.Should().Be(LmsSeatOutcomes.Unchanged);
			(await _service.SetSeatAsync(DepartmentId, "pat", false, "admin")).Outcome.Should().Be(LmsSeatOutcomes.Disabled);
			_rows.Should().BeEmpty();
			(await _service.SetSeatAsync(DepartmentId, "pat", false, "admin")).Outcome.Should().Be(LmsSeatOutcomes.Unchanged);
		}

		[Test]
		public async Task Nothing_is_enabled_while_the_lms_is_off_or_the_add_on_is_not_working()
		{
			Member("pat");
			_subscription = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Lapsed, Packs = 2 };
			(await _service.SetSeatAsync(DepartmentId, "pat", true, "admin")).Outcome.Should().Be(LmsSeatOutcomes.AddonInactive);
			(await _service.GetSummaryAsync(DepartmentId)).LicensedUserIds.Should().BeEmpty("a lapsed add-on licenses nobody, though seats are kept");

			_subscription = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.DunningGrace, Packs = 1 };
			(await _service.SetSeatAsync(DepartmentId, "pat", true, "admin")).Outcome.Should().Be(LmsSeatOutcomes.Enabled, "the dunning grace keeps the LMS working");

			_access.Setup(a => a.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(false);
			(await _service.SetSeatAsync(DepartmentId, "pat", false, "admin")).Outcome.Should().Be(LmsSeatOutcomes.NotOffered);
		}

		[Test]
		public async Task Bulk_enable_by_group_stops_at_the_purchased_seats_and_skips_inactive_members()
		{
			for (var i = 0; i < 20; i++)
				Enabled("seated" + i);
			var group = new List<string>();
			for (var i = 0; i < 10; i++)
			{
				Member("g" + i.ToString("00"));
				group.Add("g" + i.ToString("00"));
			}
			Member("hiddenOne", hidden: true);
			Member("disabledOne", disabled: true);
			group.Add("hiddenOne");
			group.Add("disabledOne");
			group.Add("seated0");
			GroupHas(group.ToArray());

			var result = await _service.SetSeatsForGroupAsync(DepartmentId, GroupId, true, "admin");

			result.Outcome.Should().Be(LmsSeatOutcomes.SeatsFull);
			result.Changed.Should().Be(5, "five seats were free");
			result.RefusedForSeats.Should().Be(5);
			result.SkippedInactive.Should().Be(2);
			result.AlreadySet.Should().Be(1);
			result.PacksNeeded.Should().Be(1);
			_rows.Count(r => r.UserId.StartsWith("g")).Should().Be(5);
			_rows.Where(r => r.UserId.StartsWith("g")).Select(r => r.UserId).Should().BeEquivalentTo(new[] { "g00", "g01", "g02", "g03", "g04" }, "members are taken in name order");
			_rows.Should().NotContain(r => r.UserId == "hiddenOne" || r.UserId == "disabledOne");
		}

		[Test]
		public async Task Bulk_disable_by_group_frees_every_seat_in_the_group()
		{
			Enabled("a");
			Enabled("b");
			Enabled("outside");
			GroupHas("a", "b", "c");

			var result = await _service.SetSeatsForGroupAsync(DepartmentId, GroupId, false, "admin");

			result.Outcome.Should().Be(LmsSeatOutcomes.Disabled);
			result.Changed.Should().Be(2);
			_rows.Select(r => r.UserId).Should().Equal("outside");
		}

		[Test]
		public async Task A_group_of_another_department_changes_nothing()
		{
			_groups.Setup(g => g.GetGroupByIdAsync(GroupId, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = GroupId, DepartmentId = 999 });
			GroupHas("a");
			Member("a");

			var result = await _service.SetSeatsForGroupAsync(DepartmentId, GroupId, true, "admin");

			result.Changed.Should().Be(0);
			_seats.Verify(s => s.AddAsync(It.IsAny<LmsMemberSeat>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_minimum_packs_seat_every_enabled_member()
		{
			for (var i = 0; i < 51; i++)
				Enabled("m" + i);

			(await _service.GetMinimumPacksAsync(DepartmentId)).Should().Be(3, "51 enabled members need three 25-member packs");
		}
	}
}
