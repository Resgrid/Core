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
		private Mock<IMfaPolicyService> _mfaPolicy;

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
			_mfaPolicy = new Mock<IMfaPolicyService>();
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

		[Test]
		public async Task unenrolled_in_scope_admin_can_reach_password_reauthentication()
		{
			// Starting enrollment can require confirming the password; redirecting that page back to enrollment would loop.
			_departmentSettingsService.Setup(x => x.GetRequire2FAForAdminsAsync(DepartmentId)).ReturnsAsync(1);
			var context = CreateContext();
			context.Request.Path = "/User/AccountSecurity/Reauthenticate";
			var invocations = 0;
			var middleware = new Require2FAEnrollmentMiddleware(_ =>
			{
				invocations++;
				return Task.CompletedTask;
			});

			await middleware.InvokeAsync(context);

			invocations.Should().Be(1);
			context.Response.Headers.Location.ToString().Should().BeEmpty();
		}

		[TestCase("/Account/SsoSessionBegin")]
		[TestCase("/Account/SsoReturn")]
		[TestCase("/Account/SsoUnlockBegin")]
		[TestCase("/SharedSession/Locked")]
		[TestCase("/SharedSession/EndShift")]
		public async Task unenrolled_in_scope_admin_can_reauthenticate_through_the_identity_provider(string path)
		{
			// A member who signs in through the department's identity provider reauthenticates there before enrolling; stopping the
			// round trip on its way out or back would leave the reauthentication page unable to finish.
			_departmentSettingsService.Setup(x => x.GetRequire2FAForAdminsAsync(DepartmentId)).ReturnsAsync(1);
			var context = CreateContext();
			context.Request.Path = path;
			var invocations = 0;
			var middleware = new Require2FAEnrollmentMiddleware(_ =>
			{
				invocations++;
				return Task.CompletedTask;
			});

			await middleware.InvokeAsync(context);

			invocations.Should().Be(1);
			context.Response.Headers.Location.ToString().Should().BeEmpty();
		}

		private async Task<(DefaultHttpContext Context, int Invocations)> InvokeAsync()
		{
			var context = CreateContext();
			var invocations = 0;
			await new Require2FAEnrollmentMiddleware(_ => { invocations++; return Task.CompletedTask; }).InvokeAsync(context);
			return (context, invocations);
		}

		[Test]
		public async Task department_require_mfa_confines_an_unenrolled_member_to_enrollment()
		{
			// Not an administrator, so Require2FAForAdmins would never apply; RequireMfa covers every member.
			_departmentsService.Setup(x => x.GetDepartmentByUserIdAsync(UserId, false))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "someone-else" });
			_mfaPolicy.Setup(x => x.IsRequireMfaEnforcedAsync(DepartmentId, It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(true);

			var (context, invocations) = await InvokeAsync();

			invocations.Should().Be(0, "the Web API bridge is blocked too");
			context.Response.Headers.Location.ToString().Should().Be("/User/TwoFactor/Enable2FA?enforced=1");
		}

		[Test]
		public async Task department_require_mfa_off_or_gated_leaves_a_member_alone()
		{
			_departmentsService.Setup(x => x.GetDepartmentByUserIdAsync(UserId, false))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "someone-else" });

			var (_, invocations) = await InvokeAsync();

			invocations.Should().Be(1);
		}

		[Test]
		public async Task an_enrolled_user_is_never_redirected_and_costs_no_department_lookup()
		{
			_userManager.Setup(x => x.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(true);
			_mfaPolicy.Setup(x => x.IsRequireMfaEnforcedAsync(It.IsAny<int?>(), It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(true);

			var (_, invocations) = await InvokeAsync();

			invocations.Should().Be(1);
			_departmentsService.Verify(x => x.GetDepartmentByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
		}

		private DefaultHttpContext CreateContext()
		{
			var services = new ServiceCollection();
			services.AddSingleton(_userManager.Object);
			services.AddSingleton(_departmentsService.Object);
			services.AddSingleton(_departmentSettingsService.Object);
			services.AddSingleton(_departmentGroupsService.Object);
			services.AddSingleton(_mfaPolicy.Object);

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
