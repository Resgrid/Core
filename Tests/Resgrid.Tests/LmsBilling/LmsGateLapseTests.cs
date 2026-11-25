using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.LmsBilling
{
	/// <summary>
	/// The LMS gate at general availability (§17.3): packs from the running interval, the seat check for being trained, the dunning
	/// grace for a failed renewal (lapse rule 13), lapses recorded without deleting anything (rule 1), reactivation closing the lapse
	/// (rule 12's input) and the self-hosted model (billing unconfigured: no add-on, as for every paid add-on).
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LmsGateLapseTests
	{
		private const int DepartmentId = 52;
		private const int OtherDepartmentId = 53;

		private Mock<IFeatureToggleService> _flags;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ISubscriptionsService> _billing;
		private Mock<ILmsAddonStatesRepository> _states;
		private Mock<ILmsAddonLapsesRepository> _lapses;
		private Mock<ILmsMemberSeatsRepository> _seats;
		private Mock<IDepartmentsService> _departments;
		private List<PaymentAddon> _payments;
		private LmsAddonState _state;
		private List<LmsMemberSeat> _seatRows;
		private HashSet<string> _active;
		private LmsAccessService _gate;
		private string _billingUrl, _billingKey;

		[SetUp]
		public void SetUp()
		{
			_billingUrl = SystemBehaviorConfig.BillingApiBaseUrl;
			_billingKey = ApiConfig.BackendInternalApikey;
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
			ApiConfig.BackendInternalApikey = "unit-test-only";

			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.TrainingLms, It.IsAny<int>())).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
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
			_lapses = new Mock<ILmsAddonLapsesRepository>();
			_lapses.Setup(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_seatRows = new List<LmsMemberSeat>();
			_seats = new Mock<ILmsMemberSeatsRepository>();
			_seats.Setup(s => s.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int department) => _seatRows.FindAll(r => r.DepartmentId == department));
			_active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetActiveMemberUserIdsAsync(It.IsAny<int>())).ReturnsAsync(() => _active);

			_gate = new LmsAccessService(_flags.Object, _settings.Object, _billing.Object, _states.Object, _lapses.Object, _seats.Object, _departments.Object);
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.BillingApiBaseUrl = _billingUrl;
			ApiConfig.BackendInternalApikey = _billingKey;
		}

		private void Running(long packs, bool cancelled = false, int departmentId = DepartmentId) => _payments.Add(new PaymentAddon
		{
			DepartmentId = departmentId, PlanAddonId = LmsAddonConfig.PlanAddonId, TransactionId = "in_paid", Quantity = packs, IsCancelled = cancelled,
			PurchaseOn = DateTime.UtcNow.AddDays(-3), EffectiveOn = DateTime.UtcNow.AddDays(-3), EndingOn = DateTime.UtcNow.AddDays(27)
		});

		private void LastSeen(DateTime paidThrough, bool cancelled = false, int packs = 2, DateTime? lapsedSince = null) => _state = new LmsAddonState
		{
			DepartmentId = DepartmentId, Packs = packs, PaidThroughUtc = paidThrough, RenewalCancelled = cancelled, LastActiveOn = paidThrough.AddDays(-1),
			LapsedSinceUtc = lapsedSince, UpdatedOn = paidThrough.AddDays(-1)
		};

		private void Seat(string userId, int minutesAgo, int departmentId = DepartmentId) =>
			_seatRows.Add(new LmsMemberSeat { LmsMemberSeatId = _seatRows.Count + 1, DepartmentId = departmentId, UserId = userId, EnabledOn = DateTime.UtcNow.AddMinutes(-minutesAgo) });

		[Test]
		public async Task A_running_interval_answers_active_with_its_packs_and_records_them()
		{
			Running(3);

			var subscription = await _gate.GetSubscriptionAsync(DepartmentId);

			subscription.State.Should().Be(LmsSubscriptionStates.Active);
			subscription.Packs.Should().Be(3);
			(await _gate.CanAuthorAndManageAsync(DepartmentId)).Should().BeTrue();
			_state.Should().NotBeNull();
			_state.Packs.Should().Be(3);
			_state.PaidThroughUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(27), TimeSpan.FromMinutes(1));
		}

		[Test]
		public async Task A_running_interval_without_a_quantity_counts_as_the_floor()
		{
			Running(0);

			(await _gate.GetSubscriptionAsync(DepartmentId)).Packs.Should().Be(LmsAddonConfig.MinimumPacks);
		}

		[Test]
		public async Task A_failed_renewal_keeps_the_lms_working_through_the_dunning_grace()
		{
			LastSeen(DateTime.UtcNow.AddDays(-2));

			var subscription = await _gate.GetSubscriptionAsync(DepartmentId);

			subscription.State.Should().Be(LmsSubscriptionStates.DunningGrace, "lapse rule 13: the same grace as other monthly renewals before any lapse rule applies");
			subscription.Packs.Should().Be(2);
			subscription.GraceEndsUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(LmsAddonConfig.DunningGraceDays - 2), TimeSpan.FromMinutes(1));
			(await _gate.CanAuthorAndManageAsync(DepartmentId)).Should().BeTrue();
			_lapses.Verify(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task After_the_dunning_grace_the_add_on_lapses_from_the_end_of_the_grace()
		{
			var paidThrough = DateTime.UtcNow.AddDays(-(LmsAddonConfig.DunningGraceDays + 3));
			LastSeen(paidThrough);
			LmsAddonLapse opened = null;
			_lapses.Setup(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>())).Callback<LmsAddonLapse, CancellationToken>((l, _) => opened = l).ReturnsAsync(true);

			var subscription = await _gate.GetSubscriptionAsync(DepartmentId);

			subscription.State.Should().Be(LmsSubscriptionStates.Lapsed);
			(await _gate.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse("lapse rule 3: authoring and new LMS assignments stop");
			opened.Should().NotBeNull();
			opened.Reason.Should().Be((int)LmsLapseReasons.PaymentFailed);
			opened.StartedOn.Should().Be(paidThrough.AddDays(LmsAddonConfig.DunningGraceDays));
			opened.Packs.Should().Be(2);
			_state.LapsedSinceUtc.Should().Be(opened.StartedOn);
		}

		[Test]
		public async Task A_cancelled_renewal_ends_at_its_paid_through_date_without_grace()
		{
			var paidThrough = DateTime.UtcNow.AddHours(-2);
			LastSeen(paidThrough, cancelled: true);
			LmsAddonLapse opened = null;
			_lapses.Setup(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>())).Callback<LmsAddonLapse, CancellationToken>((l, _) => opened = l).ReturnsAsync(true);

			(await _gate.GetActiveAddonStateAsync(DepartmentId)).Should().BeFalse();
			opened.Reason.Should().Be((int)LmsLapseReasons.RenewalCancelled);
			opened.StartedOn.Should().Be(paidThrough);
		}

		[Test]
		public async Task A_revoked_interval_lapses_at_once()
		{
			LastSeen(DateTime.UtcNow.AddDays(20));
			LmsAddonLapse opened = null;
			_lapses.Setup(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>())).Callback<LmsAddonLapse, CancellationToken>((l, _) => opened = l).ReturnsAsync(true);

			(await _gate.GetActiveAddonStateAsync(DepartmentId)).Should().BeFalse("a refund or chargeback cut the interval short");
			opened.Reason.Should().Be((int)LmsLapseReasons.Revoked);
			opened.StartedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
		}

		[Test]
		public async Task A_lapse_already_recorded_is_answered_from_the_state_without_writing()
		{
			LastSeen(DateTime.UtcNow.AddDays(-30), lapsedSince: DateTime.UtcNow.AddDays(-23));

			var subscription = await _gate.GetSubscriptionAsync(DepartmentId);

			subscription.State.Should().Be(LmsSubscriptionStates.Lapsed);
			subscription.LapsedSinceUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(-23), TimeSpan.FromMinutes(1));
			_lapses.Verify(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>()), Times.Never);
			_states.Verify(s => s.SaveForDepartmentAsync(It.IsAny<LmsAddonState>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_lapse_deletes_nothing_and_seats_stay_enabled()
		{
			Seat("u1", 10);
			LastSeen(DateTime.UtcNow.AddDays(-30), cancelled: true);

			(await _gate.GetActiveAddonStateAsync(DepartmentId)).Should().BeFalse();

			// Lapse rule 1: the gate never removes a seat or anything else when the add-on ends.
			_seats.Verify(s => s.RemoveAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
			_seats.Verify(s => s.DeleteAsync(It.IsAny<LmsMemberSeat>(), It.IsAny<CancellationToken>()), Times.Never);
			_seatRows.Should().ContainSingle();
		}

		[Test]
		public async Task Coming_back_closes_the_open_lapse_and_clears_it_from_the_state()
		{
			LastSeen(DateTime.UtcNow.AddDays(-40), cancelled: true, lapsedSince: DateTime.UtcNow.AddDays(-40));
			Running(2);

			(await _gate.GetSubscriptionAsync(DepartmentId)).State.Should().Be(LmsSubscriptionStates.Active);

			_lapses.Verify(l => l.CloseAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_state.LapsedSinceUtc.Should().BeNull();
			_state.RenewalCancelled.Should().BeFalse();
		}

		[Test]
		public async Task A_department_that_never_held_the_add_on_has_none_and_nothing_is_written()
		{
			var subscription = await _gate.GetSubscriptionAsync(DepartmentId);

			subscription.State.Should().Be(LmsSubscriptionStates.None);
			_states.Verify(s => s.SaveForDepartmentAsync(It.IsAny<LmsAddonState>(), It.IsAny<CancellationToken>()), Times.Never);
			_lapses.Verify(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Self_hosted_installs_follow_the_other_add_ons_and_never_grant_the_lms()
		{
			// SystemBehaviorConfig.BillingApiBaseUrl is documented as never set on an open-source install. Every paid add-on gate
			// (Readiness Pro, Business Ops, Enhanced AI, Incident Back Office, Dedicated Number) answers "no add-on" then, because the
			// generic helpers synthesize a free-forever payment; the LMS follows the same model (§25.4a item 3).
			SystemBehaviorConfig.BillingApiBaseUrl = null;
			Running(5);
			Seat("u1", 5);
			_active.Add("u1");

			(await _gate.GetSubscriptionAsync(DepartmentId)).State.Should().Be(LmsSubscriptionStates.None);
			(await _gate.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse();
			(await _gate.CanBeTrainedAsync(DepartmentId, "u1")).Should().BeFalse();
			(await _gate.CanRunItemAsync(DepartmentId, LmsRunnableOrigins.SystemPublished)).Should().BeTrue("the Academy runs everywhere the flag is on (R-S2)");
			_billing.Verify(s => s.GetCurrentPaymentAddonsForDepartmentAsync(It.IsAny<int>(), It.IsAny<List<string>>()), Times.Never);
		}

		[Test]
		public async Task Only_a_licensed_seat_can_be_trained_and_nobody_needs_one_to_author()
		{
			Running(1);
			_active.UnionWith(new[] { "officer", "trainee", "former" });
			Seat("trainee", 30);
			Seat("FORMER", 40);

			(await _gate.CanAuthorAndManageAsync(DepartmentId)).Should().BeTrue("authoring, teaching, evaluating and reporting need a permission, not a seat");
			(await _gate.CanBeTrainedAsync(DepartmentId, "officer")).Should().BeFalse("not LMS-enabled: the free baseline only");
			(await _gate.CanBeTrainedAsync(DepartmentId, "TRAINEE")).Should().BeTrue("user ids compare case-insensitively");

			_active.Remove("former");
			(await _gate.CanBeTrainedAsync(DepartmentId, "former")).Should().BeFalse("only active members hold a counted seat");
		}

		[Test]
		public async Task Enabled_members_beyond_the_purchased_seats_are_licensed_in_enable_order()
		{
			Running(1);
			for (var i = 0; i < 26; i++)
			{
				_active.Add("m" + i);
				Seat("m" + i, 100 - i);
			}

			(await _gate.CanBeTrainedAsync(DepartmentId, "m0")).Should().BeTrue();
			(await _gate.CanBeTrainedAsync(DepartmentId, "m24")).Should().BeTrue();
			(await _gate.CanBeTrainedAsync(DepartmentId, "m25")).Should().BeFalse("the 26th enabled member waits for a pack");
		}

		[Test]
		public async Task Seats_are_per_department()
		{
			Running(1);
			Running(1, departmentId: OtherDepartmentId);
			_active.Add("shared");
			Seat("shared", 5, OtherDepartmentId);

			(await _gate.CanBeTrainedAsync(DepartmentId, "shared")).Should().BeFalse("a seat in another department does not count here");
			(await _gate.CanBeTrainedAsync(OtherDepartmentId, "shared")).Should().BeTrue();
		}

		[Test]
		public async Task Nobody_is_trained_through_the_lms_while_the_flag_is_off_or_the_add_on_has_lapsed()
		{
			_active.Add("u1");
			Seat("u1", 5);
			LastSeen(DateTime.UtcNow.AddDays(-30), cancelled: true);
			(await _gate.CanBeTrainedAsync(DepartmentId, "u1")).Should().BeFalse("lapsed");

			Running(1);
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.TrainingLms, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = false });
			(await _gate.CanBeTrainedAsync(DepartmentId, "u1")).Should().BeFalse("flag off");
		}
	}
}
