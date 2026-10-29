using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Options;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The one hard difference between the ADP addon pages and every other addon page (plan 17.1):
	/// buying and cancelling are restricted to <c>Department.ManagingUserId</c>, not to department
	/// administrators generally.
	///
	/// The reason is asymmetric consequence. Enrolling commits the department's data to a key it then
	/// depends on to read its own records, and cancelling starts the migration that decrypts all of
	/// it. A department can have many administrators; exactly one person owns the account, and that
	/// is who answers for both of those.
	///
	/// The page hides the buttons from anyone else, which is a courtesy. These tests are about the
	/// server refusing the POST regardless of what the page drew. Purchases run through the yearly
	/// checkout (AdpAddonConfig); departments that hold the legacy subscription item still cancel it.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class AdpAddonBillingAuthorizationTests
	{
		private const int DepartmentId = 10;
		private const string ManagingUserId = "the-owner";
		private const string OtherAdminUserId = "an-admin";

		private Mock<ISubscriptionsService> _subscriptionsService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentDataProtectionService> _dataProtectionService;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IAdpBillingService> _adpBilling;

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private SubscriptionController BuildController(string callerUserId, bool holdsAddon = true)
		{
			var department = new Department
			{
				DepartmentId = DepartmentId,
				Name = "Test Department",
				ManagingUserId = ManagingUserId
			};

			_dataProtectionService = new Mock<IDepartmentDataProtectionService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);

			var legacyPlanAddon = new PlanAddon { PlanAddonId = "adp-addon", AddonType = (int)PlanAddonTypes.ADP, PlanId = 4, Cost = 999 };
			var yearlyPlanAddon = new PlanAddon { PlanAddonId = AdpAddonConfig.YearlyPlanAddonId, AddonType = (int)PlanAddonTypes.ADP, Cost = 1950 };

			_subscriptionsService = new Mock<ISubscriptionsService>();
			_subscriptionsService.Setup(x => x.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ADP, false))
				.ReturnsAsync(new List<PlanAddon> { legacyPlanAddon, yearlyPlanAddon });
			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Plan { PlanId = 2, Name = "Paid", Cost = 100 });
			_subscriptionsService.Setup(x => x.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>()))
				.ReturnsAsync(holdsAddon
					? new List<PaymentAddon> { new PaymentAddon { PlanAddonId = "adp-addon", IsCancelled = false, EndingOn = DateTime.UtcNow.AddYears(1) } }
					: new List<PaymentAddon>());

			// Offered (enrollment toggle on), with a yearly checkout that can open: the purchase path these tests exercise.
			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, DepartmentId))
				.ReturnsAsync(new FeatureFlagEvaluation { Key = FeatureFlagKeys.DepartmentProtectedDataEnrollment, IsEnabled = true });
			_adpBilling = new Mock<IAdpBillingService>();
			_adpBilling.Setup(x => x.GetAsync(DepartmentId)).ReturnsAsync(new AdpBillingStatus { Provider = "Stripe", Currency = "USD", YearlyAmount = 1950, CheckoutAvailable = true });
			_adpBilling.Setup(x => x.BeginCheckoutAsync(DepartmentId)).ReturnsAsync(new AdpCheckout { Provider = "Stripe", Url = "https://checkout.stripe.com/c/pay/cs_adp" });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, callerUserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};

			// The audit event these actions write stamps the caller's IP, and IpAddressHelper throws
			// when it cannot find one - a bare DefaultHttpContext has no connection.
			httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;

			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor =
				new HttpContextAccessor { HttpContext = httpContext };

			return new SubscriptionController(
				_departmentsService.Object,
				Mock.Of<IUsersService>(),
				Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<Resgrid.Model.Services.IAuthorizationService>(),
				_subscriptionsService.Object,
				Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IUnitsService>(),
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IEmailService>(),
				Mock.Of<IAffiliateService>(),
				Mock.Of<IUserProfileService>(),
				Options.Create(new AppOptions()),
				Mock.Of<IEventAggregator>(),
				_dataProtectionService.Object,
				_flags.Object,
				_adpBilling.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[Test]
		public async Task A_department_admin_who_is_not_the_managing_member_cannot_buy_it()
		{
			var controller = BuildController(OtherAdminUserId, holdsAddon: false);

			var result = await controller.AdpCheckout();

			result.Should().BeOfType<ForbidResult>();
			_adpBilling.Verify(x => x.BeginCheckoutAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_department_admin_who_is_not_the_managing_member_cannot_cancel_it()
		{
			var controller = BuildController(OtherAdminUserId);

			var result = await controller.CancelAdpAddon(CancellationToken.None);

			// SecureBaseController.Unauthorized() redirects rather than returning a 401 - this is a
			// cookie-authenticated MVC page, not an API.
			result.Should().BeOfType<RedirectResult>()
				.Which.Url.Should().Be("/Public/Unauthorized");
			_subscriptionsService.Verify(x => x.CancelPlanAddonByTypeFromStripeAsync(It.IsAny<int>(), It.IsAny<int>()),
				Times.Never);
			_adpBilling.Verify(x => x.CancelRenewalAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task The_managing_member_cancels_a_legacy_subscription_item_where_it_lives()
		{
			var controller = BuildController(ManagingUserId);
			_subscriptionsService.Setup(x => x.CancelPlanAddonByTypeFromStripeAsync(DepartmentId, (int)PlanAddonTypes.ADP))
				.ReturnsAsync(true);

			await controller.CancelAdpAddon(CancellationToken.None);

			_subscriptionsService.Verify(x => x.CancelPlanAddonByTypeFromStripeAsync(DepartmentId, (int)PlanAddonTypes.ADP),
				Times.Once);
			_adpBilling.Verify(x => x.CancelRenewalAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task The_managing_member_cancels_the_yearly_checkout_at_the_end_of_its_paid_year()
		{
			var controller = BuildController(ManagingUserId);
			_adpBilling.Setup(x => x.GetAsync(DepartmentId)).ReturnsAsync(new AdpBillingStatus { Provider = "Stripe", Active = true, CanCancel = true });
			_adpBilling.Setup(x => x.CancelRenewalAsync(DepartmentId)).ReturnsAsync(true);

			await controller.CancelAdpAddon(CancellationToken.None);

			_adpBilling.Verify(x => x.CancelRenewalAsync(DepartmentId), Times.Once);
			_subscriptionsService.Verify(x => x.CancelPlanAddonByTypeFromStripeAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never,
				"the yearly checkout is its own subscription; removing a base-subscription item would cancel nothing");
		}

		[Test]
		public async Task A_free_department_cannot_buy_it_even_as_the_managing_member()
		{
			var controller = BuildController(ManagingUserId, holdsAddon: false);
			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Plan { PlanId = 1, Name = "Forever Free", Cost = 0 });

			(await controller.AdpCheckout()).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(409);

			_adpBilling.Verify(x => x.BeginCheckoutAsync(It.IsAny<int>()), Times.Never, "the addon requires an active paid plan (plan 17.1)");
		}

		[Test]
		public async Task It_is_not_sold_while_the_enrollment_toggle_is_off()
		{
			var controller = BuildController(ManagingUserId, holdsAddon: false);
			_flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, DepartmentId))
				.ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = false });

			(await controller.AdpCheckout()).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(409);
			_adpBilling.Verify(x => x.BeginCheckoutAsync(It.IsAny<int>()), Times.Never, "the Subscription page hides the card, and the POST must agree");
		}

		[Test]
		public async Task Buying_the_addon_never_touches_protection_state()
		{
			var controller = BuildController(ManagingUserId, holdsAddon: false);

			var result = await controller.AdpCheckout();

			result.Should().BeOfType<JsonResult>("the page opens Stripe Checkout with the returned URL");
			// Buying makes the department ELIGIBLE to enroll and nothing more. The wizard, run later
			// and separately, is what commits data to a key.
			_dataProtectionService.Verify(x => x.QueueEnrollmentAsync(It.IsAny<int>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);

			_dataProtectionService.Verify(x => x.ScheduleOffboardingAsync(It.IsAny<int>(),
				It.IsAny<DepartmentDataProtectionOffboardingSource>(), It.IsAny<DateTime>(),
				It.IsAny<CancellationToken>()), Times.Never);

			_adpBilling.Verify(x => x.BeginCheckoutAsync(DepartmentId), Times.Once, "the purchase itself still has to happen");
			_subscriptionsService.Verify(x => x.AddAddonAddedToExistingSub(It.IsAny<int>(), It.IsAny<Plan>(), It.IsAny<PlanAddon>()), Times.Never,
				"ADP is never bolted onto the base subscription any more");
		}

		[Test]
		public async Task A_paddle_transaction_is_relayed_and_anything_else_is_refused()
		{
			var controller = BuildController(ManagingUserId, holdsAddon: false);
			_adpBilling.Setup(x => x.BeginCheckoutAsync(DepartmentId)).ReturnsAsync(new AdpCheckout { Provider = "Paddle", TransactionId = "txn_01abcdefghjkmnpqrstvwxyz00" });
			(await controller.AdpCheckout()).Should().BeOfType<JsonResult>();

			_adpBilling.Setup(x => x.BeginCheckoutAsync(DepartmentId)).ReturnsAsync(new AdpCheckout { Provider = "Stripe", Url = "https://evil.example.com/pay" });
			(await controller.AdpCheckout()).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(503);

			_adpBilling.Setup(x => x.BeginCheckoutAsync(DepartmentId)).ReturnsAsync((AdpCheckout)null);
			(await controller.AdpCheckout()).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(503);
		}

		[Test]
		public async Task The_purchase_page_shows_the_published_yearly_price_and_the_yearly_checkout()
		{
			var controller = BuildController(ManagingUserId, holdsAddon: false);

			var model = (await controller.BuyAdpAddon()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AdpAddonView>().Subject;

			model.Price.Should().Be("$1,950", "resgrid.com/pricing lists ADP at USD 1,950 a year");
			model.PlanAddonId.Should().Be(AdpAddonConfig.YearlyPlanAddonId);
			model.CanPurchaseOnline.Should().BeTrue();
			model.CheckoutProvider.Should().Be("Stripe");
		}

		[Test]
		public async Task A_held_legacy_item_is_reported_as_such()
		{
			var controller = BuildController(ManagingUserId);

			var model = (await controller.ManageAdpAddon()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AdpAddonView>().Subject;

			model.HasAddon.Should().BeTrue();
			model.IsLegacyItem.Should().BeTrue();
		}
	}
}
