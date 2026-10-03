using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Models.TwoFactor;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using WebAccountController = Resgrid.Web.Controllers.AccountController;
using TwoFactorController = Resgrid.Web.Areas.User.Controllers.TwoFactorController;
using WebClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 21 (Phase 3): Web password sign-in on the restricted login transaction (plan sections 5.2,
	/// 7.1 and 7.5), and Responder approval at Web step-up (section 7.9). The real transaction service runs over an in-memory store;
	/// each request is a fresh controller with only the cookies the browser would send.
	/// </summary>
	[TestFixture, NonParallelizable]
	public partial class WebLoginMfaTransactionTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string Cookie = ".Resgrid.MfaLogin";

		private bool _apiGate, _webGate, _tracking;
		private IHttpContextAccessor _previousClaims;
		private IdentityUser _user;
		private DepartmentSecurityPolicy _policy;
		private InMemoryMfaLoginTransactionRepository _rows;
		private MfaLoginTransactionService _transactions;
		private Mock<UserManager<IdentityUser>> _users;
		private Mock<SignInManager<IdentityUser>> _signIn;
		private Mock<IPasskeyService> _passkeys;
		private Mock<IMfaApprovalService> _approvals;
		private Mock<IPasskeyFeatureGates> _gates;
		private Mock<IUserSessionService> _sessions;
		private Mock<IMfaEvidenceService> _evidence;
		private Mock<IMfaActivityService> _activity;
		private Mock<ISecurityNoticeService> _notices;
		private Mock<IExternalIdentityLinkService> _links;
		private Mock<ISystemAuditsService> _audits;
		private List<SessionIssueContext> _issued;
		private List<(MfaEvidenceKind Kind, MfaEvidenceMethod Method, DateTime At, string Reference, string SessionKey)> _recorded;
		private bool _lockedOut;
		private int _failures;

		[SetUp]
		public void SetUp()
		{
			(_apiGate, _webGate, _tracking) = (TwoFactorConfig.LoginMfaTransactionEnabled, TwoFactorConfig.WebLoginMfaTransactionEnabled, SessionSecurityConfig.TrackingEnabled);
			_sharedGate = PasskeyConfig.SharedDeviceModeEnabled;
			_grantShared = false;
			_sharedSessions = Mock.Of<ISharedSessionService>();
			_begun = null;
			_redeemedFor = null;
			TwoFactorConfig.LoginMfaTransactionEnabled = true;
			TwoFactorConfig.WebLoginMfaTransactionEnabled = true;
			SessionSecurityConfig.TrackingEnabled = true;
			_previousClaims = WebClaims._httpContextAccessor;

			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_policy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId, MfaPolicyVersion = 3 };
			_lockedOut = false;
			_failures = 0;
			_issued = new List<SessionIssueContext>();
			_recorded = new List<(MfaEvidenceKind, MfaEvidenceMethod, DateTime, string, string)>();

			_gates = new Mock<IPasskeyFeatureGates>();
			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			_gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);
			_mfaState = new InMemoryUserMfaStateRepository();
			_recoveryRows = new InMemoryFactorRecoveryTransactionRepository();
			_recoveries = new FactorRecoveryService(_recoveryRows, identity.Object, TimeProvider.System);
			_passkeyRows = new InMemoryUserPasskeyRepository();
			_challenges = new Mock<IAuthenticationChallengeService>();
			_approvalRows = new Mock<IMfaApprovalRequestRepository>();
			_userStore = new Mock<IUserStore<IdentityUser>>();
			_keyStore = _userStore.As<IUserAuthenticatorKeyStore<IdentityUser>>();
			_keyStore.Setup(s => s.SetAuthenticatorKeyAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((IdentityUser _, string key, CancellationToken _) => _activeKey = key).Returns(Task.CompletedTask);
			_tokens = new Dictionary<string, string>();
			_activeKey = null;
			_passkeys = new Mock<IPasskeyService>();
			_approvals = new Mock<IMfaApprovalService>();
			_rows = new InMemoryMfaLoginTransactionRepository();
			_policyService = new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), _gates.Object);
			_transactions = new MfaLoginTransactionService(_rows, _policyService, sso.Object, identity.Object, _passkeys.Object, _approvals.Object,
				_gates.Object, TimeProvider.System);
			_sso = sso;

			_users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			_users.Object.Options = new IdentityOptions();
			_users.Setup(m => m.FindByNameAsync("user1")).ReturnsAsync(() => _user);
			_users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(() => _user);
			_users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(() => _user);
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(true);
			_users.Setup(m => m.IsLockedOutAsync(It.IsAny<IdentityUser>())).ReturnsAsync(() => _lockedOut);
			_users.Setup(m => m.AccessFailedAsync(It.IsAny<IdentityUser>())).Callback(() => _failures++).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string _, string code) => code == "123456");
			_users.Setup(m => m.RedeemTwoFactorRecoveryCodeAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string code) => code == "RC-1111" ? IdentityResult.Success : IdentityResult.Failed());
			// Authenticator staging: the staged key lives in an authentication token until a code from it verifies.
			_users.Setup(m => m.GenerateNewAuthenticatorKey()).Returns(StagedKey);
			_users.Setup(m => m.GetEmailAsync(It.IsAny<IdentityUser>())).ReturnsAsync("user1@example.test");
			_users.Setup(m => m.SetAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
				.Callback((IdentityUser _, string provider, string name, string value) => _tokens[provider + "/" + name] = value).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.GetAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string provider, string name) => _tokens.TryGetValue(provider + "/" + name, out var value) ? value : null);
			_users.Setup(m => m.RemoveAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.Callback((IdentityUser _, string provider, string name) => _tokens.Remove(provider + "/" + name)).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.SetTwoFactorEnabledAsync(It.IsAny<IdentityUser>(), true)).Callback(() => _totpTurnedOn = true).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.GenerateNewTwoFactorRecoveryCodesAsync(It.IsAny<IdentityUser>(), It.IsAny<int>()))
				.ReturnsAsync(new[] { "new-1", "new-2" });
			_users.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			_totpTurnedOn = false;

			_signIn = new Mock<SignInManager<IdentityUser>>(_users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(),
				null, null, null, null);
			_signIn.Setup(s => s.PasswordSignInAsync("user1", "pw", true, true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.TwoFactorRequired);
			_signIn.Setup(s => s.SignOutAsync()).Returns(Task.CompletedTask);
			_signIn.Setup(s => s.CreateUserPrincipalAsync(It.IsAny<IdentityUser>()))
				.ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, UserId) }, "Identity.Application")));
			_signIn.Setup(s => s.RememberTwoFactorClientAsync(It.IsAny<IdentityUser>())).Returns(Task.CompletedTask);

			_sessions = new Mock<IUserSessionService>();
			_sessions.Setup(s => s.CreateSessionAsync(It.IsAny<SessionIssueContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SessionIssueContext c, CancellationToken _) =>
				{
					_issued.Add(c);
					// A shared session, when the server grants the request, ends at its shift ceiling (here two hours).
					var shared = c.SharedModeRequested && _grantShared;
					return new UserSession
					{
						UserSessionId = "new-session-" + _issued.Count, UserId = c.UserId, DepartmentId = c.DepartmentId, DeviceName = c.DeviceName,
						SharedMode = shared, ExpiresOn = shared ? DateTime.UtcNow.AddHours(2) : c.ExpiresOn
					};
				});
			_evidence = new Mock<IMfaEvidenceService>();
			_evidence.Setup(e => e.RecordAsync(UserId, It.IsAny<string>(), UserSessionClientApplication.Web, It.IsAny<MfaEvidenceKind>(), It.IsAny<MfaEvidenceMethod>(),
					It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), 4, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string _, string key, UserSessionClientApplication _, MfaEvidenceKind kind, MfaEvidenceMethod method, MfaEvidencePurpose _, DateTime at,
					long _, int? _, string reference, CancellationToken _) => _recorded.Add((kind, method, at, reference, key)))
				.Returns(Task.CompletedTask);
			_activity = new Mock<IMfaActivityService>();
			_notices = new Mock<ISecurityNoticeService>();
			_links = new Mock<IExternalIdentityLinkService>();
			_links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_audits = new Mock<ISystemAuditsService>();
			_broker = new Mock<ISsoBrokerService>();
			_returnTargets = new Mock<ISsoReturnTargetRegistry>();
			_adpStepUp = new Mock<IAdpStepUpService>();
			_dataProtection = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
			_evidenceService = null;
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByUserIdAsync(UserId, It.IsAny<bool>())).ReturnsAsync(() => new Department { DepartmentId = _activeDepartment });
			_departments.Setup(d => d.GetDepartmentForUserAsync("user1")).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(() => new Department { DepartmentId = DepartmentId, Code = "DEPT", ManagingUserId = _managingUserId });
			_departments.Setup(d => d.GetDepartmentMemberAsync(UserId, DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _membership);
			_activeDepartment = DepartmentId;
			_managingUserId = "someone-else";
			_membership = new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId };
		}

		private Mock<IDepartmentsService> _departments;
		/// <summary>A real evidence service for the Phase 3 matrix; the mock otherwise.</summary>
		private IMfaEvidenceService _evidenceService;
		private const string StagedKey = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
		private InMemoryUserMfaStateRepository _mfaState;
		private ISharedSessionService _sharedSessions = Mock.Of<ISharedSessionService>();
		private bool _grantShared;
		private bool _sharedGate;
		private Mock<IAuthenticationService> _lastAuthentication;
		private InMemoryFactorRecoveryTransactionRepository _recoveryRows;
		private FactorRecoveryService _recoveries;
		private InMemoryUserPasskeyRepository _passkeyRows;
		private Mock<IAuthenticationChallengeService> _challenges;
		private Mock<IMfaApprovalRequestRepository> _approvalRows;
		private Mock<IUserStore<IdentityUser>> _userStore;
		private Mock<IUserAuthenticatorKeyStore<IdentityUser>> _keyStore;
		private Dictionary<string, string> _tokens;
		private string _activeKey;
		private bool _totpTurnedOn;
		private int _activeDepartment;
		private string _managingUserId;
		private DepartmentMember _membership;
		private DefaultHttpContext _lastHttp;

		private MfaPolicyService _policyService;
		private Mock<IDepartmentSsoService> _sso;
		private Mock<ISsoBrokerService> _broker;
		private Mock<ISsoReturnTargetRegistry> _returnTargets;
		private Mock<IAdpStepUpService> _adpStepUp;
		private Microsoft.AspNetCore.DataProtection.IDataProtectionProvider _dataProtection;

		[TearDown]
		public void TearDown()
		{
			TwoFactorConfig.LoginMfaTransactionEnabled = _apiGate;
			TwoFactorConfig.WebLoginMfaTransactionEnabled = _webGate;
			SessionSecurityConfig.TrackingEnabled = _tracking;
			PasskeyConfig.SharedDeviceModeEnabled = _sharedGate;
			WebClaims._httpContextAccessor = _previousClaims;
		}

		// ---- The browser -------------------------------------------------------------------------------------------

		private static IUrlHelper Urls()
		{
			var urls = new Mock<IUrlHelper>();
			urls.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns((UrlActionContext c) =>
			{
				var area = c.Values?.GetType().GetProperty("Area")?.GetValue(c.Values) as string ?? c.Values?.GetType().GetProperty("area")?.GetValue(c.Values) as string;
				var returnUrl = c.Values?.GetType().GetProperty("returnUrl")?.GetValue(c.Values) as string;
				return (area == null ? "" : "/" + area) + "/" + (c.Controller ?? "Account") + "/" + c.Action + (returnUrl == null ? "" : "?returnUrl=" + Uri.EscapeDataString(returnUrl));
			});
			urls.Setup(u => u.IsLocalUrl(It.IsAny<string>())).Returns((string url) => url != null && url.StartsWith("/") && !url.StartsWith("//") && !url.StartsWith("/\\"));
			return urls.Object;
		}

		/// <summary>
		/// A new request from the same browser: a fresh controller that sees only the cookies the browser holds (the login transaction,
		/// the SSO round trip) and, when signed in, its session.
		/// </summary>
		private (WebAccountController Controller, DefaultHttpContext Http) Browser(string secret = null, string ssoTrip = null, bool signedIn = false,
			string workstation = null)
		{
			var services = new ServiceCollection();
			var authentication = new Mock<IAuthenticationService>();
			_lastAuthentication = authentication;
			services.AddSingleton(authentication.Object);
			var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			var cookies = new List<string>();
			if (secret != null)
				cookies.Add(Cookie + "=" + secret);
			if (ssoTrip != null)
				cookies.Add(Resgrid.Web.Helpers.WebSsoRoundTrip.CookieName + "=" + ssoTrip);
			if (workstation != null)
				cookies.Add(Resgrid.Web.Helpers.WebSharedSession.WorkstationCookie + "=" + Uri.EscapeDataString("v1:" + workstation));
			if (cookies.Count > 0)
				http.Request.Headers["Cookie"] = string.Join("; ", cookies);
			if (signedIn)
			{
				http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ClaimTypes.Name, "user1"),
					new Claim(SessionClaimTypes.SessionId, WebSession)
				}, "test"));
				http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
				{
					SessionId = WebSession, ClientApplication = (int)UserSessionClientApplication.Web, AuthenticationGeneration = 4
				};
				WebClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			}

			var departments = _departments;
			var usersService = new Mock<IUsersService>();
			usersService.Setup(u => u.DoesUserHaveAnyActiveDepartments("user1")).ReturnsAsync(true);
			_lastHttp = http;
			var twoFactor = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor>>();
			twoFactor.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			var controller = new WebAccountController(_users.Object, _signIn.Object, departments.Object, usersService.Object, null, null, null, null, null, null, null,
				_audits.Object, null, _sso.Object, null, _sessions.Object, _links.Object, null, null, _evidenceService ?? _evidence.Object, _notices.Object, _activity.Object,
				_transactions, _passkeys.Object, _approvals.Object, _policyService, twoFactor.Object, _broker.Object, _returnTargets.Object, _dataProtection,
				_adpStepUp.Object, _mfaState, _userStore.Object, _recoveries, _passkeyRows, _challenges.Object, _approvalRows.Object, _sharedSessions)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>()),
				Url = Urls()
			};
			return (controller, http);
		}

		private static string SetCookie(HttpContext http) => string.Join("\n", http.Response.Headers.SetCookie.ToArray());

		private static string SecretFrom(HttpContext http)
		{
			var header = http.Response.Headers.SetCookie.Single(c => c.StartsWith(Cookie + "=", StringComparison.Ordinal));
			return Uri.UnescapeDataString(header.Substring(Cookie.Length + 1).Split(';')[0]);
		}

		/// <summary>The password step: returns the transaction secret this browser now holds.</summary>
		private async Task<string> SignInWithPassword(string returnUrl = "/User/Calls")
		{
			var (controller, http) = Browser();
			var result = await controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, returnUrl);
			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfa");
			return SecretFrom(http);
		}

		private static JObject Body(IActionResult result) => JObject.FromObject(((JsonResult)result).Value);

		private MfaLoginTransaction Row => _rows.Rows.Single();

		// ---- Starting ------------------------------------------------------------------------------------------------

		[Test]
		public async Task With_the_web_gate_off_the_password_leads_to_the_code_page_as_before()
		{
			TwoFactorConfig.WebLoginMfaTransactionEnabled = false;
			var (controller, http) = Browser();

			var result = await controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, "/User/Calls");

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginWith2fa");
			_rows.Rows.Should().BeEmpty();
			SetCookie(http).Should().NotContain(Cookie);
		}

		[Test]
		public async Task A_password_starts_a_transaction_held_only_in_a_strict_cookie_and_drops_identitys_partial_sign_in()
		{
			var (controller, http) = Browser();

			var result = await controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, "/User/Calls");

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfa");
			_signIn.Verify(s => s.SignOutAsync(), Times.AtLeast(2), "Identity's two-factor cookie is dropped after the password too");
			var cookie = http.Response.Headers.SetCookie.Single(c => c.StartsWith(Cookie + "=", StringComparison.Ordinal)).ToLowerInvariant();
			cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/account");

			Row.UserId.Should().Be(UserId);
			Row.DepartmentId.Should().Be(DepartmentId);
			Row.ClientApplication.Should().Be((int)UserSessionClientApplication.Web);
			Row.FirstFactorMethod.Should().Be((int)MfaEvidenceMethod.Password);
			Row.SecretHash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(SecretFrom(http))), "the server keeps only the hash");
			_issued.Should().BeEmpty("no session exists until a second factor verifies");
		}

		// ---- Choosing --------------------------------------------------------------------------------------------------

		[Test]
		public async Task The_choice_offers_what_the_account_has_and_the_department_accepts()
		{
			var secret = await SignInWithPassword();
			_passkeys.Setup(p => p.HasActiveForClientAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_approvals.Setup(a => a.IsAvailableAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var model = (LoginMfaViewModel)((ViewResult)await Browser(secret).Controller.LoginMfa("/User/Calls", CancellationToken.None)).Model;
			model.Methods.Should().Equal(MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval);
			model.Preferred.Should().Be(MfaMethodNames.Totp);
			model.RecoveryAvailable.Should().BeTrue();
			model.ReturnUrl.Should().Be("/User/Calls");

			_policy.AllowPasskeysForLoginMfa = false;
			var narrowed = (LoginMfaViewModel)((ViewResult)await Browser(secret).Controller.LoginMfa((string)null, CancellationToken.None)).Model;
			narrowed.Methods.Should().Equal(new[] { MfaMethodNames.Totp }, "approval rides on the passkey switch, and the department switched passkeys off");

			var foreign = (LoginMfaViewModel)((ViewResult)await Browser(secret).Controller.LoginMfa("https://evil.example/", CancellationToken.None)).Model;
			foreign.ReturnUrl.Should().BeNull("only a local return address is kept");
		}

		[Test]
		public async Task Without_a_usable_transaction_the_sign_in_starts_again_with_the_reason()
		{
			var missing = Browser();
			var result = await missing.Controller.LoginMfa("/User/Calls", CancellationToken.None);
			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			missing.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaInvalid");

			var secret = await SignInWithPassword();
			Row.ExpiresOnUtc = DateTime.UtcNow.AddSeconds(-1);
			var expired = Browser(secret);
			(await expired.Controller.LoginMfa((string)null, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			expired.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaExpired");
			SetCookie(expired.Http).Should().Contain(Cookie + "=;", "the spent transaction is dropped from the browser");
		}

		[Test]
		public async Task A_policy_change_during_sign_in_voids_it()
		{
			var secret = await SignInWithPassword();
			_policy.MfaPolicyVersion = 4;

			var browser = Browser(secret);
			await browser.Controller.LoginMfa((string)null, CancellationToken.None);

			browser.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaPolicyChanged");
		}

		// ---- The authenticator code -----------------------------------------------------------------------------------

		[Test]
		public async Task A_code_finishes_the_sign_in_once_with_the_passwords_own_time_and_the_code_as_evidence()
		{
			var secret = await SignInWithPassword();
			var firstFactorAt = Row.FirstFactorVerifiedOnUtc;

			var browser = Browser(secret);
			var result = await browser.Controller.LoginMfa(new LoginMfaViewModel { Code = "123 456", ReturnUrl = "/User/Calls" }, CancellationToken.None);

			result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls");
			Row.TransactionState.Should().Be(MfaLoginTransactionState.Redeemed);
			_issued.Should().ContainSingle();
			_issued[0].LoginMfaMethod.Should().Be(MfaEvidenceMethod.Totp);
			_issued[0].AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.LocalPassword);
			_issued[0].ClientApplication.Should().Be(UserSessionClientApplication.Web);
			_recorded.Should().HaveCount(2);
			_recorded[0].Should().Match<(MfaEvidenceKind Kind, MfaEvidenceMethod Method, DateTime At, string Reference, string SessionKey)>(e =>
				e.Kind == MfaEvidenceKind.FirstFactor && e.Method == MfaEvidenceMethod.Password && e.At == firstFactorAt);
			_recorded[1].Kind.Should().Be(MfaEvidenceKind.SecondFactor);
			_recorded[1].Method.Should().Be(MfaEvidenceMethod.Totp);
			_recorded.Should().OnlyContain(e => e.SessionKey == "sid:new-session-1");
			SetCookie(browser.Http).Should().Contain(Cookie + "=;");
			_signIn.Verify(s => s.RememberTwoFactorClientAsync(It.IsAny<IdentityUser>()), Times.Never);

			var replay = Browser(secret);
			(await replay.Controller.LoginMfa(new LoginMfaViewModel { Code = "123456" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn", "a spent transaction never signs in twice");
			_issued.Should().ContainSingle();
		}

		[Test]
		public async Task A_wrong_code_counts_against_the_transaction_and_the_lockout_and_the_last_one_ends_the_sign_in()
		{
			var secret = await SignInWithPassword();

			var first = Browser(secret);
			(await first.Controller.LoginMfa(new LoginMfaViewModel { Code = "000000" }, CancellationToken.None)).Should().BeOfType<ViewResult>();
			first.Controller.ModelState["Code"]!.Errors.Single().ErrorMessage.Should().Be("InvalidCodeLogin");
			Row.Attempts.Should().Be(1);
			_failures.Should().Be(1);
			_activity.Verify(a => a.RecordAsync(It.Is<MfaActivityEntry>(e => !e.Successful && e.Method == MfaEvidenceMethod.Totp && e.Purpose == MfaEvidencePurpose.Login),
				It.IsAny<CancellationToken>()), Times.Once);

			for (var i = 2; i < Row.MaxAttempts; i++)
				await Browser(secret).Controller.LoginMfa(new LoginMfaViewModel { Code = "000000" }, CancellationToken.None);

			var last = Browser(secret);
			(await last.Controller.LoginMfa(new LoginMfaViewModel { Code = "000000" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			last.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaTooManyAttempts");
			(await Browser(secret).Controller.LoginMfa(new LoginMfaViewModel { Code = "123456" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn", "even the right code cannot use a spent transaction");
			_issued.Should().BeEmpty();
		}

		[Test]
		public async Task A_locked_account_starts_again_without_trying_the_code()
		{
			var secret = await SignInWithPassword();
			_lockedOut = true;

			var browser = Browser(secret);
			(await browser.Controller.LoginMfa(new LoginMfaViewModel { Code = "123456" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");

			browser.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaTooManyAttempts");
			_users.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_issued.Should().BeEmpty();
		}

		[Test]
		public async Task What_allowed_the_password_is_checked_again_before_the_session_exists()
		{
			var secret = await SignInWithPassword();
			_links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var browser = Browser(secret);
			(await browser.Controller.LoginMfa(new LoginMfaViewModel { Code = "123456" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");

			browser.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaSignInChanged");
			_issued.Should().BeEmpty();
		}

		[Test]
		public async Task Remembering_the_browser_follows_a_real_second_factor_and_a_foreign_return_address_is_dropped()
		{
			var secret = await SignInWithPassword();

			var result = await Browser(secret).Controller.LoginMfa(
				new LoginMfaViewModel { Code = "123456", RememberBrowser = true, ReturnUrl = "https://evil.example/" }, CancellationToken.None);

			result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Home/Dashboard");
			_signIn.Verify(s => s.RememberTwoFactorClientAsync(_user), Times.Once);
		}

		// ---- A passkey -----------------------------------------------------------------------------------------------

		[Test]
		public async Task A_passkey_for_the_web_finishes_the_sign_in_and_names_itself_on_the_session()
		{
			var secret = await SignInWithPassword();
			_passkeys.Setup(p => p.HasActiveForClientAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			PasskeyCaller caller = null;
			_passkeys.Setup(p => p.BeginAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.LoginSecondFactor, It.IsAny<CancellationToken>()))
				.Callback((PasskeyCaller c, AuthenticationChallengePurpose _, CancellationToken _) => caller = c)
				.ReturnsAsync(new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = "req-1", OptionsJson = "{\"challenge\":\"abc\"}" });
			var verifiedAt = DateTime.UtcNow.AddSeconds(-1);
			_passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.LoginSecondFactor, "req-1", "{\"id\":\"x\"}",
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyAssertionResult { Outcome = PasskeyOutcome.Succeeded, Passkey = new UserPasskey { UserPasskeyId = "pk-web" }, VerifiedOnUtc = verifiedAt });

			var options = Body(await Browser(secret).Controller.LoginMfaPasskeyOptions("/User/Calls", CancellationToken.None));
			options["success"]!.Value<bool>().Should().BeTrue();
			options["requestId"]!.Value<string>().Should().Be("req-1");
			caller.LoginTransactionId.Should().Be(Row.MfaLoginTransactionId);
			caller.ClientApplication.Should().Be(UserSessionClientApplication.Web);
			caller.AuditSystem.Should().Be(SystemAuditSystems.Website);
			caller.SessionId.Should().BeNull("there is no session yet");

			var done = Body(await Browser(secret).Controller.LoginMfaPasskey("req-1", "{\"id\":\"x\"}", "/User/Calls", false, CancellationToken.None));
			done["success"]!.Value<bool>().Should().BeTrue();
			done["redirect"]!.Value<string>().Should().Be("/User/Calls");
			_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.Passkey);
			_issued.Single().LoginMfaFactorReference.Should().Be(UserPasskey.FactorReferenceFor("pk-web"));
			_recorded.Last().Should().Be((MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Passkey, verifiedAt, UserPasskey.FactorReferenceFor("pk-web"), "sid:new-session-1"));
		}

		[Test]
		public async Task A_passkey_that_does_not_verify_is_a_failed_attempt_but_a_stale_request_is_not()
		{
			var secret = await SignInWithPassword();
			_passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.LoginSecondFactor, "bad", It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyAssertionResult { Outcome = PasskeyOutcome.VerificationFailed });
			_passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.LoginSecondFactor, "old", It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyAssertionResult { Outcome = PasskeyOutcome.ChallengeExpired });

			var failed = Body(await Browser(secret).Controller.LoginMfaPasskey("bad", "{}", null, false, CancellationToken.None));
			failed["success"]!.Value<bool>().Should().BeFalse();
			Row.Attempts.Should().Be(1);
			_failures.Should().Be(1);

			var stale = Body(await Browser(secret).Controller.LoginMfaPasskey("old", "{}", null, false, CancellationToken.None));
			stale["success"]!.Value<bool>().Should().BeFalse();
			stale["restart"].Should().BeNull("the user can simply try again");
			Row.Attempts.Should().Be(1, "an expired request is not a wrong answer");
			_issued.Should().BeEmpty();
		}

		[Test]
		public async Task A_method_the_department_does_not_accept_is_refused_and_a_dead_transaction_sends_the_page_back_to_sign_in()
		{
			var secret = await SignInWithPassword();
			_policy.AllowPasskeysForLoginMfa = false;

			var refused = Body(await Browser(secret).Controller.LoginMfaPasskeyOptions(null, CancellationToken.None));
			refused["error"]!.Value<string>().Should().Be("mfa_method_not_allowed");
			refused["restart"].Should().BeNull();
			_passkeys.Verify(p => p.BeginAssertionAsync(It.IsAny<PasskeyCaller>(), It.IsAny<AuthenticationChallengePurpose>(), It.IsAny<CancellationToken>()), Times.Never);
			Body(await Browser(secret).Controller.LoginMfaRequestApproval(null, CancellationToken.None))["error"]!.Value<string>().Should().Be("approval_unavailable");

			var gone = Body(await Browser("not-a-secret").Controller.LoginMfaPasskeyOptions("/User/Calls", CancellationToken.None));
			gone["success"]!.Value<bool>().Should().BeFalse();
			gone["restart"]!.Value<string>().Should().Be("/Account/LogOn?returnUrl=%2FUser%2FCalls");
		}

		// ---- Responder approval ------------------------------------------------------------------------------------------

		[Test]
		public async Task Approval_is_asked_for_this_sign_in_and_used_once_to_finish_it()
		{
			var secret = await SignInWithPassword();
			MfaApprovalRequester requester = null;
			_approvals.Setup(a => a.RequestAsync(It.IsAny<MfaApprovalRequester>(), It.IsAny<CancellationToken>()))
				.Callback((MfaApprovalRequester r, CancellationToken _) => requester = r)
				.ReturnsAsync(new MfaApprovalStart { Outcome = MfaApprovalOutcome.Succeeded, ApprovalRequestId = "ap-1", MatchNumber = "47", ExpiresInSeconds = 120 });
			var transactionId = Row.MfaLoginTransactionId;
			_approvals.Setup(a => a.GetForRequesterAsync("ap-1", MfaApprovalRequesterKind.LoginTransaction, transactionId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, new MfaApprovalRequest
				{
					State = (int)MfaApprovalRequestState.Approved, ExpiresOnUtc = DateTime.UtcNow.AddMinutes(1)
				}));
			var decidedAt = DateTime.UtcNow.AddSeconds(-3);
			_approvals.Setup(a => a.ConsumeAsync("ap-1", MfaApprovalRequesterKind.LoginTransaction, transactionId, UserId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, new MfaApprovalRequest
				{
					ApproverPasskeyId = "pk-responder", ApproverSessionId = "responder-session", DecidedOnUtc = decidedAt
				}));

			// Redemption checks again that the approving passkey and Responder session still count.
			_approvals.Setup(a => a.IsApproverValidAsync(UserId, MfaApprovalRequest.FactorReferenceFor("pk-responder", "responder-session"), 4,
				It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var started = Body(await Browser(secret).Controller.LoginMfaRequestApproval(null, CancellationToken.None));
			started["matchNumber"]!.Value<string>().Should().Be("47");
			requester.Kind.Should().Be(MfaApprovalRequesterKind.LoginTransaction);
			requester.RequesterId.Should().Be(transactionId);
			requester.ClientApplication.Should().Be(UserSessionClientApplication.Web);
			requester.Purpose.Should().Be(MfaApprovalPurpose.Login);
			requester.DepartmentId.Should().Be(DepartmentId);
			requester.AuditSystem.Should().Be(SystemAuditSystems.Website);

			Body(await Browser(secret).Controller.LoginMfaApprovalStatus("ap-1", null, CancellationToken.None))["state"]!.Value<string>().Should().Be("approved");

			var done = Body(await Browser(secret).Controller.LoginMfaCompleteApproval("ap-1", "/User/Calls", false, CancellationToken.None));
			done["redirect"]!.Value<string>().Should().Be("/User/Calls");
			var reference = MfaApprovalRequest.FactorReferenceFor("pk-responder", "responder-session");
			_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.PasskeyApproval);
			_issued.Single().LoginMfaFactorReference.Should().Be(reference);
			_recorded.Last().Should().Be((MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.PasskeyApproval, decidedAt, reference, "sid:new-session-1"));
		}

		[Test]
		public async Task An_approval_that_is_not_ready_leaves_the_sign_in_waiting()
		{
			var secret = await SignInWithPassword();
			_approvals.Setup(a => a.ConsumeAsync("ap-1", MfaApprovalRequesterKind.LoginTransaction, It.IsAny<string>(), UserId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Pending));

			var body = Body(await Browser(secret).Controller.LoginMfaCompleteApproval("ap-1", null, false, CancellationToken.None));

			body["success"]!.Value<bool>().Should().BeFalse();
			Row.TransactionState.Should().Be(MfaLoginTransactionState.Pending);
			_issued.Should().BeEmpty();
		}

		// ---- A recovery code ----------------------------------------------------------------------------------------------

		[Test]
		public async Task A_recovery_code_finishes_as_a_recovery_session_that_counts_as_no_second_factor()
		{
			var secret = await SignInWithPassword();

			var wrong = Browser(secret);
			(await wrong.Controller.LoginWithRecoveryCode(new VerifyCodeViewModel { Code = "nope" }, CancellationToken.None)).Should().BeOfType<ViewResult>();
			Row.Attempts.Should().Be(1);

			var browser = Browser(secret);
			var result = await browser.Controller.LoginWithRecoveryCode(new VerifyCodeViewModel { Code = "RC-1111", RememberBrowser = true }, CancellationToken.None);

			result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/TwoFactor/Index");
			_issued.Single().AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.Recovery);
			_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.RecoveryCode);
			_recorded.Should().NotContain(e => e.Kind == MfaEvidenceKind.SecondFactor, "a recovery code is never MFA");
			_recorded.Last().Kind.Should().Be(MfaEvidenceKind.Recovery);
			Row.IsRecovery.Should().BeTrue();
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.UserId == UserId && r.Kind == SecurityNoticeKind.RecoveryCodeUsed), It.IsAny<CancellationToken>()),
				Times.Once);
			_signIn.Verify(s => s.RememberTwoFactorClientAsync(It.IsAny<IdentityUser>()), Times.Never, "a recovery sign-in never remembers the browser");
		}

		// ---- Responder approval at Web step-up (Verify2FA) -------------------------------------------------------------

		private const string WebSession = "web-session";

		private TwoFactorController StepUp(Mock<IMfaEvidenceService> evidence = null, bool withSession = true, long lockVersion = 0)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ClaimTypes.Name, "user1"),
					new Claim(SessionClaimTypes.SessionId, WebSession)
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			if (withSession)
				http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
				{
					SessionId = WebSession, ClientApplication = (int)UserSessionClientApplication.Web, AuthenticationGeneration = 4, SessionLockVersion = lockVersion
				};
			WebClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return new TwoFactorController(_users.Object, null, _audits.Object, UrlEncoder.Default, localizer.Object, Mock.Of<IUserStore<IdentityUser>>(),
				new InMemoryUserMfaStateRepository(), Mock.Of<IUserSessionService>(), (evidence ?? _evidence).Object, _policyService, new InMemoryUserPasskeyRepository(),
				_notices.Object, _activity.Object, _passkeys.Object, _approvals.Object, _broker.Object, _returnTargets.Object, _sso.Object, _departments.Object, Mock.Of<Resgrid.Model.Providers.ICacheProvider>(), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider())
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				Url = Urls()
			};
		}

		[Test]
		public async Task Verify2FA_offers_approval_only_where_the_guarded_action_accepts_it()
		{
			_approvals.Setup(a => a.IsAvailableAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var generic = (StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA("/User/Calls", null, CancellationToken.None)).Model;
			generic.ApprovalAvailable.Should().BeTrue();
			generic.Scope.Should().Be(nameof(MfaMethodScope.Login));

			((StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA(null, "SecurityChange", CancellationToken.None)).Model).ApprovalAvailable
				.Should().BeFalse("security changes need a direct factor");
			((StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA(null, "Account", CancellationToken.None)).Model).ApprovalAvailable
				.Should().BeFalse("account factors need a direct factor");
			((StepUpVerifyViewModel)((ViewResult)await StepUp(withSession: false).Verify2FA(null, null, CancellationToken.None)).Model).ApprovalAvailable
				.Should().BeFalse("approval is asked for by a tracked session");
		}

		[Test]
		public async Task A_refused_code_keeps_the_other_choices_on_the_page()
		{
			_gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(true);
			_approvals.Setup(a => a.IsAvailableAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var again = (StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA(new StepUpVerifyViewModel { Code = "000000", Scope = "Adp", ApprovalAvailable = false },
				CancellationToken.None)).Model;

			again.ApprovalAvailable.Should().BeTrue("the choices come from the server, not the form");
			again.Scope.Should().Be(nameof(MfaMethodScope.Adp));
		}

		[Test]
		public async Task A_step_up_approval_is_asked_for_this_sessions_operation_and_becomes_its_evidence()
		{
			MfaApprovalRequester requester = null;
			_approvals.Setup(a => a.RequestAsync(It.IsAny<MfaApprovalRequester>(), It.IsAny<CancellationToken>()))
				.Callback((MfaApprovalRequester r, CancellationToken _) => requester = r)
				.ReturnsAsync(new MfaApprovalStart { Outcome = MfaApprovalOutcome.Succeeded, ApprovalRequestId = "ap-9", MatchNumber = "12", ExpiresInSeconds = 120 });
			Body(await StepUp(lockVersion: 2).Verify2FARequestApproval("Adp", CancellationToken.None))["matchNumber"]!.Value<string>().Should().Be("12");
			requester.Kind.Should().Be(MfaApprovalRequesterKind.Session);
			requester.RequesterId.Should().Be(WebSession);
			requester.Purpose.Should().Be(MfaApprovalPurpose.StepUp);
			requester.Operation.Should().Be(MfaStepUpOperations.AdpManagement);
			requester.LockVersion.Should().Be(2);
			requester.AuditSystem.Should().Be(SystemAuditSystems.Website);

			await StepUp().Verify2FARequestApproval(null, CancellationToken.None);
			requester.Operation.Should().Be(MfaStepUpOperations.SensitiveOperation, "a generic guarded action is a known operation too");

			var decidedAt = DateTime.UtcNow.AddSeconds(-2);
			_approvals.Setup(a => a.GetForRequesterAsync("ap-9", MfaApprovalRequesterKind.Session, WebSession, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, new MfaApprovalRequest
				{
					Purpose = (int)MfaApprovalPurpose.StepUp, Operation = MfaStepUpOperations.SensitiveOperation, LockVersion = 0
				}));
			_approvals.Setup(a => a.ConsumeAsync("ap-9", MfaApprovalRequesterKind.Session, WebSession, UserId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, new MfaApprovalRequest
				{
					ApproverPasskeyId = "pk-responder", ApproverSessionId = "responder-session", DecidedOnUtc = decidedAt
				}));

			var done = Body(await StepUp().Verify2FACompleteApproval("ap-9", "Login", "/User/Calls", CancellationToken.None));

			done["success"]!.Value<bool>().Should().BeTrue();
			done["redirect"]!.Value<string>().Should().Be("/User/Calls");
			_recorded.Should().ContainSingle().Which.Should().Be((MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.PasskeyApproval, decidedAt,
				MfaApprovalRequest.FactorReferenceFor("pk-responder", "responder-session"), "sid:" + WebSession));
		}

		[Test]
		public async Task A_step_up_approval_for_another_operation_or_lock_version_or_a_refusing_scope_is_not_used()
		{
			_approvals.Setup(a => a.GetForRequesterAsync("ap-adp", MfaApprovalRequesterKind.Session, WebSession, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, new MfaApprovalRequest
				{
					Purpose = (int)MfaApprovalPurpose.StepUp, Operation = MfaStepUpOperations.AdpManagement, LockVersion = 0
				}));
			_approvals.Setup(a => a.GetForRequesterAsync("ap-locked", MfaApprovalRequesterKind.Session, WebSession, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, new MfaApprovalRequest
				{
					Purpose = (int)MfaApprovalPurpose.StepUp, Operation = MfaStepUpOperations.SensitiveOperation, LockVersion = 1
				}));

			Body(await StepUp().Verify2FACompleteApproval("ap-adp", "Login", null, CancellationToken.None))["error"]!.Value<string>().Should().Be("approval_expired");
			Body(await StepUp(lockVersion: 2).Verify2FACompleteApproval("ap-locked", "Login", null, CancellationToken.None))["error"]!.Value<string>()
				.Should().Be("approval_expired");
			Body(await StepUp().Verify2FACompleteApproval("ap-adp", "SecurityChange", null, CancellationToken.None))["error"]!.Value<string>()
				.Should().Be("mfa_method_not_allowed");

			_approvals.Verify(a => a.ConsumeAsync(It.IsAny<string>(), It.IsAny<MfaApprovalRequesterKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
				It.IsAny<CancellationToken>()), Times.Never);
			_recorded.Should().BeEmpty();
		}

		[Test]
		public void Every_scope_names_a_known_step_up_operation()
		{
			foreach (var scope in Enum.GetValues<MfaMethodScope>())
			{
				MfaStepUpOperations.IsKnown(MfaStepUpOperations.ForScope(scope)).Should().BeTrue(scope.ToString());
				MfaStepUpOperations.ScopeFor(MfaStepUpOperations.ForScope(scope)).Should().Be(scope);
			}
		}
	}
}
