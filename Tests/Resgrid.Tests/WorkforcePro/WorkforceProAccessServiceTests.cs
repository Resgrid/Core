using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.WorkforcePro
{
	/// <summary>
	/// The Workforce Pro gate (shifts plan §7; owner 2026-11-26) fails closed: the fresh Workforce.Pro flag, billing configured, a live
	/// PlanAddonTypes.WorkforcePro window with no entitlement cache, packs × 25 seats, and the members holding a licensed seat in enable order.
	/// A renewal that failed keeps it working for the seven-day dunning grace (owner 2026-11-26, the LMS's lapse rule 13); a cancelled one ends
	/// at its paid-through date and a refunded one at once.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class WorkforceProAccessServiceTests
	{
		private const int DepartmentId = 52;
		private Mock<IFeatureToggleService> _flags;
		private Mock<ISubscriptionsService> _billing;
		private Mock<IWorkforceProSeatsRepository> _seats;
		private Mock<IDepartmentsService> _departments;
		private Mock<IWorkforceProAddonStatesRepository> _states;
		private WorkforceProAddonState _state;
		private int _stateWrites;
		private PaymentAddon _payment;
		private List<WorkforceProSeat> _seatRows;
		private HashSet<string> _active;
		private string _billingUrl, _billingKey;

		[SetUp]
		public void SetUp()
		{
			_billingUrl = Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl;
			_billingKey = Resgrid.Config.ApiConfig.BackendInternalApikey;
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
			Resgrid.Config.ApiConfig.BackendInternalApikey = "unit-test-only";

			_flags = new Mock<IFeatureToggleService>();
			_billing = new Mock<ISubscriptionsService>(MockBehavior.Strict);
			_seats = new Mock<IWorkforceProSeatsRepository>();
			_departments = new Mock<IDepartmentsService>();
			SetFlag(true);
			_payment = new PaymentAddon
			{
				DepartmentId = DepartmentId, PlanAddonId = "wp-pack", TransactionId = "paid-invoice", EffectiveOn = DateTime.UtcNow.AddDays(-1),
				EndingOn = DateTime.UtcNow.AddDays(20), Quantity = 1
			};
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.WorkforcePro, false))
				.ReturnsAsync(new List<PlanAddon> { new PlanAddon { PlanAddonId = "wp-pack", AddonType = (int)PlanAddonTypes.WorkforcePro } });
			_billing.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.Is<List<string>>(ids => ids.Count == 1 && ids[0] == "wp-pack")))
				.ReturnsAsync(() => new List<PaymentAddon> { _payment });
			_seatRows = new List<WorkforceProSeat>();
			_active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_seats.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _seatRows.ToList());
			_departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId)).ReturnsAsync(() => _active);

			_state = null;
			_stateWrites = 0;
			_states = new Mock<IWorkforceProAddonStatesRepository>();
			_states.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => Copy(_state));
			_states.Setup(s => s.SaveForDepartmentAsync(It.IsAny<WorkforceProAddonState>(), It.IsAny<CancellationToken>()))
				.Callback<WorkforceProAddonState, CancellationToken>((s, _) => { _state = Copy(s); _stateWrites++; }).Returns(Task.CompletedTask);
		}

		private static WorkforceProAddonState Copy(WorkforceProAddonState s) => s == null ? null : new WorkforceProAddonState
		{
			WorkforceProAddonStateId = s.WorkforceProAddonStateId, DepartmentId = s.DepartmentId, Packs = s.Packs, PaidThroughUtc = s.PaidThroughUtc,
			RenewalCancelled = s.RenewalCancelled, LastActiveOn = s.LastActiveOn, LapsedSinceUtc = s.LapsedSinceUtc, UpdatedOn = s.UpdatedOn
		};

		/// <summary>The gate last saw an interval with these packs, ending at <paramref name="paidThrough"/>, and no interval runs now.</summary>
		private void LastSeen(DateTime paidThrough, bool cancelled = false, int packs = 2, DateTime? lapsedSince = null)
		{
			_state = new WorkforceProAddonState
			{
				DepartmentId = DepartmentId, Packs = packs, PaidThroughUtc = paidThrough, RenewalCancelled = cancelled, LastActiveOn = paidThrough.AddDays(-1),
				LapsedSinceUtc = lapsedSince, UpdatedOn = paidThrough.AddDays(-1)
			};
			_payment.EndingOn = paidThrough;
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = _billingUrl;
			Resgrid.Config.ApiConfig.BackendInternalApikey = _billingKey;
		}

		private WorkforceProAccessService Service() => new WorkforceProAccessService(_flags.Object, _billing.Object, _seats.Object, _states.Object, _departments.Object);

		private void SetFlag(bool enabled) =>
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.WorkforcePro, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = enabled });

		private void Seat(string userId, int minutesAgo, bool active = true)
		{
			_seatRows.Add(new WorkforceProSeat { WorkforceProSeatId = _seatRows.Count + 1, DepartmentId = DepartmentId, UserId = userId, EnabledOn = DateTime.UtcNow.AddMinutes(-minutesAgo) });
			if (active)
				_active.Add(userId);
		}

		[Test]
		public async Task The_flag_off_is_not_offered_and_never_asks_billing()
		{
			SetFlag(false);
			var access = await Service().GetAccessAsync(DepartmentId);

			access.Offered.Should().BeFalse();
			access.Level.Should().Be(WorkforceProAccessLevels.None);
			access.Invoking(a => a.EnsureReadable()).Should().Throw<WorkforceProAccessException>().Which.Code.Should().Be(WorkforceProCodes.Unavailable);
			_billing.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_flag_that_cannot_be_read_is_off()
		{
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.WorkforcePro, DepartmentId)).ThrowsAsync(new InvalidOperationException("flags down"));
			(await Service().IsOfferedAsync(DepartmentId)).Should().BeFalse();
			(await Service().GetAccessAsync(DepartmentId)).Offered.Should().BeFalse();
		}

		[Test]
		public async Task A_running_interval_licenses_packs_times_25_seats_in_enable_order()
		{
			_payment.Quantity = 1;
			for (var i = 0; i < 27; i++)
				Seat("member-" + i.ToString("00"), 1000 - i);

			var access = await Service().GetAccessAsync(DepartmentId);

			access.Licensed.Should().BeTrue();
			access.Level.Should().Be(WorkforceProAccessLevels.Full);
			access.Packs.Should().Be(1);
			access.PurchasedSeats.Should().Be(25);
			access.SeatHolderUserIds.Should().HaveCount(25);
			access.HoldsSeat("member-00").Should().BeTrue("the earliest seats are licensed first");
			access.HoldsSeat("MEMBER-24").Should().BeTrue("user ids compare case-insensitively");
			access.HoldsSeat("member-25").Should().BeFalse("beyond the purchased seats");
			access.Invoking(a => a.EnsureWritableFor("member-25")).Should().Throw<WorkforceProAccessException>().Which.Code.Should().Be(WorkforceProCodes.SeatRequired);
			access.Invoking(a => a.EnsureWritableFor("member-03")).Should().NotThrow();
		}

		[Test]
		public async Task A_seat_of_a_member_who_is_no_longer_active_is_dormant()
		{
			Seat("left-member", 50, active: false);
			Seat("member", 10);

			var access = await Service().GetAccessAsync(DepartmentId);

			access.HoldsSeat("left-member").Should().BeFalse();
			access.HoldsSeat("member").Should().BeTrue();
		}

		[TestCase("SYSTEM")]
		[TestCase("expired")]
		[TestCase("forever")]
		[TestCase("future")]
		public async Task Synthetic_expired_free_forever_and_future_payments_are_read_only(string kind)
		{
			if (kind == "SYSTEM") _payment.TransactionId = "SYSTEM";
			if (kind == "expired") _payment.EndingOn = DateTime.UtcNow.AddMinutes(-1);
			if (kind == "forever") _payment.EndingOn = DateTime.MaxValue;
			if (kind == "future") _payment.EffectiveOn = DateTime.UtcNow.AddDays(1);
			Seat("member", 5);

			var access = await Service().GetAccessAsync(DepartmentId);

			access.Offered.Should().BeTrue();
			access.Licensed.Should().BeFalse();
			access.IsReadOnly.Should().BeTrue();
			access.HoldsSeat("member").Should().BeFalse("a seat licenses nothing without a running add-on");
			access.Invoking(a => a.EnsureReadable()).Should().NotThrow();
			access.Invoking(a => a.EnsureWritable()).Should().Throw<WorkforceProAccessException>().Which.Code.Should().Be(WorkforceProCodes.Required);
		}

		[Test]
		public async Task Unconfigured_billing_never_grants_the_add_on_self_hosted()
		{
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = null;
			var access = await Service().GetAccessAsync(DepartmentId);

			access.Offered.Should().BeTrue();
			access.State.Should().Be(WorkforceProSubscriptionStates.None);
			access.Licensed.Should().BeFalse();
			(await Service().HasActiveAddonAsync(DepartmentId)).Should().BeFalse();
			_billing.VerifyNoOtherCalls();
		}

		[Test]
		public async Task No_catalog_row_or_a_billing_failure_fails_closed_to_read_only()
		{
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.WorkforcePro, false)).ReturnsAsync(new List<PlanAddon>());
			(await Service().GetAccessAsync(DepartmentId)).State.Should().Be(WorkforceProSubscriptionStates.Unknown);

			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.WorkforcePro, false)).ThrowsAsync(new InvalidOperationException("billing down"));
			var access = await Service().GetAccessAsync(DepartmentId);
			access.State.Should().Be(WorkforceProSubscriptionStates.Unknown);
			access.Licensed.Should().BeFalse();
			access.CanRead.Should().BeTrue("billing trouble never hides records, it only stops changes");
		}

		[Test]
		public async Task A_seat_read_failure_licenses_nobody_but_keeps_the_add_on()
		{
			_seats.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("db down"));
			var access = await Service().GetAccessAsync(DepartmentId);

			access.Licensed.Should().BeTrue();
			access.SeatHolderUserIds.Should().BeEmpty();
		}

		[Test]
		public async Task One_instance_remembers_an_evaluation_until_invalidated_and_a_new_instance_asks_again()
		{
			var service = Service();
			(await service.GetAccessAsync(DepartmentId)).Licensed.Should().BeTrue();

			// The renewal is cancelled and the interval ended: the same request keeps its answer, the next request sees it.
			_payment.IsCancelled = true;
			_state.RenewalCancelled = true;
			_payment.EndingOn = DateTime.UtcNow.AddMinutes(-1);
			_state.PaidThroughUtc = _payment.EndingOn;
			(await service.GetAccessAsync(DepartmentId)).Licensed.Should().BeTrue();
			service.Invalidate(DepartmentId);
			(await service.GetAccessAsync(DepartmentId)).Licensed.Should().BeFalse();
			(await Service().GetAccessAsync(DepartmentId)).Licensed.Should().BeFalse("no entitlement cache across requests");
		}

		[Test]
		public async Task The_pack_quantity_of_the_interval_sets_the_seats()
		{
			_payment.Quantity = 3;
			var access = await Service().GetAccessAsync(DepartmentId);
			access.Packs.Should().Be(3);
			access.PurchasedSeats.Should().Be(75);

			_payment.Quantity = 0;
			(await Service().GetAccessAsync(DepartmentId)).Packs.Should().Be(1, "a row without a quantity counts as the floor");
		}

		[Test]
		public async Task A_running_interval_is_recorded_once_with_its_packs_and_paid_through_date()
		{
			_payment.Quantity = 3;
			(await Service().GetAccessAsync(DepartmentId)).State.Should().Be(WorkforceProSubscriptionStates.Active);

			_state.Should().NotBeNull();
			_state.Packs.Should().Be(3);
			_state.PaidThroughUtc.Should().Be(_payment.EndingOn);
			_state.RenewalCancelled.Should().BeFalse();
			_state.LapsedSinceUtc.Should().BeNull();

			(await Service().GetAccessAsync(DepartmentId)).Licensed.Should().BeTrue();
			_stateWrites.Should().Be(1, "the state is written only when something changed");
		}

		[Test]
		public async Task A_failed_renewal_keeps_working_with_the_last_packs_for_seven_days()
		{
			Resgrid.Config.WorkforceProAddonConfig.DunningGraceDays.Should().Be(7);
			var paidThrough = DateTime.UtcNow.AddDays(-2);
			LastSeen(paidThrough, packs: 2);
			for (var i = 0; i < 30; i++)
				Seat("member-" + i.ToString("00"), 1000 - i);

			var access = await Service().GetAccessAsync(DepartmentId);

			access.State.Should().Be(WorkforceProSubscriptionStates.DunningGrace);
			access.InDunningGrace.Should().BeTrue();
			access.Licensed.Should().BeTrue("a failed renewal keeps Workforce Pro working while the payment is retried");
			access.Level.Should().Be(WorkforceProAccessLevels.Full);
			access.Packs.Should().Be(2);
			access.PurchasedSeats.Should().Be(50);
			access.SeatHolderUserIds.Should().HaveCount(30);
			access.PaidThroughUtc.Should().Be(paidThrough);
			access.GraceEndsUtc.Should().Be(paidThrough.AddDays(7));
			access.Invoking(a => a.EnsureWritableFor("member-29")).Should().NotThrow();
			(await Service().HasActiveAddonAsync(DepartmentId)).Should().BeTrue("the dashboards count the add-on as held during the grace");
			_state.LapsedSinceUtc.Should().BeNull("the grace is not an end");
		}

		[Test]
		public async Task A_failed_renewal_ends_when_the_grace_runs_out_and_never_gets_it_again()
		{
			var paidThrough = DateTime.UtcNow.AddDays(-7).AddMinutes(-1);
			LastSeen(paidThrough);
			Seat("member", 5);

			var access = await Service().GetAccessAsync(DepartmentId);

			access.State.Should().Be(WorkforceProSubscriptionStates.None);
			access.IsReadOnly.Should().BeTrue();
			access.HoldsSeat("member").Should().BeFalse();
			access.GraceEndsUtc.Should().BeNull();
			access.Invoking(a => a.EnsureWritable()).Should().Throw<WorkforceProAccessException>().Which.Code.Should().Be(WorkforceProCodes.Required);
			_state.LapsedSinceUtc.Should().Be(paidThrough.AddDays(7), "the end is recorded at the end of the grace");
			(await Service().HasActiveAddonAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task A_cancelled_renewal_ends_at_its_paid_through_date_with_no_grace()
		{
			var paidThrough = DateTime.UtcNow.AddMinutes(-5);
			LastSeen(paidThrough, cancelled: true);

			var access = await Service().GetAccessAsync(DepartmentId);

			access.State.Should().Be(WorkforceProSubscriptionStates.None);
			access.Licensed.Should().BeFalse();
			_state.LapsedSinceUtc.Should().Be(paidThrough);
		}

		[Test]
		public async Task A_refunded_interval_ends_at_once_and_its_old_paid_through_date_grants_no_grace_later()
		{
			// The gate saw a running interval ending in 20 days; then it disappeared (a refund or chargeback).
			(await Service().GetAccessAsync(DepartmentId)).Licensed.Should().BeTrue();
			_billing.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>())).ReturnsAsync(new List<PaymentAddon>());

			(await Service().GetAccessAsync(DepartmentId)).State.Should().Be(WorkforceProSubscriptionStates.None);
			_state.LapsedSinceUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

			// Later, past the old paid-through date: the recorded end wins, so it is not mistaken for a failed renewal.
			_state.PaidThroughUtc = DateTime.UtcNow.AddDays(-1);
			(await Service().GetAccessAsync(DepartmentId)).State.Should().Be(WorkforceProSubscriptionStates.None);
		}

		[Test]
		public async Task A_renewal_seen_again_clears_the_recorded_end()
		{
			LastSeen(DateTime.UtcNow.AddDays(-30), lapsedSince: DateTime.UtcNow.AddDays(-23));
			_payment.EndingOn = DateTime.UtcNow.AddDays(29);

			var access = await Service().GetAccessAsync(DepartmentId);

			access.State.Should().Be(WorkforceProSubscriptionStates.Active);
			_state.LapsedSinceUtc.Should().BeNull();
			_state.PaidThroughUtc.Should().Be(_payment.EndingOn);
		}

		[Test]
		public async Task A_state_store_failure_never_changes_a_running_answer_and_grants_no_grace()
		{
			_states.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("db down"));
			_states.Setup(s => s.SaveForDepartmentAsync(It.IsAny<WorkforceProAddonState>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
			(await Service().GetAccessAsync(DepartmentId)).State.Should().Be(WorkforceProSubscriptionStates.Active);

			_payment.EndingOn = DateTime.UtcNow.AddDays(-1);
			(await Service().GetAccessAsync(DepartmentId)).State.Should().Be(WorkforceProSubscriptionStates.None, "without the stored state an ended interval has no grace");
		}

		[Test]
		public async Task A_grace_of_zero_days_ends_a_failed_renewal_at_its_paid_through_date()
		{
			var grace = Resgrid.Config.WorkforceProAddonConfig.DunningGraceDays;
			try
			{
				Resgrid.Config.WorkforceProAddonConfig.DunningGraceDays = 0;
				LastSeen(DateTime.UtcNow.AddMinutes(-1));
				(await Service().GetAccessAsync(DepartmentId)).State.Should().Be(WorkforceProSubscriptionStates.None);
			}
			finally
			{
				Resgrid.Config.WorkforceProAddonConfig.DunningGraceDays = grace;
			}
		}

		[Test]
		public async Task Self_hosted_never_reads_the_stored_state()
		{
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = null;
			LastSeen(DateTime.UtcNow.AddDays(-1));
			(await Service().GetAccessAsync(DepartmentId)).State.Should().Be(WorkforceProSubscriptionStates.None);
			_states.Verify(s => s.GetForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}
	}
}
