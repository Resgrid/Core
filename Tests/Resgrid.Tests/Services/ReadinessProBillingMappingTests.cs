using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Options;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public class ReadinessProBillingMappingTests
	{
		private bool _previousTestMode;
		private string _previousPaddleTestPrice;
		private string _previousBillingUrl;
		private string _previousBillingKey;

		[SetUp]
		public void SetUp()
		{
			_previousTestMode = PaymentProviderConfig.IsTestMode;
			_previousPaddleTestPrice = PaymentProviderConfig.PaddleReadinessProAddonTest;
			_previousBillingUrl = SystemBehaviorConfig.BillingApiBaseUrl;
			_previousBillingKey = ApiConfig.BackendInternalApikey;
		}

		[TearDown]
		public void TearDown()
		{
			PaymentProviderConfig.IsTestMode = _previousTestMode;
			PaymentProviderConfig.PaddleReadinessProAddonTest = _previousPaddleTestPrice;
			SystemBehaviorConfig.BillingApiBaseUrl = _previousBillingUrl;
			ApiConfig.BackendInternalApikey = _previousBillingKey;
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase(" ")]
		public void Stripe_test_mode_does_not_fall_back_to_the_supplied_live_price(string testPrice)
		{
			PaymentProviderConfig.IsTestMode = true;
			var addon = new PlanAddon
			{
				AddonType = (int)PlanAddonTypes.ReadinessPro,
				ExternalId = "price_0UDRwaqJFDZJcnkVnYP8bAcd", TestExternalId = testPrice
			};
			addon.GetExternalKey().Should().BeNull();
		}

		[Test]
		public void Stripe_selects_the_price_for_the_current_environment()
		{
			var addon = new PlanAddon
			{
				AddonType = (int)PlanAddonTypes.ReadinessPro,
				ExternalId = "price_0UDRwaqJFDZJcnkVnYP8bAcd", TestExternalId = "price_test_fixture"
			};
			PaymentProviderConfig.IsTestMode = true;
			addon.GetExternalKey().Should().Be("price_test_fixture");
			PaymentProviderConfig.IsTestMode = false;
			addon.GetExternalKey().Should().Be("price_0UDRwaqJFDZJcnkVnYP8bAcd");
		}

		[Test]
		public void Paddle_selects_the_price_for_the_current_environment_without_a_live_fallback()
		{
			PaymentProviderConfig.IsTestMode = true;
			PaymentProviderConfig.PaddleReadinessProAddonTest = "";
			PaymentProviderConfig.GetPaddleReadinessProAddonPriceId().Should().BeEmpty();
			PaymentProviderConfig.PaddleReadinessProAddonTest = "pri_test_fixture";
			PaymentProviderConfig.GetPaddleReadinessProAddonPriceId().Should().Be("pri_test_fixture");
			PaymentProviderConfig.IsTestMode = false;
			PaymentProviderConfig.GetPaddleReadinessProAddonPriceId().Should().Be("pri_01m20xy5x54j0sp4mcydcm4q6m");
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Legacy_purchase_get_and_post_cannot_sell_Readiness_Pro_as_PTT(bool post)
		{
			var billing = new Mock<ISubscriptionsService>(MockBehavior.Strict);
			billing.Setup(s => s.GetPlanAddonByIdAsync("readiness")).ReturnsAsync(new PlanAddon
			{
				PlanAddonId = "readiness", AddonType = (int)PlanAddonTypes.ReadinessPro
			});
			var controller = new SubscriptionController(Mock.Of<IDepartmentsService>(), Mock.Of<IUsersService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<Resgrid.Model.Services.IAuthorizationService>(), billing.Object,
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IEmailService>(), Mock.Of<IAffiliateService>(), Mock.Of<IUserProfileService>(),
				Options.Create(new AppOptions()), Mock.Of<IEventAggregator>(), Mock.Of<IDepartmentDataProtectionService>());

			var result = post
				? await controller.BuyAddon(new BuyAddonView { PlanAddonId = "readiness", Quantity = 1 }, CancellationToken.None)
				: await controller.BuyAddon("readiness");
			result.Should().BeOfType<NotFoundResult>();
			billing.Verify(s => s.GetPlanAddonByIdAsync("readiness"), Times.Once);
			billing.VerifyNoOtherCalls();
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task PTT_service_methods_reject_Readiness_before_constructing_a_billing_request(bool paddle)
		{
			// This would fail URI validation if either method tried to construct a billing client.
			SystemBehaviorConfig.BillingApiBaseUrl = "http://[invalid";
			ApiConfig.BackendInternalApikey = "unit-test-only";
			var service = new SubscriptionsService(null, null, null, null, null, null, null, null);
			var addon = new PlanAddon { AddonType = (int)PlanAddonTypes.ReadinessPro };
			var result = paddle
				? await service.ModifyPaddlePTTAddonSubscriptionAsync("customer", 1, addon)
				: await service.ModifyPTTAddonSubscriptionAsync("customer", 1, addon);
			result.Should().BeFalse();
		}
	}
}
