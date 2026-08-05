using System;
using System.Collections.Generic;
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

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task Index_TreatsSerializedMaxDateAsNeverExpiring()
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

			var subscriptionsService = new Mock<ISubscriptionsService>();
			subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DepartmentId, false))
				.ReturnsAsync(new Plan { PlanId = 1, Name = "Forever Free", Frequency = (int)PlanFrequency.Never });
			subscriptionsService.Setup(x => x.GetCurrentPaymentForDepartmentAsync(DepartmentId, true)).ReturnsAsync(payment);
			subscriptionsService.Setup(x => x.GetAllPaymentsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<Payment> { payment });

			var authorizationService = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorizationService.Setup(x => x.CanUserManageSubscriptionAsync(UserId, DepartmentId)).ReturnsAsync(true);

			var unitsService = new Mock<IUnitsService>();
			unitsService.Setup(x => x.GetUnitsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(new List<Unit>());

			var departmentSettingsService = new Mock<IDepartmentSettingsService>();
			departmentSettingsService.Setup(x => x.GetPaddleCustomerIdForDepartmentAsync(DepartmentId))
				.ReturnsAsync("paddle-customer");

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

			var controller = new SubscriptionController(
				departmentsService.Object,
				Mock.Of<IUsersService>(),
				Mock.Of<IDepartmentGroupsService>(),
				authorizationService.Object,
				subscriptionsService.Object,
				Mock.Of<IPersonnelRolesService>(),
				unitsService.Object,
				departmentSettingsService.Object,
				Mock.Of<IEmailService>(),
				Mock.Of<IAffiliateService>(),
				Mock.Of<IUserProfileService>(),
				Options.Create(new AppOptions()),
				Mock.Of<IEventAggregator>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};

			var result = await controller.Index();

			var model = result.Should().BeOfType<ViewResult>().Subject.Model
				.Should().BeOfType<SubscriptionView>().Subject;
			model.Expires.Should().Be("Never");
		}
	}
}
