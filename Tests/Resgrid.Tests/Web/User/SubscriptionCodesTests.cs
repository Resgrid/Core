using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Billing.Api;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Options;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Discount and affiliate codes entered on the Subscription page after signup: only subscription managers may enter them, a valid
	/// discount code is applied to the active subscription or carried into checkout, and an affiliate attribution is set once.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class SubscriptionCodesTests
	{
		private const int DepartmentId = 21;
		private const string UserId = "billing-admin";

		private Mock<ISubscriptionsService> _subscriptions;
		private Mock<IDepartmentsService> _departments;
		private Mock<IAffiliateService> _affiliates;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private Department _department;

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private SubscriptionController Build(bool canManage = true)
		{
			_department = new Department { DepartmentId = DepartmentId, Name = "Codes Department" };
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _department);
			_departments.Setup(x => x.SaveDepartmentAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>())).ReturnsAsync((Department d, CancellationToken _) => d);
			_subscriptions = new Mock<ISubscriptionsService>();
			_affiliates = new Mock<IAffiliateService>();
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_authorization.Setup(x => x.CanUserManageSubscriptionAsync(UserId, DepartmentId)).ReturnsAsync(canManage);

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			return new SubscriptionController(_departments.Object, Mock.Of<IUsersService>(), Mock.Of<IDepartmentGroupsService>(), _authorization.Object,
				_subscriptions.Object, Mock.Of<IPersonnelRolesService>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IEmailService>(),
				_affiliates.Object, Mock.Of<IUserProfileService>(), Options.Create(new AppOptions()), Mock.Of<IEventAggregator>(),
				Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IAdpBillingService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext },
				TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
			};
		}

		private void DiscountOutcome(DiscountCodeOutcomes outcome) =>
			_subscriptions.Setup(x => x.ApplyDiscountCodeToDepartmentAsync(DepartmentId, "SPRING25")).ReturnsAsync(new ApplyDiscountCodeData { Outcome = outcome });

		[Test]
		public async Task A_valid_code_is_applied_to_the_active_subscription()
		{
			var controller = Build();
			DiscountOutcome(DiscountCodeOutcomes.Applied);

			await controller.ApplyDiscountCode(" SPRING25 ");

			controller.TempData["SubscriptionCodesMessage"].Should().Be("DiscountApplied");
			controller.TempData.ContainsKey("SubscriptionPendingDiscountCode").Should().BeFalse();
		}

		[Test]
		public async Task A_valid_code_without_a_subscription_is_carried_into_checkout()
		{
			var controller = Build();
			DiscountOutcome(DiscountCodeOutcomes.NoActiveSubscription);

			await controller.ApplyDiscountCode("SPRING25");

			controller.TempData["SubscriptionCodesMessage"].Should().Be("DiscountPending");
			controller.TempData["SubscriptionPendingDiscountCode"].Should().Be("SPRING25");
		}

		[Test]
		public async Task An_invalid_code_is_reported_and_never_carried_into_checkout()
		{
			var controller = Build();
			DiscountOutcome(DiscountCodeOutcomes.InvalidCode);

			await controller.ApplyDiscountCode("SPRING25");

			controller.TempData["SubscriptionCodesMessage"].Should().Be("DiscountInvalid");
			controller.TempData.ContainsKey("SubscriptionPendingDiscountCode").Should().BeFalse();
		}

		[TestCase(DiscountCodeOutcomes.Applied)]
		[TestCase(DiscountCodeOutcomes.InvalidCode)]
		[TestCase(DiscountCodeOutcomes.Unavailable)]
		public async Task A_new_entry_drops_the_code_that_was_waiting_for_checkout(DiscountCodeOutcomes outcome)
		{
			var controller = Build();
			controller.TempData["SubscriptionPendingDiscountCode"] = "WINTER10";
			DiscountOutcome(outcome);

			await controller.ApplyDiscountCode("SPRING25");

			controller.TempData.ContainsKey("SubscriptionPendingDiscountCode").Should().BeFalse("a replaced code must not be carried into a later checkout");
		}

		[Test]
		public async Task A_new_pending_code_replaces_the_one_that_was_waiting()
		{
			var controller = Build();
			controller.TempData["SubscriptionPendingDiscountCode"] = "WINTER10";
			DiscountOutcome(DiscountCodeOutcomes.NoActiveSubscription);

			await controller.ApplyDiscountCode("SPRING25");

			controller.TempData["SubscriptionPendingDiscountCode"].Should().Be("SPRING25");
		}

		[Test]
		public async Task A_member_who_cannot_manage_the_subscription_cannot_enter_codes()
		{
			var controller = Build(canManage: false);

			(await controller.ApplyDiscountCode("SPRING25")).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			(await controller.ApplyAffiliateCode("PARTNER", CancellationToken.None)).Should().BeOfType<RedirectResult>();

			_subscriptions.Verify(x => x.ApplyDiscountCodeToDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
			_departments.Verify(x => x.SaveDepartmentAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_active_affiliate_code_is_recorded_when_the_department_has_none()
		{
			var controller = Build();
			_affiliates.Setup(x => x.GetActiveAffiliateByCodeAsync("partner")).ReturnsAsync(new Affiliate { AffiliateCode = "PARTNER" });

			await controller.ApplyAffiliateCode(" partner ", CancellationToken.None);

			_department.AffiliateCode.Should().Be("PARTNER", "the affiliate's own spelling of the code is stored");
			_departments.Verify(x => x.SaveDepartmentAsync(_department, It.IsAny<CancellationToken>()), Times.Once);
			controller.TempData["SubscriptionCodesMessage"].Should().Be("AffiliateApplied");
		}

		[Test]
		public async Task An_existing_affiliate_attribution_is_never_replaced()
		{
			var controller = Build();
			_department.AffiliateCode = "ORIGINAL";
			_affiliates.Setup(x => x.GetActiveAffiliateByCodeAsync(It.IsAny<string>())).ReturnsAsync(new Affiliate { AffiliateCode = "OTHER" });

			await controller.ApplyAffiliateCode("OTHER", CancellationToken.None);

			_department.AffiliateCode.Should().Be("ORIGINAL");
			_departments.Verify(x => x.SaveDepartmentAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>()), Times.Never);
			controller.TempData["SubscriptionCodesMessage"].Should().Be("AffiliateAlreadySet");
		}

		[Test]
		public async Task An_unknown_or_inactive_affiliate_code_is_refused()
		{
			var controller = Build();

			await controller.ApplyAffiliateCode("NOPE", CancellationToken.None);

			_department.AffiliateCode.Should().BeNull();
			controller.TempData["SubscriptionCodesMessage"].Should().Be("AffiliateInvalid");
		}
	}
}
