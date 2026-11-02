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
	/// <summary>
	/// A session whose department requires a new password (an administrator's flag, an expired password) reaches only the change page
	/// until the password is changed: the sign-in's redirect there was the only thing that sent it, and any other address skipped it.
	/// </summary>
	[TestFixture]
	public class RequirePasswordChangeMiddlewareTests
	{
		private const string UserId = "user-1";

		private static DefaultHttpContext Context(string path, bool mustChange = true, bool signedIn = true)
		{
			var context = new DefaultHttpContext();
			context.Request.Path = path;
			context.Response.Body = new System.IO.MemoryStream();
			if (signedIn)
			{
				var claims = new System.Collections.Generic.List<Claim> { new Claim(ClaimTypes.NameIdentifier, UserId) };
				if (mustChange)
					claims.Add(new Claim(RequirePasswordChangeMiddleware.ClaimType, "42"));
				context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
			}

			return context;
		}

		private static async Task<(bool Passed, DefaultHttpContext Context)> Run(DefaultHttpContext context)
		{
			var passed = false;
			await new RequirePasswordChangeMiddleware(_ =>
			{
				passed = true;
				return Task.CompletedTask;
			}).InvokeAsync(context);
			return (passed, context);
		}

		[Test]
		public async Task A_session_that_must_change_its_password_is_sent_to_the_change_page_from_any_other_page()
		{
			var (passed, context) = await Run(Context("/User/Home/Dashboard"));

			passed.Should().BeFalse();
			context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
			context.Response.Headers.Location.ToString().Should().Be("/Account/ForcePasswordChange");
		}

		[TestCase("/Account/ForcePasswordChange")]
		[TestCase("/Account/LogOff")]
		[TestCase("/SharedSession/Lock")]
		[TestCase("/js/app/public/resgrid.submit-once.js")]
		[TestCase("/lib/jquery/dist/jquery.min.js")]
		public async Task The_change_page_signing_out_and_static_files_stay_reachable(string path)
		{
			(await Run(Context(path))).Passed.Should().BeTrue();
		}

		[Test]
		public async Task Sessions_without_a_required_change_and_anonymous_requests_are_untouched()
		{
			(await Run(Context("/User/Home/Dashboard", mustChange: false))).Passed.Should().BeTrue();
			(await Run(Context("/User/Home/Dashboard", signedIn: false))).Passed.Should().BeTrue();
		}

		[Test]
		public async Task A_script_call_is_answered_403_with_where_to_go_instead_of_the_change_page()
		{
			var context = Context("/User/Department/TopIconsArea");
			context.Request.PathBase = "/rg";
			context.Request.Headers["X-Requested-With"] = "XMLHttpRequest";

			var (passed, _) = await Run(context);

			passed.Should().BeFalse();
			context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			context.Response.Body.Position = 0;
			var body = System.Text.Json.JsonDocument.Parse(context.Response.Body).RootElement;
			body.GetProperty("error").GetString().Should().Be("password_change_required");
			body.GetProperty("redirect").GetString().Should().Be("/rg/Account/ForcePasswordChange");
		}

		[Test]
		public async Task The_change_page_is_served_when_2fa_enrollment_is_also_required_so_the_two_never_loop()
		{
			// Both apply: the password change comes first, and the enrollment middleware lets its page through.
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(new IdentityUser { Id = UserId });
			users.Setup(x => x.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByUserIdAsync(UserId, false)).ReturnsAsync(new Department { DepartmentId = 42, ManagingUserId = UserId });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(x => x.GetRequire2FAForAdminsAsync(42)).ReturnsAsync(1);
			var services = new ServiceCollection();
			services.AddSingleton(users.Object);
			services.AddSingleton(departments.Object);
			services.AddSingleton(settings.Object);
			services.AddSingleton(new Mock<IDepartmentGroupsService>().Object);

			var context = Context("/Account/ForcePasswordChange");
			context.RequestServices = services.BuildServiceProvider();
			var served = false;
			var enrollment = new Require2FAEnrollmentMiddleware(_ =>
			{
				served = true;
				return Task.CompletedTask;
			});

			await new RequirePasswordChangeMiddleware(enrollment.InvokeAsync).InvokeAsync(context);

			served.Should().BeTrue();

			// And the enrollment page itself is held for the password change, rather than the reverse.
			var enrollmentPage = Context("/User/TwoFactor/Enable2FA");
			(await Run(enrollmentPage)).Passed.Should().BeFalse();
			enrollmentPage.Response.Headers.Location.ToString().Should().Be("/Account/ForcePasswordChange");
		}
	}
}
