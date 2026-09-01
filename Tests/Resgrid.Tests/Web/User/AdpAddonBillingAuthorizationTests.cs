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
	/// server refusing the POST regardless of what the page drew.
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

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private SubscriptionController BuildController(string callerUserId)
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

			var adpPlanAddon = new PlanAddon
			{
				PlanAddonId = "adp-addon",
				AddonType = (int)PlanAddonTypes.ADP,
				PlanId = 4,
				Cost = 999
			};

			_subscriptionsService = new Mock<ISubscriptionsService>();
			_subscriptionsService.Setup(x => x.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ADP))
				.ReturnsAsync(new List<PlanAddon> { adpPlanAddon });
			_subscriptionsService.Setup(x => x.GetPlanByIdAsync(4, It.IsAny<bool>()))
				.ReturnsAsync(new Plan { PlanId = 4, Name = "ADP", Cost = 999 });
			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Plan { PlanId = 2, Name = "Paid", Cost = 100 });
			_subscriptionsService.Setup(x => x.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>()))
				.ReturnsAsync(new List<PaymentAddon>
				{
					new PaymentAddon { PlanAddonId = "adp-addon", IsCancelled = false, EndingOn = DateTime.UtcNow.AddYears(1) }
				});

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
				_dataProtectionService.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[Test]
		public async Task A_department_admin_who_is_not_the_managing_member_cannot_buy_it()
		{
			var controller = BuildController(OtherAdminUserId);

			var result = await controller.BuyAdpAddon(new AdpAddonView { PlanAddonId = "adp-addon" }, CancellationToken.None);

			// SecureBaseController.Unauthorized() redirects rather than returning a 401 - this is a
			// cookie-authenticated MVC page, not an API.
			result.Should().BeOfType<RedirectResult>()
				.Which.Url.Should().Be("/Public/Unauthorized");
			_subscriptionsService.Verify(x => x.AddAddonAddedToExistingSub(It.IsAny<int>(), It.IsAny<Plan>(),
				It.IsAny<PlanAddon>()), Times.Never);
		}

		[Test]
		public async Task A_department_admin_who_is_not_the_managing_member_cannot_cancel_it()
		{
			var controller = BuildController(OtherAdminUserId);

			var result = await controller.CancelAdpAddon(CancellationToken.None);

			result.Should().BeOfType<RedirectResult>()
				.Which.Url.Should().Be("/Public/Unauthorized");
			_subscriptionsService.Verify(x => x.CancelPlanAddonByTypeFromStripeAsync(It.IsAny<int>(), It.IsAny<int>()),
				Times.Never);
		}

		[Test]
		public async Task The_managing_member_can_cancel_it()
		{
			var controller = BuildController(ManagingUserId);
			_subscriptionsService.Setup(x => x.CancelPlanAddonByTypeFromStripeAsync(DepartmentId, (int)PlanAddonTypes.ADP))
				.ReturnsAsync(true);

			await controller.CancelAdpAddon(CancellationToken.None);

			_subscriptionsService.Verify(x => x.CancelPlanAddonByTypeFromStripeAsync(DepartmentId, (int)PlanAddonTypes.ADP),
				Times.Once);
		}

		[Test]
		public async Task A_free_department_cannot_buy_it_even_as_the_managing_member()
		{
			var controller = BuildController(ManagingUserId);
			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Plan { PlanId = 1, Name = "Forever Free", Cost = 0 });

			await controller.BuyAdpAddon(new AdpAddonView { PlanAddonId = "adp-addon" }, CancellationToken.None);

			_subscriptionsService.Verify(x => x.AddAddonAddedToExistingSub(It.IsAny<int>(), It.IsAny<Plan>(),
				It.IsAny<PlanAddon>()), Times.Never, "the addon requires an active paid plan (plan 17.1)");
		}

		[Test]
		public async Task Buying_the_addon_never_touches_protection_state()
		{
			var controller = BuildController(ManagingUserId);

			await controller.BuyAdpAddon(new AdpAddonView { PlanAddonId = "adp-addon" }, CancellationToken.None);

			// Buying makes the department ELIGIBLE to enroll and nothing more. The wizard, run later
			// and separately, is what commits data to a key.
			_dataProtectionService.Verify(x => x.QueueEnrollmentAsync(It.IsAny<int>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);

			_dataProtectionService.Verify(x => x.ScheduleOffboardingAsync(It.IsAny<int>(),
				It.IsAny<DepartmentDataProtectionOffboardingSource>(), It.IsAny<DateTime>(),
				It.IsAny<CancellationToken>()), Times.Never);

			_subscriptionsService.Verify(x => x.AddAddonAddedToExistingSub(DepartmentId, It.IsAny<Plan>(),
				It.IsAny<PlanAddon>()), Times.Once, "the purchase itself still has to happen");
		}
	}
}
