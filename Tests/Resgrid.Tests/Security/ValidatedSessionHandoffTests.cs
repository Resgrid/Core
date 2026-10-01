using System;
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

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Session validation hands the session it just validated to grant binding (plan section 8.3), in both the Web and
	/// API hosts, and hands nothing over when validation fails. The grant contexts read only that hand-off.
	/// </summary>
	[TestFixture]
	public class ValidatedSessionHandoffTests
	{
		private static readonly DateTime CookieIssued = new(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);

		private Mock<IUserSessionService> _sessions;

		[SetUp]
		public void SetUp()
		{
			_sessions = new Mock<IUserSessionService>();
			_sessions.Setup(s => s.ShouldRecordActivity(It.IsAny<UserSession>(), It.IsAny<DateTime>())).Returns(false);
		}

		private void SessionIs(bool valid) =>
			_sessions.Setup(s => s.ValidateAsync(It.IsAny<SessionPrincipalContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(valid
					? SessionValidationResult.Valid(new UserSession
					{
						UserSessionId = "session-9",
						UserId = "user-1",
						ClientApplication = (int)UserSessionClientApplication.Responder,
						AuthenticationGeneration = 4
					})
					: SessionValidationResult.Invalid("session_revoked"));

		private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(new[]
		{
			new Claim(ClaimTypes.NameIdentifier, "user-1"),
			new Claim(ClaimTypes.PrimarySid, "user-1"),
			new Claim(SessionClaimTypes.SessionId, "session-9"),
			new Claim("iat", new DateTimeOffset(CookieIssued).ToUnixTimeSeconds().ToString())
		}, "test"));

		private static ProtectedGrantSessionContext HandedOff(HttpContext context) =>
			context.Items[ProtectedGrantSessionContext.HttpItemKey] as ProtectedGrantSessionContext;

		[Test]
		public async Task The_api_hands_the_validated_session_to_grant_binding()
		{
			SessionIs(valid: true);
			var context = new DefaultHttpContext { User = Principal() };
			ProtectedGrantSessionContext seen = null;
			var middleware = new Resgrid.Web.Services.Middleware.SessionValidationMiddleware(c => { seen = HandedOff(c); return Task.CompletedTask; });

			await middleware.InvokeAsync(context, _sessions.Object);

			seen.Should().NotBeNull();
			seen.SessionId.Should().Be("session-9");
			seen.ClientApplication.Should().Be((int)UserSessionClientApplication.Responder, "the client comes from the session record, not the token");
			seen.AuthenticationGeneration.Should().Be(4);
			seen.CredentialIssuedOnUtc.Should().Be(CookieIssued);

			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(context);
			new Resgrid.Web.Services.Helpers.HttpProtectedGrantContext(accessor.Object).Session.Should().BeSameAs(seen);
		}

		[Test]
		public async Task The_api_hands_nothing_over_for_an_invalid_session()
		{
			SessionIs(valid: false);
			var context = new DefaultHttpContext { User = Principal() };
			var middleware = new Resgrid.Web.Services.Middleware.SessionValidationMiddleware(_ => Task.CompletedTask);

			await middleware.InvokeAsync(context, _sessions.Object);

			context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
			HandedOff(context).Should().BeNull();
		}

		[Test]
		public async Task The_web_hands_the_validated_session_and_cookie_issue_time_to_grant_binding()
		{
			SessionIs(valid: true);
			var principal = Principal();
			var authentication = new Mock<IAuthenticationService>();
			authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme))
				.ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal,
					new AuthenticationProperties { IssuedUtc = CookieIssued }, CookieAuthenticationDefaults.AuthenticationScheme)));
			var context = new DefaultHttpContext
			{
				User = principal,
				RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider()
			};
			ProtectedGrantSessionContext seen = null;
			var middleware = new Resgrid.Web.Middleware.SessionValidationMiddleware(c => { seen = HandedOff(c); return Task.CompletedTask; });

			await middleware.InvokeAsync(context, _sessions.Object);

			seen.Should().NotBeNull();
			seen.SessionId.Should().Be("session-9");
			seen.CredentialIssuedOnUtc.Should().Be(CookieIssued);

			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(context);
			new Resgrid.Web.Helpers.HttpProtectedGrantContext(accessor.Object).Session.Should().BeSameAs(seen);
		}

		[Test]
		public void A_workload_request_has_no_session_even_if_one_was_handed_off()
		{
			var context = new DefaultHttpContext();
			context.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext { SessionId = "session-9" };
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(context);

			new Resgrid.Web.Services.Helpers.HttpProtectedGrantContext(accessor.Object).Session.Should().BeNull();
			new Resgrid.Web.Helpers.HttpProtectedGrantContext(accessor.Object).Session.Should().BeNull();
		}
	}
}
