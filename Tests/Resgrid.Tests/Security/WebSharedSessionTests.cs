using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Controllers;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Core Web on a shared workstation (passkey plan sections 10.5 and 12.5; workbook section 12, slice 25): Web session
	/// validation over the real session and shared-session services, and the lock screen, Lock, End shift and unlock.
	/// </summary>
	[TestFixture]
	public class WebSharedSessionTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string Code = "246810";

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
		}

		private Clock _clock;
		private InMemoryUserSessionsRepository _rows;
		private InMemoryMfaEvidenceRepository _evidenceRows;
		private DepartmentSecurityPolicy _policy;
		private IdentityUser _user;
		private List<SystemAudit> _audited;
		private UserSessionService _sessions;
		private SharedSessionService _shared;
		private MfaPolicyService _policyService;
		private Mock<UserManager<IdentityUser>> _users;
		private Mock<SignInManager<IdentityUser>> _signIn;
		private Mock<IPasskeyService> _passkeys;
		private Mock<IMfaApprovalService> _approvals;
		private Mock<IDepartmentSsoService> _sso;
		private bool _totpEnrolled;
		private int _failedCodes;
		private long _attempts;
		private bool _tracking;
		private bool _sharedGate;

		[SetUp]
		public void SetUp()
		{
			(_tracking, _sharedGate) = (SessionSecurityConfig.TrackingEnabled, PasskeyConfig.SharedDeviceModeEnabled);
			SessionSecurityConfig.TrackingEnabled = true;
			PasskeyConfig.SharedDeviceModeEnabled = true;
			_clock = new Clock();
			_rows = new InMemoryUserSessionsRepository();
			_evidenceRows = new InMemoryMfaEvidenceRepository();
			_policy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId };
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_audited = new List<SystemAudit>();
			_totpEnrolled = true;
			_failedCodes = 0;
			_attempts = 0;

			_sso = new Mock<IDepartmentSsoService>();
			_sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
			_sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DepartmentSsoConfig>());
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(UserId, DepartmentId, true)).ReturnsAsync(new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId });
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(true);
			gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			var audits = new Mock<ISystemAuditsService>();
			audits.Setup(a => a.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()))
				.Callback((SystemAudit audit, CancellationToken _) => _audited.Add(audit))
				.ReturnsAsync((SystemAudit audit, CancellationToken _) => audit);

			_sessions = new UserSessionService(_rows, identity.Object, Mock.Of<IIdentityRepository>(), departments.Object, _sso.Object,
				new ClientSessionMetadataParser(), Mock.Of<IIpLocationProvider>(), gates.Object, audits.Object, Mock.Of<ISessionEventPublisher>(), _clock);
			var evidence = new MfaEvidenceService(_evidenceRows, new InMemoryUserMfaStateRepository(), Mock.Of<IUserPasskeyRepository>(), _rows,
				new InMemoryMfaActivityRepository(), _clock);
			_shared = new SharedSessionService(_rows, _sessions, evidence, _sso.Object, audits.Object, Mock.Of<IMfaActivityService>(), _clock);
			_policyService = new MfaPolicyService(_sso.Object, new InMemoryUserMfaStateRepository(), gates.Object);

			_users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			_users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(() => _user);
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(() => _totpEnrolled);
			_users.Setup(m => m.IsLockedOutAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
			_users.Setup(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string _, string code) => code == Code);
			_users.Setup(m => m.AccessFailedAsync(It.IsAny<IdentityUser>())).Callback(() => _failedCodes++).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			_signIn = new Mock<SignInManager<IdentityUser>>(_users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(),
				null, null, null, null);
			_signIn.Setup(s => s.SignOutAsync()).Returns(Task.CompletedTask);
			_passkeys = new Mock<IPasskeyService>();
			_approvals = new Mock<IMfaApprovalService>();
		}

		[TearDown]
		public void TearDown()
		{
			SessionSecurityConfig.TrackingEnabled = _tracking;
			PasskeyConfig.SharedDeviceModeEnabled = _sharedGate;
		}

		private UserSession Seed(bool shared = true)
		{
			var session = new UserSession
			{
				UserSessionId = Guid.NewGuid().ToString("N"), UserId = UserId, DepartmentId = DepartmentId, AuthenticationGeneration = 4,
				State = (int)UserSessionState.Active, ClientApplication = (int)UserSessionClientApplication.Web, DeviceName = "Desk 2",
				AuthenticationMethod = (int)UserSessionAuthenticationMethod.LocalPassword, CreatedOn = _clock.Now, LastActiveOn = _clock.Now,
				ExpiresOn = _clock.Now.AddHours(12), SharedMode = shared, SharedModeSource = shared ? (int)SharedModeSource.InstallationRequested : 0,
				SharedIdleLockMinutes = shared ? 5 : null, LastOperatorActivityOn = shared ? _clock.Now : null
			};
			_rows.Add(session);
			return session;
		}

		/// <summary>The row as the next request reads it.</summary>
		private async Task<UserSession> Current(UserSession session) => await _rows.GetByIdAsync(session.UserSessionId);

		private async Task<UserSession> Locked(UserSession session)
		{
			(await _shared.LockAsync(await Current(session), new SharedSessionRequestInfo())).Succeeded.Should().BeTrue();
			return await Current(session);
		}

		private static IUrlHelper Urls()
		{
			var urls = new Mock<IUrlHelper>();
			urls.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns((UrlActionContext c) => "/" + c.Controller + "/" + c.Action);
			urls.Setup(u => u.IsLocalUrl(It.IsAny<string>())).Returns((string url) => url != null && url.StartsWith("/") && !url.StartsWith("//") && !url.StartsWith("/\\"));
			return urls.Object;
		}

		private (SharedSessionController Controller, DefaultHttpContext Http, Mock<IAuthenticationService> Authentication) Station(UserSession session,
			string workstation = null)
		{
			var authentication = new Mock<IAuthenticationService>();
			var services = new ServiceCollection();
			services.AddSingleton(authentication.Object);
			var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "user1") }, "test"));
			if (session != null)
				http.Items[WebSharedSession.SessionItemKey] = session;
			if (workstation != null)
				http.Request.Headers["Cookie"] = WebSharedSession.WorkstationCookie + "=" + Uri.EscapeDataString("v1:" + workstation);

			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(() => Interlocked.Increment(ref _attempts));
			var broker = new Mock<ISsoBrokerService>();

			var controller = new SharedSessionController(_shared, _users.Object, _signIn.Object, _policyService, _passkeys.Object, _approvals.Object, cache.Object,
				_sso.Object, broker.Object, Mock.Of<ISsoReturnTargetRegistry>(), localizer.Object, _clock)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>()),
				Url = Urls()
			};
			return (controller, http, authentication);
		}

		private static JsonElement Json(IActionResult result) =>
			JsonSerializer.SerializeToElement(result.Should().BeOfType<JsonResult>().Subject.Value);

		// ---- Web session validation ------------------------------------------------------------------------------------

		private async Task<(DefaultHttpContext Http, bool Reached, Mock<IAuthenticationService> Authentication)> Request(UserSession session, string method,
			string path, params (string Name, string Value)[] headers)
		{
			var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.NameIdentifier, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
				new Claim(SessionClaimTypes.SessionId, session.UserSessionId), new Claim(SessionClaimTypes.AuthenticationGeneration, "4")
			}, "test"));
			var authentication = new Mock<IAuthenticationService>();
			authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme))
				.ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = _clock.Now },
					CookieAuthenticationDefaults.AuthenticationScheme)));
			var http = new DefaultHttpContext
			{
				User = principal,
				RequestServices = new ServiceCollection().AddSingleton(authentication.Object).AddLogging().BuildServiceProvider()
			};
			http.Request.Method = method;
			http.Request.Path = path;
			http.Response.Body = new System.IO.MemoryStream();
			foreach (var (name, value) in headers)
				http.Request.Headers[name] = value;

			var reached = false;
			await new Resgrid.Web.Middleware.SessionValidationMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(http, _sessions);
			return (http, reached, authentication);
		}

		private static string Body(HttpContext http)
		{
			http.Response.Body.Position = 0;
			return new System.IO.StreamReader(http.Response.Body).ReadToEnd();
		}

		[Test]
		public async Task A_locked_workstation_session_keeps_its_cookie_and_reaches_only_its_lock_screen()
		{
			var session = await Locked(Seed());

			var page = await Request(session, "GET", "/User/Calls", ("Sec-Fetch-Mode", "navigate"));
			page.Reached.Should().BeFalse();
			page.Http.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
			page.Http.Response.Headers.Location.ToString().Should().Be("/SharedSession/Locked?returnUrl=%2FUser%2FCalls");
			page.Authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()), Times.Never,
				"the same operator unlocks this session");

			var call = await Request(session, "POST", "/User/Calls/Save", ("X-Requested-With", "XMLHttpRequest"));
			call.Reached.Should().BeFalse();
			call.Http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
			using (var body = JsonDocument.Parse(Body(call.Http)))
			{
				body.RootElement.GetProperty("error").GetString().Should().Be("shared_session_locked");
				body.RootElement.GetProperty("lock_version").GetInt64().Should().Be(session.LockVersion);
			}

			var fetch = await Request(session, "GET", "/User/Calls/List", ("Sec-Fetch-Mode", "cors"));
			fetch.Reached.Should().BeFalse();
			fetch.Http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized, "only a page load goes to the lock screen");

			foreach (var (method, path) in new[]
			{
				("GET", "/SharedSession/Locked"), ("GET", "/sharedsession/status"), ("POST", "/SharedSession/Unlock"), ("POST", "/SharedSession/UnlockPasskey"),
				("POST", "/SharedSession/UnlockApprovalComplete"), ("POST", "/SharedSession/EndShift"), ("POST", "/SharedSession/Lock"),
				("POST", "/Account/SsoUnlockBegin"), ("POST", "/Account/SsoReturn")
			})
			{
				var allowed = await Request(session, method, path);
				allowed.Reached.Should().BeTrue(path);
				WebSharedSession.SessionOf(allowed.Http).Should().Match<UserSession>(s => s.IsLocked && s.UserSessionId == session.UserSessionId, path);
				allowed.Http.Items.ContainsKey(ProtectedGrantSessionContext.HttpItemKey).Should().BeFalse("a locked session gets no grant context");
			}

			foreach (var path in new[] { "/SharedSession/Workstation", "/Account/SsoSessionBegin", "/User/Calls/Unlock", "/SharedSession/Locked/extra" })
				(await Request(session, "POST", path)).Reached.Should().BeFalse(path);
		}

		[Test]
		public async Task A_shared_session_past_its_shift_is_signed_out_and_the_sign_in_page_says_why()
		{
			var session = Seed();
			_clock.Now = _clock.Now.AddHours(12).AddMinutes(1);

			var page = await Request(session, "GET", "/User/Calls", ("Sec-Fetch-Mode", "navigate"));
			page.Reached.Should().BeFalse();
			page.Http.Response.Headers.Location.ToString().Should().Be("/Account/LogOn?returnUrl=%2FUser%2FCalls&reason=shift_ended");
			page.Authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme,
				It.IsAny<AuthenticationProperties>()), Times.Once);
		}

		[Test]
		public async Task Only_what_the_operator_does_moves_the_idle_deadline_and_the_server_locks_when_it_passes()
		{
			var session = Seed();
			var start = _clock.Now;

			_clock.Now = start.AddMinutes(1);
			var polled = await Request(session, "GET", "/User/Calls/List", ("Sec-Fetch-Mode", "cors"), ("Sec-Fetch-User", "?0"));
			polled.Reached.Should().BeTrue();
			WebSharedSession.SessionOf(polled.Http).UserSessionId.Should().Be(session.UserSessionId);
			(await Current(session)).LastOperatorActivityOn.Should().Be(start, "polling is not the operator");

			_clock.Now = start.AddMinutes(2);
			var navigated = await Request(session, "GET", "/User/Calls", ("Sec-Fetch-Mode", "navigate"), ("Sec-Fetch-User", "?1"));
			(await Current(session)).LastOperatorActivityOn.Should().Be(start.AddMinutes(2), "a navigation the operator made");
			WebSharedSession.SessionOf(navigated.Http).LastOperatorActivityOn.Should().Be(start.AddMinutes(2),
				"the same request's status shows the moved deadline");

			_clock.Now = start.AddMinutes(3);
			await Request(session, "GET", "/SharedSession/Status", (SharedSessionRules.ActivityHeader, "1"));
			(await Current(session)).LastOperatorActivityOn.Should().Be(start.AddMinutes(3), "input the page reported");

			_clock.Now = start.AddMinutes(3).AddSeconds(10);
			await Request(session, "GET", "/SharedSession/Status", (SharedSessionRules.ActivityHeader, "1"));
			(await Current(session)).LastOperatorActivityOn.Should().Be(start.AddMinutes(3), "written at most every 30 seconds");

			_clock.Now = start.AddMinutes(8).AddSeconds(1);
			var late = await Request(session, "GET", "/User/Calls", ("Sec-Fetch-Mode", "navigate"), ("Sec-Fetch-User", "?1"));
			late.Http.Response.Headers.Location.ToString().Should().StartWith(WebSharedSession.LockedPath, "the idle deadline passed before this request");
			var row = await Current(session);
			row.IsLocked.Should().BeTrue();
			row.LockReason.Should().Be((int)SharedSessionLockReason.Idle);

			var personal = Seed(shared: false);
			await Request(personal, "GET", "/User/Calls", (SharedSessionRules.ActivityHeader, "1"));
			(await Current(personal)).LastOperatorActivityOn.Should().BeNull("a personal session has no idle lock");
		}

		// ---- Status, Lock and End shift ---------------------------------------------------------------------------------

		[Test]
		public async Task Status_counts_down_in_seconds_from_now_and_says_when_the_session_is_locked()
		{
			var session = Seed();
			_clock.Now = _clock.Now.AddMinutes(1);
			var active = Json(await Station(await Current(session)).Controller.Status(CancellationToken.None));
			active.GetProperty("shared").GetBoolean().Should().BeTrue();
			active.GetProperty("locked").GetBoolean().Should().BeFalse();
			active.GetProperty("idleLocksInSeconds").GetInt32().Should().BeInRange(235, 240, "the server's own clock, not the browser's");
			active.GetProperty("shiftEndsInSeconds").GetInt32().Should().BeInRange(11 * 3600 + 58 * 60, 11 * 3600 + 59 * 60);

			var locked = Json(await Station(await Locked(session)).Controller.Status(CancellationToken.None));
			locked.GetProperty("locked").GetBoolean().Should().BeTrue();
			locked.GetProperty("lockedUrl").GetString().Should().Be(WebSharedSession.LockedPath);

			Json(await Station(await Current(Seed(shared: false))).Controller.Status(CancellationToken.None)).GetProperty("shared").GetBoolean().Should().BeFalse();
			(await Station(null).Controller.Status(CancellationToken.None)).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
		}

		[Test]
		public async Task Lock_advances_the_lock_version_and_goes_to_the_lock_screen()
		{
			var session = Seed();
			var (controller, _, _) = Station(await Current(session));

			(await controller.Lock("/User/Calls?id=7", CancellationToken.None))
				.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/SharedSession/Locked?returnUrl=%2FUser%2FCalls%3Fid%3D7");
			var row = await Current(session);
			row.IsLocked.Should().BeTrue();
			row.LockVersion.Should().Be(1);
			row.LockReason.Should().Be((int)SharedSessionLockReason.Explicit);
			_audited.Should().Contain(a => a.Type == (int)SystemAuditTypes.SharedSessionLocked && a.System == (int)SystemAuditSystems.Website);

			(await Station(await Current(session)).Controller.Lock("//evil.example/", CancellationToken.None))
				.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(WebSharedSession.LockedPath, "only a local return address is kept");
			(await Station(await Current(session)).Controller.Lock("/SharedSession/Locked", CancellationToken.None))
				.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(WebSharedSession.LockedPath, "the lock screen never returns to itself");
		}

		[Test]
		public async Task End_shift_and_switch_operator_end_the_session_sign_the_browser_out_and_clear_the_sites_data()
		{
			foreach (var (switchOperator, reason, message) in new[]
			{
				(false, UserSessionRevocationReason.ShiftEnded, "SharedShiftEndedByOperator"),
				(true, UserSessionRevocationReason.OperatorSwitched, "SharedSwitchOperatorDone")
			})
			{
				var session = await Locked(Seed());
				var (controller, http, authentication) = Station(session, workstation: "Desk 2");

				(await controller.EndShift(switchOperator, CancellationToken.None))
					.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
				var row = await Current(session);
				row.State.Should().NotBe((int)UserSessionState.Active);
				row.RevocationReason.Should().Be((int)reason);
				_signIn.Verify(s => s.SignOutAsync(), Times.AtLeastOnce);
				authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<AuthenticationProperties>()),
					Times.Once);
				http.Response.Headers["Clear-Site-Data"].ToString().Should().Be("\"cache\", \"storage\"");
				http.Response.Headers.SetCookie.ToString().Should().NotContain(WebSharedSession.WorkstationCookie, "the station keeps its setting");
				controller.TempData["LoginMfaMessage"].Should().Be(message);
				_audited.Should().Contain(a => a.Type == (int)SystemAuditTypes.SharedSessionEnded && a.System == (int)SystemAuditSystems.Website);
			}

			var personal = Station(await Current(Seed(shared: false)));
			(await personal.Controller.EndShift(false, CancellationToken.None)).Should().BeOfType<LocalRedirectResult>();
			personal.Authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()), Times.Never);
		}

		// ---- The lock screen and unlock ---------------------------------------------------------------------------------

		[Test]
		public async Task The_lock_screen_names_the_operator_and_offers_only_the_methods_they_have()
		{
			var session = await Locked(Seed());
			var (controller, http, _) = Station(session);

			var model = (SharedSessionLockedViewModel)(await controller.Locked("/User/Calls", CancellationToken.None)).Should().BeOfType<ViewResult>().Subject.Model;
			model.Operator.Should().Be("user1");
			model.InstallationLabel.Should().Be("Desk 2");
			model.LockVersion.Should().Be(1);
			model.ReturnUrl.Should().Be("/User/Calls");
			model.Methods.Should().Equal(MfaMethodNames.Totp);
			model.Unavailable.Should().BeNull();
			http.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
			http.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");

			_passkeys.Setup(p => p.HasActiveForClientAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_approvals.Setup(a => a.IsAvailableAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_policy.AllowPasskeysForLoginMfa = true;
			_policy.AllowResponderApproval = true;
			var more = (SharedSessionLockedViewModel)((ViewResult)await Station(session).Controller.Locked("/User/Calls", CancellationToken.None)).Model;
			more.Methods.Should().BeEquivalentTo(MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval);

			_totpEnrolled = false;
			var none = (SharedSessionLockedViewModel)((ViewResult)await Station(session).Controller.Locked(null, CancellationToken.None)).Model;
			none.Methods.Should().BeEmpty("passkeys and approval count only with the authenticator app behind them");
			none.Unavailable.Should().Be("SharedUnlockNeedsAuthenticator");

			(await Station(await Current(Seed())).Controller.Locked("/User/Calls", CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls", "an unlocked session has nothing to unlock");
			(await Station(null).Controller.Locked("/User/Calls", CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
		}

		[Test]
		public async Task A_code_unlocks_the_same_session_at_its_lock_version_and_a_wrong_one_counts()
		{
			var session = await Locked(Seed());
			var before = _clock.Now;
			_clock.Now = _clock.Now.AddMinutes(1);

			var wrong = Station(session);
			var again = (await wrong.Controller.Unlock("000000", 1, "/User/Calls", CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;
			again.ViewName.Should().Be("Locked");
			((SharedSessionLockedViewModel)again.Model).Error.Should().Be("InvalidCodeLogin");
			_failedCodes.Should().Be(1);
			(await Current(session)).IsLocked.Should().BeTrue();
			_audited.Should().Contain(a => a.Type == (int)SystemAuditTypes.SharedSessionUnlocked && !a.Successful && a.System == (int)SystemAuditSystems.Website);

			var stale = Station(session);
			(await stale.Controller.Unlock(Code, 0, "/User/Calls", CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be(WebSharedSession.LockedUrl("/User/Calls"));
			stale.Controller.TempData["SharedUnlockMessage"].Should().Be("SharedUnlockLockChanged");
			(await Current(session)).IsLocked.Should().BeTrue("a code for an earlier lock unlocks nothing");

			(await Station(session).Controller.Unlock(Code, 1, "/User/Calls", CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls");
			var row = await Current(session);
			row.IsLocked.Should().BeFalse();
			row.LockVersion.Should().Be(1, "unlock never advances the version");
			row.ExpiresOn.Should().Be(before.AddHours(12), "the shift end does not move");
			_evidenceRows.Rows.Should().ContainSingle(e => e.Purpose == (int)MfaEvidencePurpose.SharedUnlock && e.Method == (int)MfaEvidenceMethod.Totp);
			_audited.Should().Contain(a => a.Type == (int)SystemAuditTypes.SharedSessionUnlocked && a.Successful && a.System == (int)SystemAuditSystems.Website);

			(await Station(await Current(session)).Controller.Unlock(Code, 1, "/User/Calls", CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls", "an unlocked session is simply sent back");
		}

		[Test]
		public async Task Unlock_attempts_are_limited_per_session_and_need_an_authenticator_app()
		{
			var session = await Locked(Seed());
			for (var i = 0; i < 5; i++)
				await Station(session).Controller.Unlock("000000", 1, null, CancellationToken.None);

			var limited = (ViewResult)await Station(session).Controller.Unlock(Code, 1, null, CancellationToken.None);
			((SharedSessionLockedViewModel)limited.Model).Error.Should().Be("SharedUnlockTooManyAttempts");
			(await Current(session)).IsLocked.Should().BeTrue("even the right code waits once the limit is reached");

			_attempts = 0;
			_totpEnrolled = false;
			var noApp = (ViewResult)await Station(session).Controller.Unlock(Code, 1, null, CancellationToken.None);
			((SharedSessionLockedViewModel)noApp.Model).Error.Should().Be("SharedUnlockNeedsAuthenticator");
			(await Current(session)).IsLocked.Should().BeTrue();
		}

		[Test]
		public async Task A_session_whose_department_now_requires_sso_cannot_be_unlocked_with_a_code()
		{
			var session = await Locked(Seed());
			_policy.RequireSso = true;
			_sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new[] { new DepartmentSsoConfig { DepartmentId = DepartmentId, IsEnabled = true } });

			var model = (SharedSessionLockedViewModel)((ViewResult)await Station(session).Controller.Locked(null, CancellationToken.None)).Model;
			model.Unavailable.Should().Be("SharedUnlockNeedsSso");

			var refused = (ViewResult)await Station(session).Controller.Unlock(Code, 1, null, CancellationToken.None);
			((SharedSessionLockedViewModel)refused.Model).Error.Should().Be("SharedUnlockNeedsSso");
			(await Current(session)).IsLocked.Should().BeTrue();
		}

		[Test]
		public async Task A_web_passkey_unlocks_only_at_the_lock_version_its_prompt_was_bound_to()
		{
			var session = await Locked(Seed());
			_policy.AllowPasskeysForLoginMfa = true;
			PasskeyCaller seen = null;
			_passkeys.Setup(p => p.BeginAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.SharedDeviceUnlock, It.IsAny<CancellationToken>()))
				.Callback((PasskeyCaller c, AuthenticationChallengePurpose _, CancellationToken _) => seen = c)
				.ReturnsAsync(new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = "req-1", OptionsJson = "{\"challenge\":\"abc\"}" });

			var options = Json(await Station(session).Controller.UnlockPasskeyOptions(1, "/User/Calls", CancellationToken.None));
			options.GetProperty("success").GetBoolean().Should().BeTrue();
			options.GetProperty("requestId").GetString().Should().Be("req-1");
			seen.ClientApplication.Should().Be(UserSessionClientApplication.Web);
			seen.SessionId.Should().Be(session.UserSessionId);
			seen.SessionLockVersion.Should().Be(1);
			seen.SharedMode.Should().BeTrue();
			seen.AuditSystem.Should().Be(SystemAuditSystems.Website);

			_passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.SharedDeviceUnlock, "req-1", "bad",
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasskeyAssertionResult.Of(PasskeyOutcome.VerificationFailed));
			Json(await Station(session).Controller.UnlockPasskey("req-1", "bad", 1, "/User/Calls", CancellationToken.None))
				.GetProperty("error").GetString().Should().Be(PasskeyOutcomes.ErrorCode(PasskeyOutcome.VerificationFailed));
			_audited.Should().Contain(a => a.Type == (int)SystemAuditTypes.SharedSessionUnlocked && !a.Successful);

			Json(await Station(session).Controller.UnlockPasskey("req-1", "good", 0, "/User/Calls", CancellationToken.None))
				.GetProperty("restart").GetString().Should().Be(WebSharedSession.LockedUrl("/User/Calls"), "a stale lock version starts over");

			var verifiedAt = _clock.Now.AddSeconds(-2);
			_passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.SharedDeviceUnlock, "req-1", "good",
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyAssertionResult { Outcome = PasskeyOutcome.Succeeded, Passkey = new UserPasskey { UserPasskeyId = "pk-web", UserId = UserId },
					VerifiedOnUtc = verifiedAt });
			var done = Json(await Station(session).Controller.UnlockPasskey("req-1", "good", 1, "/User/Calls", CancellationToken.None));
			done.GetProperty("success").GetBoolean().Should().BeTrue();
			done.GetProperty("redirect").GetString().Should().Be("/User/Calls");
			(await Current(session)).IsLocked.Should().BeFalse();
			_evidenceRows.Rows.Should().ContainSingle(e => e.Purpose == (int)MfaEvidencePurpose.SharedUnlock && e.Method == (int)MfaEvidenceMethod.Passkey &&
				e.FactorReference == UserPasskey.FactorReferenceFor("pk-web"));
		}

		[Test]
		public async Task Responder_approval_unlocks_only_with_a_request_made_at_this_lock()
		{
			var session = await Locked(Seed());
			_policy.AllowResponderApproval = true;
			MfaApprovalRequester asked = null;
			_approvals.Setup(a => a.RequestAsync(It.IsAny<MfaApprovalRequester>(), It.IsAny<CancellationToken>()))
				.Callback((MfaApprovalRequester r, CancellationToken _) => asked = r)
				.ReturnsAsync(new MfaApprovalStart { Outcome = MfaApprovalOutcome.Succeeded, ApprovalRequestId = "ap-1", MatchNumber = "42", ExpiresInSeconds = 120 });

			var started = Json(await Station(session).Controller.UnlockApproval(1, "/User/Calls", CancellationToken.None));
			started.GetProperty("matchNumber").GetString().Should().Be("42");
			asked.Purpose.Should().Be(MfaApprovalPurpose.Unlock);
			asked.Kind.Should().Be(MfaApprovalRequesterKind.Session);
			asked.RequesterId.Should().Be(session.UserSessionId);
			asked.SharedMode.Should().BeTrue();
			asked.LockVersion.Should().Be(1);
			asked.ClientApplication.Should().Be(UserSessionClientApplication.Web);

			var earlier = new MfaApprovalRequest
			{
				MfaApprovalRequestId = "ap-0", UserId = UserId, Purpose = (int)MfaApprovalPurpose.Unlock, LockVersion = 0, State = (int)MfaApprovalRequestState.Approved,
				ExpiresOnUtc = DateTime.UtcNow.AddMinutes(1)
			};
			_approvals.Setup(a => a.GetForRequesterAsync("ap-0", MfaApprovalRequesterKind.Session, session.UserSessionId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, earlier));
			Json(await Station(session).Controller.UnlockApprovalStatus("ap-0", CancellationToken.None)).GetProperty("state").GetString().Should().Be("canceled");

			var forSignIn = new MfaApprovalRequest
			{
				MfaApprovalRequestId = "ap-login", UserId = UserId, Purpose = (int)MfaApprovalPurpose.Login, LockVersion = 1, State = (int)MfaApprovalRequestState.Approved,
				ExpiresOnUtc = DateTime.UtcNow.AddMinutes(1)
			};
			_approvals.Setup(a => a.GetForRequesterAsync("ap-login", MfaApprovalRequesterKind.Session, session.UserSessionId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, forSignIn));
			var notUnlock = Json(await Station(session).Controller.UnlockApprovalStatus("ap-login", CancellationToken.None));
			notUnlock.GetProperty("success").GetBoolean().Should().BeFalse("only an unlock request answers for the lock screen");
			notUnlock.GetProperty("error").GetString().Should().Be("approval_expired");
			Json(await Station(session).Controller.UnlockApprovalComplete("ap-0", 1, "/User/Calls", CancellationToken.None))
				.GetProperty("error").GetString().Should().Be("approval_expired");
			_approvals.Verify(a => a.ConsumeAsync("ap-0", It.IsAny<MfaApprovalRequesterKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
				It.IsAny<CancellationToken>()), Times.Never);

			var current = new MfaApprovalRequest
			{
				MfaApprovalRequestId = "ap-1", UserId = UserId, Purpose = (int)MfaApprovalPurpose.Unlock, LockVersion = 1, State = (int)MfaApprovalRequestState.Approved,
				ExpiresOnUtc = DateTime.UtcNow.AddMinutes(1), ApproverPasskeyId = "pk-responder", ApproverSessionId = "responder-session", DecidedOnUtc = _clock.Now
			};
			_approvals.Setup(a => a.GetForRequesterAsync("ap-1", MfaApprovalRequesterKind.Session, session.UserSessionId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, current));
			_approvals.Setup(a => a.ConsumeAsync("ap-1", MfaApprovalRequesterKind.Session, session.UserSessionId, UserId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, current));
			Json(await Station(session).Controller.UnlockApprovalStatus("ap-1", CancellationToken.None)).GetProperty("state").GetString().Should().Be("approved");
			var done = Json(await Station(session).Controller.UnlockApprovalComplete("ap-1", 1, "/User/Calls", CancellationToken.None));
			done.GetProperty("success").GetBoolean().Should().BeTrue();
			(await Current(session)).IsLocked.Should().BeFalse();
			_evidenceRows.Rows.Should().ContainSingle(e => e.Method == (int)MfaEvidenceMethod.PasskeyApproval &&
				e.FactorReference == MfaApprovalRequest.FactorReferenceFor("pk-responder", "responder-session"));
		}

		// ---- The provider's unlock rule -------------------------------------------------------------------------------------

		[Test]
		public void A_provider_round_trip_unlocks_only_this_locked_session_for_its_operator_after_the_lock()
		{
			var lockedAt = _clock.Now;
			UserSession Session() => new()
			{
				UserSessionId = "s-1", UserId = UserId, DepartmentId = DepartmentId, AuthenticationGeneration = 4, SharedMode = true, IsLocked = true,
				LockedOnUtc = lockedAt
			};
			var tested = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = "cfg", DepartmentId = DepartmentId, IsEnabled = true, FederatedMfaMappingJson = "{\"acceptAmr\":[\"mfa\"]}",
				FederatedMfaMappingVersion = 3, FederatedMfaTestedVersion = 3
			};
			SsoLoginTransaction Step() => new()
			{
				SsoLoginTransactionId = "sso-1", Purpose = (int)SsoTransactionPurpose.StepUp, DepartmentId = DepartmentId, DepartmentSsoConfigId = "cfg",
				Operation = SsoLoginTransaction.SharedUnlockOperation, SessionId = "s-1", ExpectedUserId = UserId, UserId = UserId, AuthenticationGeneration = 4,
				FederatedMfaValue = "amr:mfa", FederatedMappingVersion = 3, CreatedOnUtc = lockedAt.AddSeconds(5)
			};

			SharedSessionRules.FederatedUnlockMatches(Step(), Session(), UserId, DepartmentId, tested).Should().BeTrue();
			foreach (var (why, change) in new (string, Action<SsoLoginTransaction, UserSession>)[]
			{
				("another department", (t, _) => t.DepartmentId = 7),
				("no provider MFA", (t, _) => t.FederatedMfaValue = null),
				("another step-up", (t, _) => t.Operation = SsoLoginTransaction.LoginOperation),
				("another session", (t, _) => t.SessionId = "s-2"),
				("begun for someone else", (t, _) => t.ExpectedUserId = "user-2"),
				("signed in as someone else", (t, _) => t.UserId = "user-2"),
				("an older generation", (t, _) => t.AuthenticationGeneration = 3),
				("begun before the lock", (t, _) => t.CreatedOnUtc = lockedAt),
				("a session that never locked", (_, s) => s.LockedOnUtc = null)
			})
			{
				var step = Step();
				var session = Session();
				change(step, session);
				SharedSessionRules.FederatedUnlockMatches(step, session, UserId, DepartmentId, tested).Should().BeFalse(why);
			}

			var elsewhere = Step();
			elsewhere.DepartmentId = 7;
			var testedElsewhere = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = "cfg", DepartmentId = 7, IsEnabled = true, FederatedMfaMappingJson = tested.FederatedMfaMappingJson,
				FederatedMfaMappingVersion = 3, FederatedMfaTestedVersion = 3
			};
			SharedSessionRules.FederatedUnlockMatches(elsewhere, Session(), UserId, DepartmentId, testedElsewhere).Should().BeFalse(
				"a step-up under another department's tested mapping");
			SharedSessionRules.FederatedUnlockMatches(Step(), Session(), UserId, DepartmentId, null).Should().BeFalse("no tested mapping");
			SharedSessionRules.FederatedUnlockMatches(null, Session(), UserId, DepartmentId, tested).Should().BeFalse();
			SharedSessionRules.FederatedUnlockMatches(Step(), null, UserId, DepartmentId, tested).Should().BeFalse();
			SharedSessionRules.FederatedUnlockMatches(Step(), Session(), " ", DepartmentId, tested).Should().BeFalse();
		}

		// ---- The station's own setting ----------------------------------------------------------------------------------

		[Test]
		public void A_browser_becomes_a_shared_workstation_with_a_safe_label_and_can_stop()
		{
			var (controller, http, _) = Station(null);
			controller.Workstation(new SharedWorkstationViewModel { Shared = true, Label = "  Desk\u0007 2  " + new string('x', 80) })
				.Should().BeOfType<RedirectToActionResult>();
			var cookie = http.Response.Headers.SetCookie.Single(c => c.StartsWith(WebSharedSession.WorkstationCookie + "=", StringComparison.Ordinal));
			cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/").And.Contain("expires=");
			var value = cookie.Substring(WebSharedSession.WorkstationCookie.Length + 1).Split(';')[0];

			var read = new DefaultHttpContext();
			read.Request.Headers["Cookie"] = WebSharedSession.WorkstationCookie + "=" + value;
			var label = WebSharedSession.WorkstationLabel(read.Request);
			label.Should().StartWith("Desk 2").And.HaveLength(64, "a printable, bounded display label");
			controller.TempData["SharedWorkstationStatus"].Should().Be("SharedWorkstationSaved");

			var stop = Station(null, workstation: "Desk 2");
			stop.Controller.Workstation(new SharedWorkstationViewModel { Shared = false });
			stop.Http.Response.Headers.SetCookie.ToString().Should().StartWith(WebSharedSession.WorkstationCookie + "=;");

			PasskeyConfig.SharedDeviceModeEnabled = false;
			var off = Station(null);
			off.Controller.Workstation(new SharedWorkstationViewModel { Shared = true, Label = "Desk 2" });
			off.Http.Response.Headers.SetCookie.Should().BeEmpty("shared mode is off on this deployment");
			((SharedWorkstationViewModel)((ViewResult)off.Controller.Workstation()).Model).Available.Should().BeFalse();
		}

		[Test]
		public void Only_real_workstation_cookies_are_read()
		{
			foreach (var (value, expected) in new (string, string)[]
			{
				(Uri.EscapeDataString("v1:Desk 2"), "Desk 2"), (Uri.EscapeDataString("v1:"), ""), ("Desk", null), ("%E0%A4%A", null), ("", null)
			})
			{
				var http = new DefaultHttpContext();
				http.Request.Headers["Cookie"] = WebSharedSession.WorkstationCookie + "=" + value;
				WebSharedSession.WorkstationLabel(http.Request).Should().Be(expected, value);
			}
		}
	}
}
