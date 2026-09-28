using System;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Middleware;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Web
{
	[TestFixture]
	public class Require2FAEnrollmentMiddlewareTests
	{
		private const string UserId = "99E85870-9DAA-4BE5-8C05-77653711CA28";
		private const int DepartmentId = 42;

		private Mock<UserManager<IdentityUser>> _userManager;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;

		[SetUp]
		public void SetUp()
		{
			_userManager = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			_userManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(new IdentityUser { Id = UserId });
			_userManager.Setup(x => x.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);

			_departmentsService = new Mock<IDepartmentsService>();
			_departmentsService.Setup(x => x.GetDepartmentByUserIdAsync(UserId, false))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId });

			_departmentSettingsService = new Mock<IDepartmentSettingsService>();
			_departmentSettingsService.Setup(x => x.GetRequire2FAForAdminsAsync(DepartmentId)).ReturnsAsync(0);

			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
		}

		[Test]
		public async Task downstream_exception_should_propagate_without_rerunning_the_pipeline()
		{
			var context = CreateContext();
			var invocations = 0;
			var middleware = new Require2FAEnrollmentMiddleware(_ =>
			{
				invocations++;
				throw new OperationCanceledException("client went away");
			});

			var act = () => middleware.InvokeAsync(context);

			await act.Should().ThrowAsync<OperationCanceledException>();
			invocations.Should().Be(1);
		}

		[Test]
		public async Task downstream_exception_should_propagate_when_user_has_no_department()
		{
			_departmentsService.Setup(x => x.GetDepartmentByUserIdAsync(UserId, false)).ReturnsAsync((Department)null);
			var context = CreateContext();
			var invocations = 0;
			var middleware = new Require2FAEnrollmentMiddleware(_ =>
			{
				invocations++;
				throw new InvalidOperationException("downstream failure");
			});

			var act = () => middleware.InvokeAsync(context);

			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("downstream failure");
			invocations.Should().Be(1);
		}

		[Test]
		public async Task enrollment_check_failure_should_fail_open_and_run_the_pipeline_once()
		{
			_departmentSettingsService.Setup(x => x.GetRequire2FAForAdminsAsync(DepartmentId))
				.ThrowsAsync(new InvalidOperationException("settings unavailable"));
			var context = CreateContext();
			var invocations = 0;
			var middleware = new Require2FAEnrollmentMiddleware(_ =>
			{
				invocations++;
				return Task.CompletedTask;
			});

			await middleware.InvokeAsync(context);

			invocations.Should().Be(1);
			context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
		}

		[Test]
		public async Task unenrolled_in_scope_admin_should_be_redirected_to_enrollment()
		{
			_departmentSettingsService.Setup(x => x.GetRequire2FAForAdminsAsync(DepartmentId)).ReturnsAsync(1);
			var context = CreateContext();
			var invocations = 0;
			var middleware = new Require2FAEnrollmentMiddleware(_ =>
			{
				invocations++;
				return Task.CompletedTask;
			});

			await middleware.InvokeAsync(context);

			invocations.Should().Be(0);
			context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
			context.Response.Headers.Location.ToString().Should().Be("/User/TwoFactor/Enable2FA?enforced=1");
		}

		private DefaultHttpContext CreateContext()
		{
			var services = new ServiceCollection();
			services.AddSingleton(_userManager.Object);
			services.AddSingleton(_departmentsService.Object);
			services.AddSingleton(_departmentSettingsService.Object);
			services.AddSingleton(_departmentGroupsService.Object);

			var context = new DefaultHttpContext
			{
				RequestServices = services.BuildServiceProvider(),
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, UserId) }, "Test"))
			};
			context.Request.Path = "/api/web-bff/api/v4/Chat/GetPresence";
			return context;
		}
	}
}
