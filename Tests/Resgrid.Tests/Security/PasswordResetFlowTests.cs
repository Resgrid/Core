using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Security.Live;
using Resgrid.Web.Middleware;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using WebAccountController = Resgrid.Web.Controllers.AccountController;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The emailed reset link from click to new password: it works whatever sign-in the browser already holds, its cookie lasts as
	/// long as its grant, a refused link never leaves an earlier grant (perhaps someone else's) behind, and the form names the account.
	/// </summary>
	[TestFixture]
	public class PasswordResetFlowTests
	{
		private const string GrantCookie = ".Resgrid.PasswordRecovery";
		private const string Token = "abcdefghijklmnopqrstuvwxyz0123456789_-ABCDEFGH";
		private const string Address = "203.0.113.7";
		private const int Dept = 7;

		private Mock<IPasswordRecoveryService> _recovery;
		private Mock<UserManager<IdentityUser>> _users;
		private Mock<IDepartmentsService> _departments;
		private Mock<IExternalIdentityLinkService> _links;
		private IdentityUser _user;
		private string _billingUrl, _backendKey;

		[SetUp]
		public void SetUp()
		{
			(_billingUrl, _backendKey) = (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey);
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.test";
			ApiConfig.BackendInternalApikey = "test-key";

			_user = new IdentityUser
			{
				Id = "user-1", UserName = "bsmith", Email = "bsmith@example.test", EmailConfirmed = true,
				AuthenticationGeneration = 4, SecurityStamp = "stamp-1"
			};
			_recovery = new Mock<IPasswordRecoveryService>();
			_users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			_users.Setup(u => u.FindByIdAsync(_user.Id)).ReturnsAsync(_user);
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentForUserAsync(_user.UserName)).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Station 12" });
			_links = new Mock<IExternalIdentityLinkService>();
			_links.Setup(l => l.GetSsoManagementStateAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new SsoManagementState());
		}

		[TearDown]
		public void TearDown() => (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey) = (_billingUrl, _backendKey);

		private PasswordRecoveryRequest Grant(DateTime expiresOn, string securityStamp = "stamp-1") => new PasswordRecoveryRequest
		{
			UserId = _user.Id,
			Email = _user.Email,
			AuthenticationGeneration = _user.AuthenticationGeneration,
			SecurityStampHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp))).ToLowerInvariant(),
			CreatedOn = DateTime.UtcNow,
			ExpiresOn = expiresOn
		};

		private WebAccountController Controller(string grantCookie = null)
		{
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(x => x.GetEffectiveMinPasswordLengthAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(10);

			var http = new DefaultHttpContext();
			http.Connection.RemoteIpAddress = IPAddress.Parse(Address);
			if (grantCookie != null)
				http.Request.Headers.Cookie = $"{GrantCookie}={grantCookie}";

			return new WebAccountController(userManager: _users.Object, signInManager: null, departmentsService: _departments.Object,
				usersService: new Mock<IUsersService>().Object, emailService: null, invitesService: null, userProfileService: null,
				subscriptionsService: null, affiliateService: null, eventAggregator: null,
				systemAuditsService: null, serviceProvider: null, departmentSsoService: sso.Object, secLocalizer: null,
				userSessionService: null, externalIdentityLinkService: _links.Object, passwordRecoveryService: _recovery.Object,
				limitsService: new Mock<ILimitsService>().Object,
				mfaEvidenceService: null, securityNotices: null, mfaActivity: null, loginTransactions: null, passkeys: null, approvals: null,
				mfaPolicy: null, twoFactorLocalizer: null, ssoBroker: null, ssoReturnTargets: null, dataProtection: null, adpStepUp: null, mfaState: null,
				userStore: null, recoveries: null, passkeyRows: null, challenges: null, approvalRows: null, sharedSessions: null, importActivation: null,
				cacheProvider: new InMemoryCacheProvider(), loginThrottle: null, formLimits: new PublicFormRateLimiter(new InMemoryCacheProvider()))
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		private static string GrantCookieHeader(WebAccountController controller) =>
			controller.Response.Headers.SetCookie.FirstOrDefault(c => c.StartsWith(GrantCookie + "=", StringComparison.Ordinal));

		// ---- The browser's own sign-in --------------------------------------------------------------------------------

		private static readonly string[] RecoveryActions =
			{ "ForgotPassword", "ForgotPasswordConfirmation", "ResetPassword", "BeginPasswordReset" };

		[Test]
		public void Every_password_recovery_action_runs_signed_out()
		{
			var actions = typeof(WebAccountController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
				.Where(m => RecoveryActions.Contains(m.Name))
				.ToList();
			actions.Should().HaveCount(6, "ForgotPassword and ResetPassword each have a GET and a POST");

			// The site routes with UseMvc, so the middlewares see paths, not endpoints: each action's conventional route must match.
			foreach (var action in actions)
			{
				SignedOutFlow.Matches($"/Account/{action.Name}").Should().BeTrue(action.Name);
				SignedOutFlow.Matches($"/account/{action.Name.ToLowerInvariant()}").Should().BeTrue("routing ignores letter case");
			}
		}

		[TestCase("/Account/LogOn")]
		[TestCase("/Account/LoginMfa")]
		[TestCase("/Account/ResetPasswordElsewhere")]
		[TestCase("/User/Profile/ResetPasswordForUser")]
		[TestCase("/")]
		public void Only_the_recovery_pages_run_signed_out(string path) =>
			SignedOutFlow.Matches(path).Should().BeFalse();

		private static DefaultHttpContext SignedInRequest(string method, string path)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.NameIdentifier, "someone-else"),
					new Claim(SessionClaimTypes.SessionId, "session-1"),
					new Claim(SessionClaimTypes.AuthenticationGeneration, "2")
				}, "test"))
			};
			var authentication = new Mock<IAuthenticationService>();
			authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme))
				.ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(http.User, new AuthenticationProperties(),
					CookieAuthenticationDefaults.AuthenticationScheme)));
			http.RequestServices = new ServiceCollection().AddSingleton(authentication.Object).AddLogging().BuildServiceProvider();
			http.Request.Method = method;
			http.Request.Path = path;
			return http;
		}

		[Test]
		public async Task A_reset_link_posted_from_a_browser_holding_a_dead_session_reaches_the_reset_page()
		{
			// The session behind the cookie was revoked or idled out while the browser stayed open.
			var sessions = new Mock<IUserSessionService>();
			sessions.Setup(s => s.ValidateAsync(It.IsAny<SessionPrincipalContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(SessionValidationResult.Invalid("revoked"));
			var http = SignedInRequest("POST", "/Account/BeginPasswordReset");
			var reached = false;

			await new SessionValidationMiddleware(context =>
			{
				reached = true;
				context.User.Identity!.IsAuthenticated.Should().BeFalse(
					"the anti-forgery check and the sign-in policy middlewares after this must see the anonymous user the page was served to");
				return Task.CompletedTask;
			}).InvokeAsync(http, sessions.Object);

			reached.Should().BeTrue();
			http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
			sessions.Verify(s => s.ValidateAsync(It.IsAny<SessionPrincipalContext>(), It.IsAny<CancellationToken>()), Times.Never,
				"a signed-out flow does not depend on the browser's session at all");
		}

		[Test]
		public async Task Outside_the_recovery_flow_a_dead_session_is_still_refused()
		{
			var sessions = new Mock<IUserSessionService>();
			sessions.Setup(s => s.ValidateAsync(It.IsAny<SessionPrincipalContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(SessionValidationResult.Invalid("revoked"));
			var http = SignedInRequest("POST", "/Account/SomethingElse");
			var reached = false;

			await new SessionValidationMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(http, sessions.Object);

			reached.Should().BeFalse();
			http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
		}

		// ---- Opening the link -----------------------------------------------------------------------------------------

		[TestCase(Token)]
		[TestCase("not a token")]
		public async Task A_refused_link_clears_an_earlier_grant_instead_of_resetting_that_account(string presented)
		{
			// An earlier, unfinished reset on this (perhaps shared) computer left its grant cookie behind.
			_recovery.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasswordRecoveryLookupResult.NotFound());
			var controller = Controller(grantCookie: "earlier-grant-belonging-to-someone-else");

			var result = await controller.BeginPasswordReset(presented, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ResetPassword");
			var cookie = GrantCookieHeader(controller);
			cookie.Should().NotBeNull("the earlier grant must be removed, or the next page would offer to reset its account");
			cookie.Should().StartWith(GrantCookie + "=;");
			cookie.Should().Contain("expires=Thu, 01 Jan 1970");
			cookie.Should().Contain("path=/Account/ResetPassword");
		}

		[Test]
		public async Task The_grant_cookie_lasts_exactly_as_long_as_the_grant()
		{
			// An administrator's link lasts a day; the cookie must not die at a shorter, hard-coded lifetime.
			var expiresOn = DateTime.UtcNow.Date.AddDays(1).AddHours(14).AddMinutes(12);
			_recovery.Setup(r => r.GetAsync(Token, It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasswordRecoveryLookupResult.ForRequest(Grant(expiresOn)));
			var controller = Controller();

			var result = await controller.BeginPasswordReset(Token, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ResetPassword");
			var cookie = GrantCookieHeader(controller);
			cookie.Should().StartWith($"{GrantCookie}={Token};");
			cookie.Should().Contain($"expires={expiresOn:R}");
			cookie.Should().Contain("path=/Account/ResetPassword");
			cookie.Should().Contain("secure").And.Contain("samesite=strict").And.Contain("httponly");
		}

		// ---- The form --------------------------------------------------------------------------------------------------

		[Test]
		public async Task The_reset_form_names_the_account_it_resets()
		{
			_recovery.Setup(r => r.GetAsync(Token, It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasswordRecoveryLookupResult.ForRequest(Grant(DateTime.UtcNow.AddHours(1))));

			var result = await Controller(grantCookie: Token).ResetPassword(CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ResetPasswordViewModel>().Subject;
			model.InvalidOrExpired.Should().BeFalse();
			model.UserName.Should().Be("bsmith", "the sign-in page asks for the username, which the person may also have forgotten");
			model.MinPasswordLength.Should().Be(10);
		}

		[Test]
		public async Task A_grant_overtaken_by_a_password_change_is_refused_and_its_cookie_cleared()
		{
			_recovery.Setup(r => r.GetAsync(Token, It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasswordRecoveryLookupResult.ForRequest(Grant(DateTime.UtcNow.AddHours(1), securityStamp: "stamp-before-the-change")));
			var controller = Controller(grantCookie: Token);

			var result = await controller.ResetPassword(CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ResetPasswordViewModel>().Subject;
			model.InvalidOrExpired.Should().BeTrue();
			model.UserName.Should().BeNull("a refused link reveals nothing about its account");
			GrantCookieHeader(controller).Should().StartWith(GrantCookie + "=;");
		}
	}
}
