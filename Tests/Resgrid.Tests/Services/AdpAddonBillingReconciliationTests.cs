using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The billing/data-safety state contract (ADP plan 17.3). The rule that matters most: no
	/// billing event ever changes ciphertext. Cancellation SCHEDULES an offboarding migration and
	/// that migration, later, is what decrypts — so a member who cancels keeps working protection
	/// until the end of what they paid for.
	/// </summary>
	[TestFixture]
	public class AdpAddonBillingReconciliationTests
	{
		private const int DeptId = 42;

		private Mock<IDepartmentDataProtectionPolicyRepository> _policyRepo;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentDataProtectionPolicy _policy;
		private DepartmentDataProtectionService _service;

		[SetUp]
		public void SetUp()
		{
			_policy = new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)DepartmentDataProtectionState.Enabled
			};

			_policyRepo = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(() => _policy);
			_policyRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentDataProtectionPolicy>(),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentDataProtectionPolicy p, CancellationToken _, bool __) => p);

			// The state machine is a compare-and-swap in the repository; mirror it on the fake so a
			// transition only "happens" from the expected state, exactly as the database enforces.
			_policyRepo.Setup(x => x.TryTransitionStateAsync(DeptId, It.IsAny<DepartmentDataProtectionState>(),
					It.IsAny<DepartmentDataProtectionState>(), It.IsAny<int?>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, DepartmentDataProtectionState from, DepartmentDataProtectionState to,
					int? kind, string by, CancellationToken __) =>
				{
					if ((DepartmentDataProtectionState)_policy.State != from)
						return 0;

					_policy.State = (int)to;
					return 1;
				});

			_cacheProvider = new Mock<ICacheProvider>();
			_cacheProvider.Setup(x => x.RetrieveAsync(It.IsAny<string>(),
					It.IsAny<Func<Task<DepartmentDataProtectionPolicy>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentDataProtectionPolicy>>, TimeSpan>((_, fallback, __) => fallback());
			_cacheProvider.Setup(x => x.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);

			_service = new DepartmentDataProtectionService(_policyRepo.Object,
				new Mock<IDepartmentProtectedDataEgressPolicyRepository>().Object,
				new Mock<IDepartmentsService>().Object,
				new Mock<IFeatureToggleService>().Object,
				new Mock<ISubscriptionsService>().Object,
				_cacheProvider.Object,
				new ProtectedFieldCatalog(),
				new Mock<IDepartmentDataProtectionMigrationRepository>().Object,
				new Mock<IDepartmentLockService>().Object,
				new Mock<IDepartmentKeyService>().Object);
		}

		private static AdpAddonBillingEvent Event(AdpAddonBillingEventKind kind, string eventId = "evt-1") =>
			new AdpAddonBillingEvent
			{
				DepartmentId = DeptId,
				Kind = kind,
				ProviderEventId = eventId,
				ProviderName = "Stripe",
				ExternalSubscriptionRef = "sub_123",
				OccurredOnUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
				EffectiveEndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc)
			};

		[Test]
		public async Task Activation_and_renewal_change_no_protection_state()
		{
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Activated));
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled);

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Renewed, "evt-2"));
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled);
		}

		[Test]
		public async Task Cancellation_schedules_offboarding_for_the_end_of_the_paid_cycle()
		{
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Cancelled));

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.OffboardingScheduled);
			_policy.OffboardingEffectiveOn.Should().Be(new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
				"protection runs to the end of what was paid for");
			_policy.OffboardingSource.Should().Be((int)DepartmentDataProtectionOffboardingSource.UserCancelled);
		}

		[Test]
		public async Task A_chargeback_still_goes_through_the_offboarding_worker()
		{
			var chargeback = Event(AdpAddonBillingEventKind.Cancelled);
			chargeback.IsChargeback = true;
			chargeback.EffectiveEndUtc = null;

			await _service.ApplyAddonBillingEventAsync(chargeback);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.OffboardingScheduled,
				"even a chargeback schedules the migration - it is never an instant crypto flip");
			_policy.OffboardingSource.Should().Be((int)DepartmentDataProtectionOffboardingSource.Chargeback);
		}

		[Test]
		public async Task A_payment_failure_leaves_protection_completely_alone()
		{
			var failure = Event(AdpAddonBillingEventKind.PaymentFailed);
			failure.DunningState = "past_due";

			await _service.ApplyAddonBillingEventAsync(failure);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled,
				"protection continues during dunning; exhausted dunning arrives later as a cancellation");
			_policy.OffboardingEffectiveOn.Should().BeNull();
		}

		[Test]
		public async Task A_replayed_cancellation_does_not_reschedule()
		{
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Cancelled));
			var scheduledFor = _policy.OffboardingEffectiveOn;

			// The member revokes it, then the provider redelivers the same webhook.
			_policy.State = (int)DepartmentDataProtectionState.Enabled;
			_policy.OffboardingEffectiveOn = null;

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Cancelled));

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled,
				"the event id was already applied; a redelivery must not undo a revocation");
			_policy.OffboardingEffectiveOn.Should().BeNull();
			scheduledFor.Should().NotBeNull();
		}

		[Test]
		public async Task A_renewal_after_a_cancellation_withdraws_the_scheduled_offboarding()
		{
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Cancelled));
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.OffboardingScheduled);

			// Out-of-order delivery: the provider's current truth is that the subscription is alive.
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Renewed, "evt-later"));

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled);
			_policy.OffboardingEffectiveOn.Should().BeNull();
			_policy.OffboardingSource.Should().BeNull();
		}

		[Test]
		public async Task A_cancellation_mid_encryption_is_deferred_not_applied()
		{
			_policy.State = (int)DepartmentDataProtectionState.Encrypting;

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Cancelled));

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Encrypting,
				"the enrollment finishes to Enabled first (plan 21.3); nothing is abandoned half-encrypted");
		}

		[Test]
		public async Task A_cancellation_redelivered_after_a_renewal_is_ignored()
		{
			// The id slot only remembers ONE event. Cancel, renew (which withdraws the offboarding and
			// takes over that slot), then the provider redelivers the cancel: its id no longer matches,
			// so nothing but the provider's own ordering can tell that it is stale.
			var cancellation = Event(AdpAddonBillingEventKind.Cancelled, "evt-cancel");
			cancellation.OccurredOnUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
			await _service.ApplyAddonBillingEventAsync(cancellation);
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.OffboardingScheduled);

			var renewal = Event(AdpAddonBillingEventKind.Renewed, "evt-renew");
			renewal.OccurredOnUtc = new DateTime(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc);
			await _service.ApplyAddonBillingEventAsync(renewal);
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled);

			await _service.ApplyAddonBillingEventAsync(cancellation);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled,
				"the redelivered cancellation predates the renewal, so it must not re-schedule the offboarding");
			_policy.OffboardingEffectiveOn.Should().BeNull();
		}

		[Test]
		public async Task A_genuinely_newer_cancellation_after_a_renewal_still_applies()
		{
			// The ordering guard must not swallow real events. A member who renews and then cancels a
			// week later has cancelled, and protection has to wind down.
			var renewal = Event(AdpAddonBillingEventKind.Renewed, "evt-renew");
			renewal.OccurredOnUtc = new DateTime(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc);
			await _service.ApplyAddonBillingEventAsync(renewal);

			var cancellation = Event(AdpAddonBillingEventKind.Cancelled, "evt-cancel");
			cancellation.OccurredOnUtc = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc);
			await _service.ApplyAddonBillingEventAsync(cancellation);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.OffboardingScheduled);
		}

		[Test]
		public async Task The_ordering_watermark_never_moves_backwards()
		{
			var newer = Event(AdpAddonBillingEventKind.Renewed, "evt-newer");
			newer.OccurredOnUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
			await _service.ApplyAddonBillingEventAsync(newer);

			// Applying an older-but-not-stale event must not lower the watermark, or the redeliveries
			// it just overtook would become applicable all over again.
			_policy.LastBillingEventOccurredOn.Should().Be(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));
		}

		[Test]
		public async Task The_subscription_reference_is_recorded_for_the_operator()
		{
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Activated));

			_policy.AddonBillingReference.Should().Be("sub_123");
			_policy.LastBillingEventId.Should().Be("evt-1");
		}
	}
}
