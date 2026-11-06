using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Middleware;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// A browser whose cookie outlived its session (revoked elsewhere, idled out, an older credential) is signed out and sent on its
	/// way, never left on a blank 401: a page load on the sign-in pages carries on signed out, a form posted there goes to the sign-in
	/// page, and only script and API calls get the 401 they handle. Pages elsewhere still go to LogOn with their return URL.
	/// </summary>
	[TestFixture]
	public class SessionValidationDeadSessionTests
	{
		private sealed class Outcome
		{
			public DefaultHttpContext Http { get; init; }
			public bool Reached { get; init; }
			public bool? ReachedAuthenticated { get; init; }
			public Mock<IAuthenticationService> Authentication { get; init; }

			public void SignedOut() => Authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(),
				CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<AuthenticationProperties>()), Times.Once);
		}

		private static async Task<Outcome> Send(string method, string path, string failureCode = "revoked",
			params (string Name, string Value)[] headers)
		{
			var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.NameIdentifier, "user-1"),
				new Claim(SessionClaimTypes.SessionId, "session-1"),
				new Claim(SessionClaimTypes.AuthenticationGeneration, "3")
			}, "test"));
			var authentication = new Mock<IAuthenticationService>();
			authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme))
				.ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, new AuthenticationProperties(),
					CookieAuthenticationDefaults.AuthenticationScheme)));
			var http = new DefaultHttpContext
			{
				User = principal,
				RequestServices = new ServiceCollection().AddSingleton(authentication.Object).AddLogging().BuildServiceProvider()
			};
			http.Request.Method = method;
			http.Request.Path = path;
			foreach (var (name, value) in headers)
				http.Request.Headers[name] = value;

			var sessions = new Mock<IUserSessionService>();
			sessions.Setup(s => s.ValidateAsync(It.IsAny<SessionPrincipalContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(SessionValidationResult.Invalid(failureCode));

			bool? reachedAuthenticated = null;
			await new SessionValidationMiddleware(context =>
			{
				reachedAuthenticated = context.User?.Identity?.IsAuthenticated == true;
				return Task.CompletedTask;
			}).InvokeAsync(http, sessions.Object);

			return new Outcome
			{
				Http = http, Reached = reachedAuthenticated.HasValue, ReachedAuthenticated = reachedAuthenticated, Authentication = authentication
			};
		}

		private static readonly (string, string) PageLoad = ("Sec-Fetch-Mode", "navigate");

		[TestCase("/Account/LogOn")]
		[TestCase("/account/logon")]
		[TestCase("/Account/CompleteInvite")]
		[TestCase("/Account/LoginMfa")]
		public async Task Opening_a_sign_in_page_with_a_dead_session_shows_the_page_signed_out(string path)
		{
			var outcome = await Send("GET", path, headers: PageLoad);

			outcome.Reached.Should().BeTrue("the page renders instead of a blank 401");
			outcome.ReachedAuthenticated.Should().BeFalse("the dead session's user must not reach the page or its anti-forgery token");
			outcome.Http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
			outcome.SignedOut();
		}

		[Test]
		public async Task A_browser_without_fetch_metadata_is_judged_by_what_it_asks_for()
		{
			var page = await Send("GET", "/Account/LogOn", headers: ("Accept", "text/html,application/xhtml+xml"));
			page.Reached.Should().BeTrue();

			var script = await Send("GET", "/Account/LoginMfaStatus", headers: ("Accept", "application/json"));
			script.Reached.Should().BeFalse();
			script.Http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
		}

		[Test]
		public async Task A_form_posted_with_a_dead_session_goes_to_the_sign_in_page()
		{
			var outcome = await Send("POST", "/Account/LogOff", headers: PageLoad);

			outcome.Reached.Should().BeFalse("the form's anti-forgery token belongs to the dead session");
			outcome.Http.Response.StatusCode.Should().Be(StatusCodes.Status303SeeOther);
			outcome.Http.Response.Headers.Location.ToString().Should().Be("/Account/LogOn");
			outcome.SignedOut();
		}

		[Test]
		public async Task A_shared_session_past_its_shift_says_so_on_the_sign_in_page()
		{
			var outcome = await Send("POST", "/Account/LogOff", SharedSessionRules.ExpiredFailureCode, PageLoad);

			outcome.Http.Response.Headers.Location.ToString().Should().Be("/Account/LogOn?reason=shift_ended");
		}

		[TestCase("POST", "cors")]
		[TestCase("GET", "cors")]
		[TestCase("POST", "same-origin")]
		public async Task Script_calls_to_the_sign_in_pages_keep_their_401(string method, string mode)
		{
			var outcome = await Send(method, "/Account/LoginMfaApprovalStatus", headers: ("Sec-Fetch-Mode", mode));

			outcome.Reached.Should().BeFalse();
			outcome.Http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
			outcome.SignedOut();
		}

		[Test]
		public async Task Pages_elsewhere_still_go_to_the_sign_in_page_and_come_back()
		{
			var outcome = await Send("GET", "/User/Calls", headers: PageLoad);

			outcome.Reached.Should().BeFalse();
			outcome.Http.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
			outcome.Http.Response.Headers.Location.ToString().Should().Be("/Account/LogOn?returnUrl=%2FUser%2FCalls");
		}

		[Test]
		public async Task Posts_elsewhere_keep_their_401()
		{
			var outcome = await Send("POST", "/User/Calls/Save", headers: PageLoad);

			outcome.Reached.Should().BeFalse();
			outcome.Http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
		}
	}
}
