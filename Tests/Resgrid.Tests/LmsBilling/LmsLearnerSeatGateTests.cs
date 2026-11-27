using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.LmsBilling
{
	/// <summary>
	/// Round 10 A7 (owner decision 2026-11-26, close the LMS seat gap): who holds the LMS seat a learner action on department LMS content needs.
	/// While the add-on works, the licensed seats (enabled first, within the purchased packs); a sponsored department's members within what the
	/// sponsor's packs leave it; while the add-on is lapsed, the members who held a seat at the packs last paid for (lapse rule 1: nothing is
	/// removed, so the lapse windows keep working for them); every counted seat when billing cannot say or a sponsorship stopped paying. Only
	/// active members, the flag first, and it fails closed. Which actions ask is <see cref="LmsLearnerSeats"/>.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LmsLearnerSeatGateTests
	{
		private const int DepartmentId = 61;

		private Mock<IFeatureToggleService> _flags;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ISubscriptionsService> _billing;
		private Mock<ILmsAddonStatesRepository> _states;
		private Mock<ILmsMemberSeatsRepository> _seats;
		private Mock<IDepartmentsService> _departments;
		private Mock<ILmsSponsorshipService> _sponsorships;
		private List<PaymentAddon> _payments;
		private LmsAddonState _state;
		private List<LmsMemberSeat> _seatRows;
		private HashSet<string> _active;
		private LmsSponsorshipGrant _grant;
		private bool _flagOn;
		private LmsAccessService _access;
		private string _billingUrl, _billingKey;
		private int _packSize;

		[SetUp]
		public void SetUp()
		{
			_billingUrl = SystemBehaviorConfig.BillingApiBaseUrl;
			_billingKey = ApiConfig.BackendInternalApikey;
			_packSize = LmsAddonConfig.PackSize;
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
			ApiConfig.BackendInternalApikey = "unit-test-only";
			// Small packs keep the licensing-order cases readable.
			LmsAddonConfig.PackSize = 2;

			_flagOn = true;
			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.TrainingLms, It.IsAny<int>())).ReturnsAsync(() => new FeatureFlagEvaluation { IsEnabled = _flagOn });
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings());

			_payments = new List<PaymentAddon>();
			_state = null;
			_billing = new Mock<ISubscriptionsService>();
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.Lms, false))
				.ReturnsAsync(new List<PlanAddon> { new PlanAddon { PlanAddonId = LmsAddonConfig.PlanAddonId, AddonType = (int)PlanAddonTypes.Lms } });
			_billing.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(It.IsAny<int>(), It.IsAny<List<string>>())).ReturnsAsync(() => _payments);

			_states = new Mock<ILmsAddonStatesRepository>();
			_states.Setup(s => s.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(() => _state);
			_states.Setup(s => s.SaveForDepartmentAsync(It.IsAny<LmsAddonState>(), It.IsAny<CancellationToken>()))
				.Callback<LmsAddonState, CancellationToken>((s, _) => _state = s).Returns(Task.CompletedTask);
			var lapses = new Mock<ILmsAddonLapsesRepository>();
			lapses.Setup(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_seatRows = new List<LmsMemberSeat>();
			_seats = new Mock<ILmsMemberSeatsRepository>();
			_seats.Setup(s => s.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int department) => _seatRows.FindAll(r => r.DepartmentId == department));
			_seats.Setup(s => s.RemoveAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int department, IEnumerable<string> users, CancellationToken _) => _seatRows.RemoveAll(r => r.DepartmentId == department && users.Contains(r.UserId, StringComparer.OrdinalIgnoreCase)));
			_active = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b", "c", "d" };
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetActiveMemberUserIdsAsync(It.IsAny<int>())).ReturnsAsync(() => _active);

			_grant = null;
			_sponsorships = new Mock<ILmsSponsorshipService>();
			_sponsorships.Setup(s => s.GetGrantAsync(It.IsAny<int>())).ReturnsAsync(() => _grant);

			_access = new LmsAccessService(_flags.Object, _settings.Object, _billing.Object, _states.Object, lapses.Object, _seats.Object, _departments.Object,
				Mock.Of<Resgrid.Model.Providers.IEventAggregator>(), new Lazy<ILmsSponsorshipService>(() => _sponsorships.Object));
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.BillingApiBaseUrl = _billingUrl;
			ApiConfig.BackendInternalApikey = _billingKey;
			LmsAddonConfig.PackSize = _packSize;
		}

		private void Running(long packs) => _payments.Add(new PaymentAddon
		{
			DepartmentId = DepartmentId, PlanAddonId = LmsAddonConfig.PlanAddonId, TransactionId = "in_paid", Quantity = packs,
			PurchaseOn = DateTime.UtcNow.AddDays(-3), EffectiveOn = DateTime.UtcNow.AddDays(-3), EndingOn = DateTime.UtcNow.AddDays(27)
		});

		private void Lapsed(int packs) => _state = new LmsAddonState
		{
			DepartmentId = DepartmentId, Packs = packs, PaidThroughUtc = DateTime.UtcNow.AddDays(-40), LastActiveOn = DateTime.UtcNow.AddDays(-41),
			LapsedSinceUtc = DateTime.UtcNow.AddDays(-33), UpdatedOn = DateTime.UtcNow.AddDays(-33)
		};

		/// <summary>Seats enabled in the order given (the licensing order).</summary>
		private void Seat(params string[] userIds)
		{
			foreach (var userId in userIds)
				_seatRows.Add(new LmsMemberSeat { LmsMemberSeatId = _seatRows.Count + 1, DepartmentId = DepartmentId, UserId = userId, EnabledOn = DateTime.UtcNow.AddMinutes(-100 + _seatRows.Count) });
		}

		private LmsGateService Gate(ILmsAccessService access = null) => new LmsGateService(access ?? _access, Mock.Of<ILmsDepartmentStatesRepository>(), _departments.Object,
			Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IPermissionsService>(), Mock.Of<IContentAudienceService>(), _sponsorships.Object);

		[Test]
		public async Task With_a_working_add_on_the_licensed_seats_hold_it_in_the_order_they_were_enabled()
		{
			Running(1);
			Seat("a", "b", "c");

			var holders = await _access.GetLearnerSeatHoldersAsync(DepartmentId);

			holders.Should().BeEquivalentTo(new[] { "a", "b" }, "one pack of two seats: the third enabled member is not licensed");
			(await _access.CanBeTrainedAsync(DepartmentId, "c")).Should().BeFalse("the same licensing order as the seat counter");
			(await Gate().HoldsLearnerSeatAsync(DepartmentId, "A")).Should().BeTrue("user ids compare case-insensitively");
			(await Gate().HoldsLearnerSeatAsync(DepartmentId, "c")).Should().BeFalse();
			(await Gate().HoldsLearnerSeatAsync(DepartmentId, "d")).Should().BeFalse("no seat enabled");
		}

		[Test]
		public async Task Only_active_members_hold_a_seat()
		{
			Running(2);
			Seat("a", "gone");

			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEquivalentTo(new[] { "a" }, "a deleted, disabled or hidden member's seat never counts");
		}

		[Test]
		public async Task A_removed_seat_loses_learner_access_at_once()
		{
			Running(2);
			Seat("a", "b");
			(await Gate().HoldsLearnerSeatAsync(DepartmentId, "b")).Should().BeTrue();

			_seatRows.RemoveAll(r => r.UserId == "b");

			(await Gate().HoldsLearnerSeatAsync(DepartmentId, "b")).Should().BeFalse("no entitlement cache: the next request sees the seat gone");
		}

		[Test]
		public async Task During_a_lapse_the_members_who_held_a_seat_keep_it_at_the_packs_last_paid_for()
		{
			Lapsed(packs: 1);
			Seat("a", "b", "c");

			(await _access.GetSubscriptionAsync(DepartmentId)).State.Should().Be(LmsSubscriptionStates.Lapsed);
			var holders = await _access.GetLearnerSeatHoldersAsync(DepartmentId);

			holders.Should().BeEquivalentTo(new[] { "a", "b" }, "lapse rule 1: seats stay enabled, so assigned work stays takeable (rule 4) for who held one");
			(await _access.CanBeTrainedAsync(DepartmentId, "a")).Should().BeFalse("nobody is licensed for new LMS work while the add-on is lapsed");
		}

		[Test]
		public async Task When_billing_cannot_say_every_counted_seat_holds_it_and_the_lapse_windows_decide()
		{
			_billing.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(It.IsAny<int>(), It.IsAny<List<string>>())).ReturnsAsync((List<PaymentAddon>)null);
			Seat("a", "b", "c");

			(await _access.GetSubscriptionAsync(DepartmentId)).State.Should().Be(LmsSubscriptionStates.Unknown);
			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEquivalentTo(new[] { "a", "b", "c" });
		}

		[Test]
		public async Task A_sponsored_department_is_seated_in_what_the_sponsors_packs_leave_it()
		{
			Seat("a", "b", "c");
			_grant = new LmsSponsorshipGrant { Sponsorship = new LmsSponsorship { LmsSponsorshipId = 4, DepartmentId = DepartmentId, SponsorDepartmentId = 90 }, Capacity = 1 };

			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEquivalentTo(new[] { "a" }, "T5 item 2a: the sponsorship's capacity, enabled first");

			_grant = null;
			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEquivalentTo(new[] { "a", "b", "c" },
				"a sponsorship that stopped paying: every counted seat, the sponsored copies' own lapse windows deciding what still runs");
		}

		[Test]
		public async Task A_department_on_its_own_add_on_uses_its_own_packs_not_a_sponsorship()
		{
			Running(1);
			Seat("a", "b", "c");
			_grant = new LmsSponsorshipGrant { Sponsorship = new LmsSponsorship { LmsSponsorshipId = 4, DepartmentId = DepartmentId, SponsorDepartmentId = 90 }, Capacity = 10 };

			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEquivalentTo(new[] { "a", "b" });
			_sponsorships.Verify(s => s.GetGrantAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Nobody_holds_a_seat_with_the_flag_off_and_billing_is_not_asked()
		{
			Running(5);
			Seat("a");
			_flagOn = false;

			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEmpty();
			(await Gate().HoldsLearnerSeatAsync(DepartmentId, "a")).Should().BeFalse();
			_billing.Verify(s => s.GetCurrentPaymentAddonsForDepartmentAsync(It.IsAny<int>(), It.IsAny<List<string>>()), Times.Never);
		}

		[Test]
		public async Task Without_any_seat_rows_billing_is_not_asked()
		{
			Running(5);

			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEmpty();
			_billing.Verify(s => s.GetCurrentPaymentAddonsForDepartmentAsync(It.IsAny<int>(), It.IsAny<List<string>>()), Times.Never);
		}

		[Test]
		public async Task A_failure_reading_seats_fails_closed()
		{
			Running(5);
			_seats.Setup(s => s.GetForDepartmentAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException("database down"));

			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEmpty();

			var throwing = new Mock<ILmsAccessService>();
			throwing.Setup(a => a.GetLearnerSeatHoldersAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("boom"));
			(await Gate(throwing.Object).HoldsLearnerSeatAsync(DepartmentId, "a")).Should().BeFalse();
			(await Gate().HoldsLearnerSeatAsync(DepartmentId, null)).Should().BeFalse();
			(await Gate().HoldsLearnerSeatAsync(0, "a")).Should().BeFalse();
		}

		[Test]
		public void The_pure_rule_takes_counted_seats_in_licensing_order_up_to_the_capacity()
		{
			var now = DateTime.UtcNow;
			var seats = new List<LmsMemberSeat>
			{
				new LmsMemberSeat { LmsMemberSeatId = 3, UserId = "late", EnabledOn = now },
				new LmsMemberSeat { LmsMemberSeatId = 1, UserId = "first", EnabledOn = now.AddDays(-2) },
				new LmsMemberSeat { LmsMemberSeatId = 2, UserId = "second", EnabledOn = now.AddDays(-1) },
				new LmsMemberSeat { LmsMemberSeatId = 4, UserId = "inactive", EnabledOn = now.AddDays(-3) }
			};
			var active = new HashSet<string>(new[] { "late", "first", "second" }, StringComparer.OrdinalIgnoreCase);

			LmsSeatRules.LearnerSeatHolders(seats, active, 2).Should().BeEquivalentTo(new[] { "first", "second" });
			LmsSeatRules.LearnerSeatHolders(seats, active, null).Should().BeEquivalentTo(new[] { "first", "second", "late" });
			LmsSeatRules.LearnerSeatHolders(seats, active, 0).Should().BeEmpty();
			LmsSeatRules.LearnerSeatHolders(null, active, 5).Should().BeEmpty();
		}

		[Test]
		public void Only_department_content_for_a_member_needs_a_seat()
		{
			LmsLearnerSeats.Needed(false, "member").Should().BeTrue("department courses, sponsored copies and classes");
			LmsLearnerSeats.Needed(true, "member").Should().BeFalse("the Resgrid Academy is free (R-S2)");
			LmsLearnerSeats.Needed(false, LmsExternalLearnerIds.New()).Should().BeFalse("external learners follow the learner portal's own rules");
			LmsLearnerSeats.Needed(false, " ").Should().BeFalse();
			LmsLearnerSeats.Needed(new LmsCourse { PublisherKind = (int)LmsPublisherKinds.System }, "member").Should().BeFalse();
			LmsLearnerSeats.Needed((LmsCourse)null, "member").Should().BeFalse();
		}
	
		[Test]
		public async Task Enabling_past_the_purchased_seats_is_refused_and_a_disabled_seat_loses_learner_access()
		{
			Running(1);
			_departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string user, int department, bool _) => new DepartmentMember { DepartmentId = department, UserId = user });
			_seats.Setup(s => s.AddAsync(It.IsAny<LmsMemberSeat>(), It.IsAny<CancellationToken>())).ReturnsAsync((LmsMemberSeat seat, CancellationToken _) =>
			{
				if (_seatRows.Any(r => r.DepartmentId == seat.DepartmentId && string.Equals(r.UserId, seat.UserId, StringComparison.OrdinalIgnoreCase)))
					return false;
				seat.LmsMemberSeatId = _seatRows.Count + 1;
				_seatRows.Add(seat);
				return true;
			});
			var seats = new LmsSeatService(_seats.Object, _access, _departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<Resgrid.Model.Providers.IEventAggregator>(),
				Mock.Of<ILmsExternalLearnersRepository>(), _sponsorships.Object);

			(await seats.SetSeatAsync(DepartmentId, "a", true, "admin")).Outcome.Should().Be(LmsSeatOutcomes.Enabled);
			(await seats.SetSeatAsync(DepartmentId, "b", true, "admin")).Outcome.Should().Be(LmsSeatOutcomes.Enabled);
			var full = await seats.SetSeatAsync(DepartmentId, "c", true, "admin");
			full.Outcome.Should().Be(LmsSeatOutcomes.SeatsFull, "one pack of two: the third member is refused with an offer to add a pack");
			full.PacksNeeded.Should().Be(1);
			(await _access.GetLearnerSeatHoldersAsync(DepartmentId)).Should().BeEquivalentTo(new[] { "a", "b" });

			(await seats.SetSeatAsync(DepartmentId, "b", false, "admin")).Outcome.Should().Be(LmsSeatOutcomes.Disabled);
			(await Gate().HoldsLearnerSeatAsync(DepartmentId, "b")).Should().BeFalse("a member whose seat was removed loses learner access at once");
			(await Gate().HoldsLearnerSeatAsync(DepartmentId, "a")).Should().BeTrue();
		}
}
}
