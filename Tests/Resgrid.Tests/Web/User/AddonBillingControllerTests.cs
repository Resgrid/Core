using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The shared add-on billing flow (AddonBillingControllerBase), exercised through the Enhanced AI page: the operator toggle decides
	/// whether the add-on is offered, a held add-on stays manageable after the toggle goes off, and only the managing member may buy.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class AddonBillingControllerTests
	{
		private const int DepartmentId = 12;
		private const string OwnerId = "owner";
		private const string AdminId = "admin";

		private Mock<IAiBillingService> _billing;
		private Mock<IFeatureToggleService> _flags;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private static IStringLocalizer<T> Strings<T>()
		{
			var localizer = new Mock<IStringLocalizer<T>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return localizer.Object;
		}

		private AiBillingController Build(string userId, bool offered, AiBillingStatus status)
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = OwnerId });
			departments.Setup(x => x.GetDepartmentMemberAsync(userId, DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = userId });

			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_authorization.Setup(x => x.CanUserManageSubscriptionAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(true);

			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.AiEnhanced, DepartmentId))
				.ReturnsAsync(new FeatureFlagEvaluation { Key = FeatureFlagKeys.AiEnhanced, IsEnabled = offered });

			_billing = new Mock<IAiBillingService>();
			_billing.Setup(x => x.GetAsync(DepartmentId)).ReturnsAsync(status);

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, userId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			return new AiBillingController(_billing.Object, departments.Object, _authorization.Object, _flags.Object,
				Strings<Resgrid.Localization.Areas.User.Addons.Addons>(), Strings<Resgrid.Localization.Areas.User.EnhancedAi.EnhancedAi>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext },
				Url = Mock.Of<IUrlHelper>()
			};
		}

		private static AiBillingStatus NotHeld(bool checkoutAvailable = true) => new AiBillingStatus
			{ Provider = "Stripe", Currency = "USD", MonthlyAmount = 95m, CheckoutAvailable = checkoutAvailable };

		[Test]
		public async Task An_addon_whose_toggle_is_off_and_is_not_held_is_not_offered()
		{
			var controller = Build(OwnerId, offered: false, NotHeld(checkoutAvailable: false));

			var result = await controller.Index();

			result.Should().BeOfType<RedirectToActionResult>().Which.ControllerName.Should().Be("Subscription");
		}

		[Test]
		public async Task A_held_addon_stays_manageable_after_its_toggle_goes_off()
		{
			var controller = Build(OwnerId, offered: false, new AiBillingStatus
				{ Provider = "Stripe", Currency = "USD", MonthlyAmount = 95m, Active = true, CanCancel = true, PaidThroughUtc = DateTime.UtcNow.AddDays(20) });

			var view = (await controller.Index()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddonBillingView>().Subject;

			view.Holds.Should().BeTrue();
			view.CanCancel.Should().BeTrue("the managing member must still be able to stop the renewal");
			view.CanBuy.Should().BeFalse();
			view.Notices.Should().Contain("BillingFeatureOff");
		}

		[Test]
		public async Task The_managing_member_can_buy_an_offered_addon()
		{
			var controller = Build(OwnerId, offered: true, NotHeld());

			var view = (await controller.Index()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddonBillingView>().Subject;

			view.CanBuy.Should().BeTrue();
			view.Title.Should().Be("EnhancedAiTitle");
			view.Features.Should().HaveCount(AddonCatalog.Get(PlanAddonTypes.EnhancedAi).FeatureCount);
			view.Links.Should().Contain(l => l.External && l.Url.EndsWith("/features/enhanced-ai"), "each add-on links to its resgrid.com detail page");
		}

		[Test]
		public async Task An_offered_addon_whose_checkout_cannot_open_is_not_buyable()
		{
			var controller = Build(OwnerId, offered: true, NotHeld(checkoutAvailable: false));

			var view = (await controller.Index()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddonBillingView>().Subject;

			view.CanBuy.Should().BeFalse("the page explains checkout is temporarily unavailable instead of a dead button");
		}

		[Test]
		public async Task A_returning_completed_checkout_waits_for_activation_instead_of_offering_another_purchase()
		{
			var controller = Build(OwnerId, offered: true, NotHeld());

			var view = (await controller.Index(AddonBillingResults.Complete)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddonBillingView>().Subject;

			view.AwaitingActivation.Should().BeTrue();
			view.CanBuy.Should().BeFalse();
		}

		[Test]
		public async Task A_member_who_is_not_the_managing_member_cannot_start_a_checkout()
		{
			var controller = Build(AdminId, offered: true, NotHeld());

			var result = await controller.Checkout();

			result.Should().BeOfType<ForbidResult>();
			_billing.Verify(x => x.BeginCheckoutAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Checkout_is_refused_while_the_toggle_is_off()
		{
			var controller = Build(OwnerId, offered: false, NotHeld());

			var result = await controller.Checkout();

			result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(409);
			_billing.Verify(x => x.BeginCheckoutAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Only_a_stripe_checkout_url_or_a_paddle_transaction_is_relayed()
		{
			var controller = Build(OwnerId, offered: true, NotHeld());
			_billing.Setup(x => x.BeginCheckoutAsync(DepartmentId)).ReturnsAsync(new AiCheckout { Provider = "Stripe", Url = "https://checkout.stripe.com/c/pay/cs_test" });
			(await controller.Checkout()).Should().BeOfType<JsonResult>();

			_billing.Setup(x => x.BeginCheckoutAsync(DepartmentId)).ReturnsAsync(new AiCheckout { Provider = "Paddle", TransactionId = "txn_01abcdefghjkmnpqrstvwxyz00" });
			(await controller.Checkout()).Should().BeOfType<JsonResult>();

			_billing.Setup(x => x.BeginCheckoutAsync(DepartmentId)).ReturnsAsync(new AiCheckout { Provider = "Stripe", Url = "https://evil.example.com/checkout" });
			(await controller.Checkout()).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
		}

		[Test]
		public async Task A_failed_renewal_cancellation_returns_to_the_page_with_the_outcome()
		{
			var controller = Build(OwnerId, offered: true, NotHeld());
			_billing.Setup(x => x.CancelRenewalAsync(DepartmentId)).ReturnsAsync(false);

			var result = await controller.CancelRenewal();

			result.Should().BeOfType<RedirectToActionResult>().Which.RouteValues["result"].Should().Be(AddonBillingResults.RenewalFailed);
		}
	}

	[TestFixture]
	public class AddonCatalogTests
	{
		[TestCase(PlanAddonTypes.PTT, 35, 45)]
		[TestCase(PlanAddonTypes.ADP, 1950, 2550)]
		[TestCase(PlanAddonTypes.ReadinessPro, 150, 195)]
		[TestCase(PlanAddonTypes.BusinessOperations, 250, 295)]
		[TestCase(PlanAddonTypes.EnhancedAi, 95, 145)]
		[TestCase(PlanAddonTypes.IncidentBackOffice, 250, 350)]
		public void Prices_match_the_public_pricing_page(PlanAddonTypes type, int usd, int eur)
		{
			// resgrid.com/pricing (int-Public src/components/billing/region.ts): USD through Stripe, EUR through Paddle.
			var addon = AddonCatalog.Get(type);
			addon.StripeAmount().Should().Be(usd);
			addon.PaddleAmount().Should().Be(eur);
		}

		[Test]
		public void Every_addon_links_to_its_public_detail_page()
		{
			AddonCatalog.All.Select(a => a.LearnMoreUrl).Should().BeEquivalentTo(new[]
			{
				"https://resgrid.com/features/push-to-talk", "https://resgrid.com/features/readiness-pro", "https://resgrid.com/features/business-ops",
				"https://resgrid.com/features/incident-back-office", "https://resgrid.com/features/enhanced-ai", "https://resgrid.com/features/advanced-data-protection"
			});
		}

		[Test]
		public void Only_push_to_talk_is_offered_without_a_toggle()
		{
			AddonCatalog.All.Where(a => a.FeatureFlag == null).Select(a => a.Type).Should().Equal(PlanAddonTypes.PTT);
		}
	}
}
