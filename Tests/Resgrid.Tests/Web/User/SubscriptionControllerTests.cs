using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Options;

namespace Resgrid.Tests.Web.User
{
	[TestFixture]
	[NonParallelizable]
	public class SubscriptionControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "subscription-admin";

		private Mock<ISubscriptionsService> _subscriptionsService;
		private Mock<IFeatureToggleService> _flags;

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private SubscriptionController BuildController()
		{
			var department = new Department
			{
				DepartmentId = DepartmentId,
				Name = "Test Department",
				TimeZone = "New Zealand Standard Time"
			};
			var payment = new Payment
			{
				DepartmentId = DepartmentId,
				PlanId = 1,
				EndingOn = DateTime.MaxValue.AddTicks(-1)
			};

			var departmentsService = new Mock<IDepartmentsService>();
			departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(department);
			departmentsService.Setup(x => x.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(DepartmentId, false))
				.ReturnsAsync(new List<IdentityUser>());

			_subscriptionsService = new Mock<ISubscriptionsService>();
			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DepartmentId, false))
				.ReturnsAsync(new Plan { PlanId = 1, Name = "Forever Free", Frequency = (int)PlanFrequency.Never });
			_subscriptionsService.Setup(x => x.GetCurrentPaymentForDepartmentAsync(DepartmentId, true)).ReturnsAsync(payment);
			_subscriptionsService.Setup(x => x.GetAllPaymentsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<Payment> { payment });

			var authorizationService = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorizationService.Setup(x => x.CanUserManageSubscriptionAsync(UserId, DepartmentId)).ReturnsAsync(true);

			var unitsService = new Mock<IUnitsService>();
			unitsService.Setup(x => x.GetUnitsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(new List<Unit>());

			var departmentSettingsService = new Mock<IDepartmentSettingsService>();
			departmentSettingsService.Setup(x => x.GetPaddleCustomerIdForDepartmentAsync(DepartmentId))
				.ReturnsAsync("paddle-customer");

			_flags = new Mock<IFeatureToggleService>();

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor =
				new HttpContextAccessor { HttpContext = httpContext };

			return new SubscriptionController(
				departmentsService.Object,
				Mock.Of<IUsersService>(),
				Mock.Of<IDepartmentGroupsService>(),
				authorizationService.Object,
				_subscriptionsService.Object,
				Mock.Of<IPersonnelRolesService>(),
				unitsService.Object,
				departmentSettingsService.Object,
				Mock.Of<IEmailService>(),
				Mock.Of<IAffiliateService>(),
				Mock.Of<IUserProfileService>(),
				Options.Create(new AppOptions()),
				Mock.Of<IEventAggregator>(),
				Mock.Of<IDepartmentDataProtectionService>(),
				_flags.Object,
				Mock.Of<IAdpBillingService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext },
				TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
			};
		}

		private void Toggle(string key, bool enabled) =>
			_flags.Setup(x => x.EvaluateFreshAsync(key, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { Key = key, IsEnabled = enabled });

		private static async Task<SubscriptionView> IndexModelAsync(SubscriptionController controller) =>
			(await controller.Index()).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<SubscriptionView>().Subject;

		[Test]
		public async Task Index_TreatsSerializedMaxDateAsNeverExpiring()
		{
			var model = await IndexModelAsync(BuildController());

			model.Expires.Should().Be("Never");
		}

		[Test]
		public async Task Index_offers_an_addon_only_while_its_toggle_is_on()
		{
			var controller = BuildController();
			_subscriptionsService.Setup(x => x.HasActiveSubForDepartmentFromStripeAsync(DepartmentId)).ReturnsAsync(true);
			Toggle(FeatureFlagKeys.AiEnhanced, true);
			Toggle(FeatureFlagKeys.BusinessOperations, false);

			var model = await IndexModelAsync(controller);
			var visible = model.Addons.Where(a => a.IsVisible(model.HasActiveSubscription)).Select(a => a.Definition.Type).ToList();

			visible.Should().Contain(PlanAddonTypes.EnhancedAi);
			visible.Should().Contain(PlanAddonTypes.PTT, "Push-To-Talk has no toggle and is always offered on a paid plan");
			visible.Should().NotContain(PlanAddonTypes.BusinessOperations, "its toggle is off and the department does not hold it");
			visible.Should().NotContain(PlanAddonTypes.ReadinessPro, "an unseeded or unevaluated toggle counts as off");
		}

		[Test]
		public async Task Index_keeps_a_held_addon_listed_after_its_toggle_goes_off()
		{
			var controller = BuildController();
			_subscriptionsService.Setup(x => x.HasActiveSubForDepartmentFromStripeAsync(DepartmentId)).ReturnsAsync(true);
			Toggle(FeatureFlagKeys.IncidentBackOffice, false);
			_subscriptionsService.Setup(x => x.GetAllAddonPlansAsync()).ReturnsAsync(new List<PlanAddon>
			{
				new PlanAddon { PlanAddonId = "ibo", AddonType = (int)PlanAddonTypes.IncidentBackOffice }
			});
			var endsOn = DateTime.UtcNow.AddDays(9);
			_subscriptionsService.Setup(x => x.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.Is<List<string>>(ids => ids.Contains("ibo"))))
				.ReturnsAsync(new List<PaymentAddon> { new PaymentAddon { PlanAddonId = "ibo", IsCancelled = true, EndingOn = endsOn } });

			var model = await IndexModelAsync(controller);
			var ibo = model.Addons.Single(a => a.Definition.Type == PlanAddonTypes.IncidentBackOffice);

			ibo.Offered.Should().BeFalse();
			ibo.IsVisible(model.HasActiveSubscription).Should().BeTrue("a held add-on must stay manageable so it can be cancelled");
			ibo.Cancelled.Should().BeTrue();
			ibo.EndsOn.Should().Be(endsOn);
		}

		[Test]
		public async Task Index_shows_no_addon_to_buy_without_a_paid_plan()
		{
			var controller = BuildController();
			_subscriptionsService.Setup(x => x.HasActiveSubForDepartmentFromStripeAsync(DepartmentId)).ReturnsAsync(false);
			Toggle(FeatureFlagKeys.AiEnhanced, true);

			var model = await IndexModelAsync(controller);

			model.Addons.Where(a => a.IsVisible(model.HasActiveSubscription)).Should().BeEmpty();
		}
	}
}
