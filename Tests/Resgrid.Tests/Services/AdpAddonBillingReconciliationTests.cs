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
				new Mock<IDepartmentDataProtectionMigrationRepository>().Object);
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
		public async Task The_subscription_reference_is_recorded_for_the_operator()
		{
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.Activated));

			_policy.AddonBillingReference.Should().Be("sub_123");
			_policy.LastBillingEventId.Should().Be("evt-1");
		}
	}
}
