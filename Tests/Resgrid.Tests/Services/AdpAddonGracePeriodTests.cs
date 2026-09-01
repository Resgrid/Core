using System;
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
	/// What happens to a department's protection when the money is late (ADP plan 17.3).
	///
	/// The distinction this exists for: a declined card and an unpaid NET45 invoice both arrive as
	/// "not paid", and they are not the same event. The card failed and the provider will retry for
	/// days. The invoice has not failed at all — no charge was attempted, it simply is not due yet,
	/// and a purchase order can sit in accounts payable for well over a month. Treating the second
	/// like the first would decrypt a paying customer's data while their cheque is in the post, and
	/// there is no undo for that.
	///
	/// So protection outlives the provider's opinion by a grace window sized to how the department
	/// pays, and a payment landing inside that window recovers everything with nothing decrypted.
	/// </summary>
	[TestFixture]
	public class AdpAddonGracePeriodTests
	{
		private const int DeptId = 77;
		private static readonly DateTime PaidThrough = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

		private Mock<IDepartmentDataProtectionPolicyRepository> _policyRepo;
		private DepartmentDataProtectionPolicy _policy;
		private DepartmentDataProtectionService _service;

		private int _automaticGraceDays;
		private int _invoicedGraceDays;
		private int _maxGraceDays;

		[SetUp]
		public void SetUp()
		{
			_automaticGraceDays = Resgrid.Config.DataProtectionConfig.AddonAutomaticBillingGraceDays;
			_invoicedGraceDays = Resgrid.Config.DataProtectionConfig.AddonInvoicedBillingGraceDays;
			_maxGraceDays = Resgrid.Config.DataProtectionConfig.AddonMaxGraceDays;

			_policy = new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)DepartmentDataProtectionState.Enabled,
				AddonPaidThroughOn = PaidThrough
			};

			_policyRepo = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(() => _policy);
			_policyRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentDataProtectionPolicy>(),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentDataProtectionPolicy p, CancellationToken _, bool __) => p);

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

			var cacheProvider = new Mock<ICacheProvider>();
			cacheProvider.Setup(x => x.RetrieveAsync(It.IsAny<string>(),
					It.IsAny<Func<Task<DepartmentDataProtectionPolicy>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentDataProtectionPolicy>>, TimeSpan>((_, fallback, __) => fallback());
			cacheProvider.Setup(x => x.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);

			_service = new DepartmentDataProtectionService(_policyRepo.Object,
				new Mock<IDepartmentProtectedDataEgressPolicyRepository>().Object,
				new Mock<IDepartmentsService>().Object,
				new Mock<IFeatureToggleService>().Object,
				new Mock<ISubscriptionsService>().Object,
				cacheProvider.Object,
				new ProtectedFieldCatalog(),
				new Mock<IDepartmentDataProtectionMigrationRepository>().Object,
				new Mock<IDepartmentLockService>().Object,
				new Mock<IDepartmentKeyService>().Object);
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.DataProtectionConfig.AddonAutomaticBillingGraceDays = _automaticGraceDays;
			Resgrid.Config.DataProtectionConfig.AddonInvoicedBillingGraceDays = _invoicedGraceDays;
			Resgrid.Config.DataProtectionConfig.AddonMaxGraceDays = _maxGraceDays;
		}

		private static AdpAddonBillingEvent Event(AdpAddonBillingEventKind kind, string id, DateTime occurredOn) =>
			new AdpAddonBillingEvent
			{
				DepartmentId = DeptId,
				Kind = kind,
				ProviderEventId = id,
				ProviderName = "Stripe",
				ExternalSubscriptionRef = "sub_1",
				OccurredOnUtc = occurredOn
			};

		[Test]
		public async Task An_invoiced_department_gets_the_long_grace_and_a_card_gets_the_short_one()
		{
			_policy.AddonBillingMode = (int)AdpAddonBillingMode.Invoiced;

			var failure = Event(AdpAddonBillingEventKind.PaymentFailed, "evt-1", PaidThrough.AddDays(1));
			await _service.ApplyAddonBillingEventAsync(failure);

			_policy.AddonGraceEndsOn.Should().Be(
				PaidThrough.AddDays(Resgrid.Config.DataProtectionConfig.AddonInvoicedBillingGraceDays),
				"NET terms mean the invoice is not even due when the cycle ends");

			// Same department, same event, billed by card instead.
			_policy.AddonBillingMode = (int)AdpAddonBillingMode.Automatic;
			_policy.AddonGraceEndsOn = null;
			_policy.AddonDunningStartedOn = null;
			_policy.LastBillingEventId = null;
			_policy.LastBillingEventOccurredOn = null;

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-2", PaidThrough.AddDays(1)));

			_policy.AddonGraceEndsOn.Should().Be(
				PaidThrough.AddDays(Resgrid.Config.DataProtectionConfig.AddonAutomaticBillingGraceDays),
				"a card that declines is a real failure and the provider is already retrying it");
		}

		[Test]
		public async Task A_payment_failure_never_touches_protection()
		{
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-1", PaidThrough.AddDays(1)));

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled);
			_policy.OffboardingEffectiveOn.Should().BeNull("dunning schedules nothing; only exhausting it does");
		}

		[Test]
		public async Task Repeated_failures_cannot_push_the_grace_window_forward_forever()
		{
			// A card that will never work again produces one of these on every retry. If each one
			// re-anchored the window, the department would keep protection indefinitely while paying
			// nothing — the window has to be fixed when the lapse opens.
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-1", PaidThrough.AddDays(1)));
			var firstFloor = _policy.AddonGraceEndsOn;

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-2", PaidThrough.AddDays(9)));
			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-3", PaidThrough.AddDays(13)));

			_policy.AddonGraceEndsOn.Should().Be(firstFloor);
			_policy.AddonDunningStartedOn.Should().Be(PaidThrough.AddDays(1), "the lapse began at the first failure");
		}

		[Test]
		public async Task Exhausted_dunning_schedules_at_the_grace_floor_not_the_providers_date()
		{
			_policy.AddonBillingMode = (int)AdpAddonBillingMode.Invoiced;

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-1", PaidThrough.AddDays(1)));

			// The provider gives up long before an invoiced customer's terms have run out.
			var exhausted = Event(AdpAddonBillingEventKind.Cancelled, "evt-2", PaidThrough.AddDays(20));
			exhausted.IsDunningExhausted = true;
			exhausted.EffectiveEndUtc = PaidThrough.AddDays(20);

			await _service.ApplyAddonBillingEventAsync(exhausted);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.OffboardingScheduled);
			_policy.OffboardingEffectiveOn.Should().Be(
				PaidThrough.AddDays(Resgrid.Config.DataProtectionConfig.AddonInvoicedBillingGraceDays),
				"the provider's patience is not the customer's payment terms");
			_policy.OffboardingSource.Should().Be((int)DepartmentDataProtectionOffboardingSource.DunningExhausted);
		}

		[Test]
		public async Task A_late_payment_inside_the_window_recovers_everything()
		{
			_policy.AddonBillingMode = (int)AdpAddonBillingMode.Invoiced;

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-1", PaidThrough.AddDays(1)));

			var exhausted = Event(AdpAddonBillingEventKind.Cancelled, "evt-2", PaidThrough.AddDays(20));
			exhausted.IsDunningExhausted = true;
			await _service.ApplyAddonBillingEventAsync(exhausted);
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.OffboardingScheduled);

			// Day fifty of a NET45 cycle: accounts payable finally pays.
			var renewal = Event(AdpAddonBillingEventKind.Renewed, "evt-3", PaidThrough.AddDays(50));
			renewal.PaidThroughUtc = PaidThrough.AddYears(1);
			await _service.ApplyAddonBillingEventAsync(renewal);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Enabled);
			_policy.OffboardingEffectiveOn.Should().BeNull();
			_policy.AddonGraceEndsOn.Should().BeNull("the lapse is over");
			_policy.AddonDunningStartedOn.Should().BeNull();
			_policy.AddonPaidThroughOn.Should().Be(PaidThrough.AddYears(1));
		}

		[Test]
		public async Task A_member_who_cancels_gets_exactly_what_they_paid_for()
		{
			_policy.AddonBillingMode = (int)AdpAddonBillingMode.Invoiced;

			var cancellation = Event(AdpAddonBillingEventKind.Cancelled, "evt-1", PaidThrough.AddDays(-30));
			cancellation.EffectiveEndUtc = PaidThrough;

			await _service.ApplyAddonBillingEventAsync(cancellation);

			_policy.OffboardingEffectiveOn.Should().Be(PaidThrough,
				"they asked to stop - the grace exists for people who have not paid YET, not for people who chose to leave");
			_policy.OffboardingSource.Should().Be((int)DepartmentDataProtectionOffboardingSource.UserCancelled);
		}

		[Test]
		public async Task A_chargeback_gets_no_grace_at_all()
		{
			_policy.AddonBillingMode = (int)AdpAddonBillingMode.Invoiced;

			var chargeback = Event(AdpAddonBillingEventKind.Cancelled, "evt-1", PaidThrough.AddDays(5));
			chargeback.IsChargeback = true;
			chargeback.EffectiveEndUtc = PaidThrough.AddDays(5);

			await _service.ApplyAddonBillingEventAsync(chargeback);

			_policy.OffboardingEffectiveOn.Should().Be(PaidThrough.AddDays(5),
				"a disputed payment is not a slow one");
			_policy.OffboardingSource.Should().Be((int)DepartmentDataProtectionOffboardingSource.Chargeback);
		}

		[Test]
		public async Task A_support_override_extends_the_window_but_cannot_run_away()
		{
			_policy.AddonBillingMode = (int)AdpAddonBillingMode.Automatic;
			_policy.AddonGraceDaysOverride = 90;

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-1", PaidThrough.AddDays(1)));
			_policy.AddonGraceEndsOn.Should().Be(PaidThrough.AddDays(90));

			// A typo with an extra digit must not become a permanent free ride.
			_policy.AddonGraceEndsOn = null;
			_policy.AddonDunningStartedOn = null;
			_policy.LastBillingEventId = null;
			_policy.LastBillingEventOccurredOn = null;
			_policy.AddonGraceDaysOverride = 9000;

			await _service.ApplyAddonBillingEventAsync(Event(AdpAddonBillingEventKind.PaymentFailed, "evt-2", PaidThrough.AddDays(1)));

			_policy.AddonGraceEndsOn.Should().Be(
				PaidThrough.AddDays(Resgrid.Config.DataProtectionConfig.AddonMaxGraceDays));
		}

		[Test]
		public async Task A_provider_that_reports_exhaustion_as_a_payment_failure_still_schedules()
		{
			// Not every provider sends a separate cancellation when it gives up.
			var exhausted = Event(AdpAddonBillingEventKind.PaymentFailed, "evt-1", PaidThrough.AddDays(20));
			exhausted.IsDunningExhausted = true;
			exhausted.DunningState = "dunning_exhausted";

			await _service.ApplyAddonBillingEventAsync(exhausted);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.OffboardingScheduled);
			_policy.OffboardingSource.Should().Be((int)DepartmentDataProtectionOffboardingSource.DunningExhausted);
		}

		[Test]
		public async Task The_paid_through_date_never_moves_backwards()
		{
			var renewal = Event(AdpAddonBillingEventKind.Renewed, "evt-1", PaidThrough.AddDays(1));
			renewal.PaidThroughUtc = PaidThrough.AddYears(1);
			await _service.ApplyAddonBillingEventAsync(renewal);

			// A webhook from an older cycle turning up late must not shorten what the department has
			// already been told it is paid up to.
			var late = Event(AdpAddonBillingEventKind.Renewed, "evt-2", PaidThrough.AddDays(2));
			late.PaidThroughUtc = PaidThrough;
			await _service.ApplyAddonBillingEventAsync(late);

			_policy.AddonPaidThroughOn.Should().Be(PaidThrough.AddYears(1));
		}
	}
}
