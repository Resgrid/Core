using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Who may buy and who holds the Dedicated Number add-on: an active paid entity plan is the billing relationship, the operator flag
	/// and a configured Billing API decide whether it is offered, and only a live paid PaymentAddons window is the entitlement.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DedicatedNumberAccessServiceTests
	{
		private const int DepartmentId = 31;
		private const string AddonId = DedicatedNumberAddonConfig.PlanAddonId;

		private Mock<IFeatureToggleService> _flags;
		private Mock<ISubscriptionsService> _subscriptions;
		private Mock<IDepartmentsService> _departments;
		private Mock<IAddressService> _addresses;
		private DedicatedNumberAccessService _service;
		private (string Url, string Key, int Provider, string Stripe, string Paddle) _saved;

		[SetUp]
		public void SetUp()
		{
			_saved = (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey, PaymentProviderConfig.ActivePaymentProvider,
				DedicatedNumberAddonConfig.StripeCountries, DedicatedNumberAddonConfig.PaddleCountries);
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.fixture";
			ApiConfig.BackendInternalApikey = "fixture-key";
			PaymentProviderConfig.ActivePaymentProvider = PaymentProviderConfig.ProviderStripe;
			DedicatedNumberAddonConfig.StripeCountries = "US,CA";
			DedicatedNumberAddonConfig.PaddleCountries = "";

			_flags = new Mock<IFeatureToggleService>();
			Flag(true);
			_subscriptions = new Mock<ISubscriptionsService>();
			Plan(37);
			_subscriptions.Setup(s => s.IsPlanRestrictedOrFree(It.IsAny<int>())).Returns((int id) => id == 1 || id == 6 || id == 7 || id == 8);
			_subscriptions.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.DedicatedNumber, It.IsAny<bool>()))
				.ReturnsAsync(new List<PlanAddon> { new PlanAddon { PlanAddonId = AddonId, AddonType = (int)PlanAddonTypes.DedicatedNumber } });
			Payments();
			_departments = new Mock<IDepartmentsService>();
			_addresses = new Mock<IAddressService>();
			_service = new DedicatedNumberAccessService(_flags.Object, _subscriptions.Object, _departments.Object, _addresses.Object);
		}

		[TearDown]
		public void TearDown()
		{
			(SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey, PaymentProviderConfig.ActivePaymentProvider,
				DedicatedNumberAddonConfig.StripeCountries, DedicatedNumberAddonConfig.PaddleCountries) = _saved;
		}

		private void Flag(bool on) =>
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.IntakeDedicatedNumber, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = on });

		private void Plan(int planId) =>
			_subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Plan { PlanId = planId });

		private void Payments(params PaymentAddon[] rows) =>
			_subscriptions.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>())).ReturnsAsync(new List<PaymentAddon>(rows));

		private static PaymentAddon Paid(string transactionId = "in_1", DateTime? endingOn = null) => new PaymentAddon
		{
			DepartmentId = DepartmentId, PlanAddonId = AddonId, TransactionId = transactionId, EffectiveOn = DateTime.UtcNow.AddDays(-3),
			EndingOn = endingOn ?? DateTime.UtcNow.AddDays(27)
		};

		[Test]
		public async Task An_active_entity_plan_with_the_flag_on_may_buy_and_numbers_are_us_and_canadian()
		{
			var access = await _service.GetAccessAsync(DepartmentId);

			access.HasBillingRelationship.Should().BeTrue();
			access.IsOffered.Should().BeTrue();
			access.HasActiveAddon.Should().BeFalse();
			access.CanPurchase.Should().BeTrue();
			access.CanProvision.Should().BeFalse();
			access.BillingProvider.Should().Be("Stripe");
			access.AllowedCountries.Should().Equal("US", "CA");
		}

		[TestCase(1)]
		[TestCase(8)]
		[TestCase(2)]
		public async Task Free_restricted_and_legacy_per_user_plans_have_no_billing_relationship(int planId)
		{
			Plan(planId);

			var access = await _service.GetAccessAsync(DepartmentId);

			access.HasBillingRelationship.Should().BeFalse();
			access.CanPurchase.Should().BeFalse();
			access.BillingProvider.Should().BeNull();
			access.AllowedCountries.Should().BeEmpty();
		}

		[Test]
		public async Task A_null_plan_from_billing_is_no_billing_relationship()
		{
			_subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync((Plan)null);

			(await _service.GetAccessAsync(DepartmentId)).HasBillingRelationship.Should().BeFalse();
		}

		[Test]
		public async Task The_flag_off_withholds_the_offer_but_never_a_held_add_on()
		{
			Flag(false);
			Payments(Paid());

			var access = await _service.GetAccessAsync(DepartmentId);

			access.IsOffered.Should().BeFalse();
			access.CanPurchase.Should().BeFalse();
			access.HasActiveAddon.Should().BeTrue();
			access.CanProvision.Should().BeTrue();
		}

		[Test]
		public async Task Only_a_live_paid_interval_is_the_entitlement()
		{
			Payments(Paid());
			(await _service.HasActiveAddonAsync(DepartmentId)).Should().BeTrue();

			Payments(Paid("SYSTEM"));
			(await _service.HasActiveAddonAsync(DepartmentId)).Should().BeFalse("a synthesized grant is not a purchase");

			Payments(Paid(endingOn: DateTime.MaxValue));
			(await _service.HasActiveAddonAsync(DepartmentId)).Should().BeFalse();

			Payments(Paid(endingOn: DateTime.UtcNow.AddMinutes(-1)));
			(await _service.GetActiveAddonStateAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task A_department_that_kept_the_add_on_after_its_plan_changed_can_still_manage_its_number()
		{
			Plan(1);
			Payments(Paid());

			var access = await _service.GetAccessAsync(DepartmentId);

			access.HasBillingRelationship.Should().BeFalse();
			access.HasActiveAddon.Should().BeTrue();
			access.BillingProvider.Should().Be("Stripe");
			access.AllowedCountries.Should().Equal("US", "CA");
		}

		[Test]
		public async Task Unconfigured_billing_offers_nothing_and_the_add_on_state_is_unknown()
		{
			SystemBehaviorConfig.BillingApiBaseUrl = "";
			Payments(Paid());

			var access = await _service.GetAccessAsync(DepartmentId);

			access.IsOffered.Should().BeFalse();
			access.HasBillingRelationship.Should().BeFalse();
			access.HasActiveAddon.Should().BeFalse();
			(await _service.GetActiveAddonStateAsync(DepartmentId)).Should().BeNull("the lifecycle must never act on an unconfigured install");
			_subscriptions.Verify(s => s.GetCurrentPaymentAddonsForDepartmentAsync(It.IsAny<int>(), It.IsAny<List<string>>()), Times.Never);
		}

		[Test]
		public async Task A_billing_failure_is_an_unknown_state_not_a_lapse()
		{
			_subscriptions.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>())).ThrowsAsync(new TimeoutException());
			(await _service.GetActiveAddonStateAsync(DepartmentId)).Should().BeNull();

			_subscriptions.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.DedicatedNumber, It.IsAny<bool>())).ReturnsAsync(new List<PlanAddon>());
			(await _service.GetActiveAddonStateAsync(DepartmentId)).Should().BeNull("a missing catalog row proves nothing about the department");
		}

		[Test]
		public async Task Payment_state_reads_the_plan_only_while_the_add_on_is_paid()
		{
			// No paid interval: stopped, and the plan is not asked about.
			var state = await _service.GetPaymentStateAsync(DepartmentId);
			state.AddonActive.Should().BeFalse();
			state.Stopped.Should().BeTrue();
			state.AddonStopped.Should().BeTrue();
			_subscriptions.Verify(s => s.GetCurrentPlanForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);

			// Paid add-on, paid entity plan: paying.
			Payments(Paid());
			state = await _service.GetPaymentStateAsync(DepartmentId);
			state.Paying.Should().BeTrue();
			state.Stopped.Should().BeFalse();

			// Paid add-on on a free or legacy plan: stopped through the plan.
			Plan(1);
			state = await _service.GetPaymentStateAsync(DepartmentId);
			state.PlanStopped.Should().BeTrue();
			state.Stopped.Should().BeTrue();
			Plan(2);
			(await _service.GetPaymentStateAsync(DepartmentId)).PlanStopped.Should().BeTrue("a legacy per-user plan is not an entity plan");
		}

		[Test]
		public async Task An_unreadable_plan_or_add_on_is_unknown_never_stopped()
		{
			Payments(Paid());
			_subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync((Plan)null);
			var state = await _service.GetPaymentStateAsync(DepartmentId);
			state.PaidEntityPlan.Should().BeNull();
			state.IsUnknown.Should().BeTrue();

			_subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ThrowsAsync(new TimeoutException());
			(await _service.GetPaymentStateAsync(DepartmentId)).IsUnknown.Should().BeTrue();

			// Billing unreachable for the add-on: unknown whatever the plan read would say (it answers failures with the free plan).
			_subscriptions.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>())).ThrowsAsync(new TimeoutException());
			Plan(1);
			state = await _service.GetPaymentStateAsync(DepartmentId);
			state.AddonActive.Should().BeNull();
			state.IsUnknown.Should().BeTrue();
			state.Stopped.Should().BeFalse();

			SystemBehaviorConfig.BillingApiBaseUrl = "";
			(await _service.GetPaymentStateAsync(DepartmentId)).IsUnknown.Should().BeTrue("an unconfigured install never releases a number");
		}

		[Test]
		public async Task Paddle_departments_get_a_number_in_their_own_country_only_when_it_is_configured()
		{
			PaymentProviderConfig.ActivePaymentProvider = PaymentProviderConfig.ProviderPaddle;
			DedicatedNumberAddonConfig.PaddleCountries = "DE, fr";
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, AddressId = 9 });
			_addresses.Setup(a => a.GetAddressByIdAsync(9)).ReturnsAsync(new Address { Country = "Germany" });

			var access = await _service.GetAccessAsync(DepartmentId);
			access.BillingProvider.Should().Be("Paddle");
			access.AllowedCountries.Should().Equal("DE");

			_addresses.Setup(a => a.GetAddressByIdAsync(9)).ReturnsAsync(new Address { Country = "Italy" });
			(await _service.GetAccessAsync(DepartmentId)).AllowedCountries.Should().BeEmpty("Italy needs a regulatory bundle the operator has not set up");
		}

		[TestCase("United States", "US")]
		[TestCase("United Kingdom", "GB")]
		[TestCase("Germany", "DE")]
		[TestCase("ca", "CA")]
		[TestCase("Narnia", null)]
		[TestCase("", null)]
		public void Address_countries_resolve_to_iso_codes(string country, string expected)
		{
			DedicatedNumberAccessService.ToCountryCode(country).Should().Be(expected);
		}

		[Test]
		public void Configured_country_lists_are_trimmed_upper_cased_and_deduplicated()
		{
			DedicatedNumberAccessService.ParseCountries(" us,CA;ca , x1, GBR").Should().Equal("US", "CA");
		}
	}
}
