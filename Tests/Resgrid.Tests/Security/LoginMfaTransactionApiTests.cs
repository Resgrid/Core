using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Authentication;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 8, over real HTTP and the real OpenIddict server: the password grant opts into the
	/// login transaction, the second factor completes it once (TOTP, a passkey bound to the app, or a recovery code), and
	/// the completion code is redeemed once through the custom grant for the normal token response. Legacy clients are
	/// untouched.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LoginMfaTransactionApiTests
	{
		private const string UserId = "user-1";
		private const string Password = "correct horse";
		private const int DepartmentId = 42;
		private const string TokenRoute = "/api/v4/connect/token";

		private bool _gate;
		private bool _requireMfaGate;
		private bool _tracking;
		private bool _ssoGate;
		private string _apiBase;
		private FakeOidcProvider _idp;
		private DepartmentSsoConfig _oidc;
		private ClaimsPrincipal _externalIdentity;
		private IdentityUser _user;
		private DepartmentSecurityPolicy _policy;
		private DepartmentMember _membership;
		private Mock<UserManager<IdentityUser>> _users;
		private Mock<IPasskeyFeatureGates> _gates;
		private Mock<IUserSessionService> _sessions;
		private List<SessionIssueContext> _issued;
		private InMemoryMfaLoginTransactionRepository _transactions;
		private InMemoryUserPasskeyRepository _passkeys;
		private InMemoryMfaEvidenceRepository _evidence;
		private InMemoryMfaActivityRepository _activityRows;
		private InMemoryMfaApprovalRequestRepository _approvalRows;
		private List<UserSession> _responderSessions;
		private Mock<INovuProvider> _novu;
		private Mock<ISecurityNoticeService> _notices;
		private InMemoryUserMfaStateRepository _mfaState;
		private InMemoryFactorRecoveryTransactionRepository _recoveryRows;
		private Dictionary<string, string> _identityTokens;
		private string _activeKey;
		private bool _twoFactor;
		internal const string NewAuthenticatorKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
		private RelyingPartyRegistry _registry;

		[SetUp]
		public void SetUp()
		{
			_gate = TwoFactorConfig.LoginMfaTransactionEnabled;
			_requireMfaGate = TwoFactorConfig.RequireMfaEnforcementEnabled;
			_tracking = SessionSecurityConfig.TrackingEnabled;
			_ssoGate = SsoConfig.BrokeredSsoEnabled;
			_apiBase = SystemBehaviorConfig.ResgridApiBaseUrl;
			SsoConfig.BrokeredSsoEnabled = true;
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.resgrid.test";
			_idp = new FakeOidcProvider();
			_oidc = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = "oidc-config", DepartmentId = DepartmentId, SsoProviderType = (int)SsoProviderType.Oidc, IsEnabled = true,
				Authority = FakeOidcProvider.Authority, ClientId = "resgrid-client"
			};
			_externalIdentity = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "external-user") }, "test"));
			TwoFactorConfig.LoginMfaTransactionEnabled = true;
			SessionSecurityConfig.TrackingEnabled = true;

			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_policy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId, MfaPolicyVersion = 3 };
			_membership = new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId };
			_gates = new Mock<IPasskeyFeatureGates>();
			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			_transactions = new InMemoryMfaLoginTransactionRepository();
			_passkeys = new InMemoryUserPasskeyRepository();
			_evidence = new InMemoryMfaEvidenceRepository();
			_activityRows = new InMemoryMfaActivityRepository();
			_approvalRows = new InMemoryMfaApprovalRequestRepository();
			_responderSessions = new List<UserSession>();
			_novu = new Mock<INovuProvider>();
			_notices = new Mock<ISecurityNoticeService>();
			_mfaState = new InMemoryUserMfaStateRepository();
			_recoveryRows = new InMemoryFactorRecoveryTransactionRepository();
			_identityTokens = new Dictionary<string, string>();
			_activeKey = "OLDKEYOLDKEYOLDKEYOLDKEYOLDKEY22";
			_twoFactor = true;
			_registry = new RelyingPartyRegistry(PasskeyProviderTests.Layout);
			_issued = new List<SessionIssueContext>();

			_users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			_users.Setup(m => m.FindByNameAsync("user1")).ReturnsAsync(() => _user);
			_users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(() => _user);
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(() => _twoFactor);
			_users.Setup(m => m.SetTwoFactorEnabledAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>()))
				.ReturnsAsync((IdentityUser _, bool enabled) => { _twoFactor = enabled; return IdentityResult.Success; });
			// Identity's token store, as the staged authenticator key uses it (plan section 6.2).
			_users.Setup(m => m.GenerateNewAuthenticatorKey()).Returns(NewAuthenticatorKey);
			_users.Setup(m => m.GetEmailAsync(It.IsAny<IdentityUser>())).ReturnsAsync("user1@example.com");
			_users.Setup(m => m.SetAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string provider, string name, string value) => { _identityTokens[provider + "/" + name] = value; return IdentityResult.Success; });
			_users.Setup(m => m.GetAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string provider, string name) => _identityTokens.GetValueOrDefault(provider + "/" + name));
			_users.Setup(m => m.RemoveAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string provider, string name) => { _identityTokens.Remove(provider + "/" + name); return IdentityResult.Success; });
			_users.Setup(m => m.GenerateNewTwoFactorRecoveryCodesAsync(It.IsAny<IdentityUser>(), It.IsAny<int>()))
				.ReturnsAsync(new[] { "NEWCD-00001", "NEWCD-00002" });
			_users.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.AccessFailedAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.IsLockedOutAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
			_users.Setup(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string _, string code) => code == "123456");
			_users.Setup(m => m.RedeemTwoFactorRecoveryCodeAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string code) => code == "ABCDE-12345" ? IdentityResult.Success : IdentityResult.Failed());

			_sessions = new Mock<IUserSessionService>();
			_sessions.Setup(s => s.CreateSessionAsync(It.IsAny<SessionIssueContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SessionIssueContext context, CancellationToken _) =>
				{
					_issued.Add(context);
					return new UserSession
					{
						UserSessionId = "session-" + _issued.Count, UserId = context.UserId, DepartmentId = context.DepartmentId,
						ClientApplication = (int)context.ClientApplication, AuthenticationGeneration = context.AuthenticationGeneration,
						LoginMfaMethod = context.LoginMfaMethod == null ? null : (int)context.LoginMfaMethod.Value,
						LoginMfaFactorReference = context.LoginMfaFactorReference
					};
				});
		}

		[TearDown]
		public void TearDown()
		{
			TwoFactorConfig.LoginMfaTransactionEnabled = _gate;
			TwoFactorConfig.RequireMfaEnforcementEnabled = _requireMfaGate;
			SessionSecurityConfig.TrackingEnabled = _tracking;
			SsoConfig.BrokeredSsoEnabled = _ssoGate;
			SystemBehaviorConfig.ResgridApiBaseUrl = _apiBase;
		}

		// ---- Host ----------------------------------------------------------------------------------------------------

		private async Task WithServer(Func<HttpClient, Task> test)
		{
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddApiVersioning();
			builder.Services.AddOpenIddict().AddServer(options =>
			{
				// The token endpoint, grants and passthrough match Startup; degraded mode and ephemeral keys replace the stores
				// and certificates, which play no part in these flows.
				Resgrid.Web.Services.Helpers.ResgridTokenEndpoints.UseResgridTokenEndpoints(options);
				options.AcceptAnonymousClients();
				options.EnableDegradedMode();
				options.DisableAccessTokenEncryption();
				options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
				options.UseAspNetCore().EnableTokenEndpointPassthrough().DisableTransportSecurityRequirement();
				options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(handler => handler.UseInlineHandler(_ => default));
			});
			builder.Services.AddAuthorization();
			builder.Services.AddControllers().AddApplicationPart(typeof(ConnectController).Assembly)
				.AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByUserIdAsync(UserId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			departments.Setup(d => d.GetDepartmentMemberAsync(UserId, DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _membership);
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "DEPT" });
			departments.Setup(d => d.GetDepartmentByNameAsync("DEPT")).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "DEPT" });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => new[] { _oidc });
			sso.Setup(s => s.GetSsoConfigForDepartmentAsync(DepartmentId, SsoProviderType.Oidc, It.IsAny<CancellationToken>())).ReturnsAsync(() => _oidc);
			sso.Setup(s => s.ProvisionOrLinkUserAsync(DepartmentId, It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), "DEPT", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => _user);
			sso.Setup(s => s.ValidateExternalTokenAsync(DepartmentId, SsoProviderType.Oidc, It.IsAny<string>(), "DEPT", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => _externalIdentity);
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
			sso.Setup(s => s.GetTestedFederatedMfaConfigAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => FederatedMfaMapping.IsTested(_oidc) ? _oidc : null);
			sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => FederatedMfaMapping.IsTested(_oidc));
			sso.Setup(s => s.FindLinkedUserIdAsync(DepartmentId, It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, ClaimsPrincipal principal, DepartmentSsoConfig _, CancellationToken _) =>
					principal.FindFirst(ClaimTypes.NameIdentifier)?.Value == "external-user" ? UserId : null);
			var links = new Mock<IExternalIdentityLinkService>();
			links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);

			var signIn = new Mock<SignInManager<IdentityUser>>(_users.Object, Mock.Of<IHttpContextAccessor>(),
				Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(), null, null, null, null);
			signIn.Setup(s => s.CheckPasswordSignInAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), true))
				.ReturnsAsync((IdentityUser _, string password, bool _) => password == Password ? SignInResult.Success : SignInResult.Failed);
			signIn.Setup(s => s.CanSignInAsync(It.IsAny<IdentityUser>())).ReturnsAsync(true);
			signIn.Setup(s => s.ValidateSecurityStampAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(() => _user);
			signIn.Setup(s => s.CreateUserPrincipalAsync(It.IsAny<IdentityUser>())).ReturnsAsync((IdentityUser user) =>
				new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(OpenIddictConstants.Claims.Subject, user.Id), new Claim(OpenIddictConstants.Claims.Name, user.UserName) },
					"Identity.Application", OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role)));

			// The user's other sessions: Responder sessions that can approve (plan section 7.9).
			var sessionRows = new Mock<IUserSessionsRepository>();
			sessionRows.Setup(s => s.GetActiveByUserAsync(UserId, It.IsAny<DateTime>())).ReturnsAsync(() => _responderSessions.ToList());
			sessionRows.Setup(s => s.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync((object id) => _responderSessions.SingleOrDefault(s => s.UserSessionId == (string)id));

			var mfaState = _mfaState;
			var policy = new MfaPolicyService(sso.Object, mfaState, _gates.Object);
			var evidence = new MfaEvidenceService(_evidence, mfaState, _passkeys, sessionRows.Object, _activityRows, TimeProvider.System);
			var activity = new MfaActivityService(_activityRows, sessionRows.Object, _sessions.Object, _notices.Object, Mock.Of<ISystemAuditsService>(),
				TimeProvider.System);
			var challenges = new AuthenticationChallengeService(new InMemoryAuthenticationChallengeRepository(), TimeProvider.System);
			var passkeys = new PasskeyService(_passkeys, new Fido2PasskeyProvider(_registry), _registry, _gates.Object, challenges, evidence, policy,
				sessionRows.Object, _sessions.Object, Mock.Of<ISystemAuditsService>(), _approvalRows, _notices.Object, TimeProvider.System);
			var approvals = new MfaApprovalService(_approvalRows, _passkeys, sessionRows.Object, identity.Object, _transactions, passkeys, _registry, _gates.Object,
				policy, departments.Object, _novu.Object, Mock.Of<IIpLocationProvider>(), Mock.Of<ISystemAuditsService>(), _notices.Object, Mock.Of<ISessionEventPublisher>(),
				activity, TimeProvider.System);
			var keyStore = new Mock<IUserStore<IdentityUser>>();
			keyStore.As<IUserAuthenticatorKeyStore<IdentityUser>>()
				.Setup(s => s.SetAuthenticatorKeyAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((IdentityUser _, string key, CancellationToken _) => _activeKey = key)
				.Returns(Task.CompletedTask);
			var transactions = new MfaLoginTransactionService(_transactions, policy, sso.Object, identity.Object, passkeys, approvals, _gates.Object, TimeProvider.System);

			builder.Services.AddSingleton(Mock.Of<IUsersService>());
			builder.Services.AddSingleton(Mock.Of<IUserProfileService>());
			builder.Services.AddSingleton(departments.Object);
			builder.Services.AddSingleton(signIn.Object);
			builder.Services.AddSingleton(_users.Object);
			builder.Services.AddSingleton(Mock.Of<ISystemAuditsService>());
			builder.Services.AddSingleton(sso.Object);
			var encryption = new Mock<IEncryptionService>();
			encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string value) => "enc:" + value);
			encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string value) => value.StartsWith("enc:") ? value[4..] : throw new CryptographicException());
			builder.Services.AddSingleton(encryption.Object);
			builder.Services.AddSingleton(Mock.Of<ICacheProvider>());
			builder.Services.AddSingleton(_sessions.Object);
			builder.Services.AddSingleton(links.Object);
			builder.Services.AddSingleton<IMfaPolicyService>(policy);
			builder.Services.AddSingleton<IMfaEvidenceService>(evidence);
			builder.Services.AddSingleton<IPasskeyService>(passkeys);
			builder.Services.AddSingleton<IMfaLoginTransactionService>(transactions);
			builder.Services.AddSingleton<IMfaApprovalService>(approvals);
			builder.Services.AddSingleton<IMfaActivityService>(activity);
			builder.Services.AddSingleton(Mock.Of<IAdpStepUpService>());
			builder.Services.AddSingleton(keyStore.Object);
			builder.Services.AddSingleton<IUserMfaStateRepository>(_mfaState);
			builder.Services.AddSingleton(_notices.Object);
			builder.Services.AddSingleton<IFactorRecoveryService>(new FactorRecoveryService(_recoveryRows, identity.Object, TimeProvider.System));
			builder.Services.AddSingleton<IUserPasskeyRepository>(_passkeys);
			builder.Services.AddSingleton<IAuthenticationChallengeService>(challenges);
			builder.Services.AddSingleton<IMfaApprovalRequestRepository>(_approvalRows);
			builder.Services.AddSingleton<ISsoBrokerService>(new SsoBrokerService(new InMemorySsoLoginTransactionRepository(), sso.Object, departments.Object,
				identity.Object, encryption.Object, _idp, new SsoReturnTargetRegistry("unit=resgridunit://sso-return"), new InMemoryBrokerReplayRepository(),
				policy, TimeProvider.System));

			await using var app = builder.Build();
			app.UseRouting();
			app.UseAuthentication();
			app.UseAuthorization();
			// Stands in for SessionValidationMiddleware: a request naming one of the user's sessions runs as that validated session.
			app.Use(async (context, next) =>
			{
				var session = _responderSessions.SingleOrDefault(s => s.UserSessionId == context.Request.Headers["X-Test-Session"].ToString());
				if (session != null)
				{
					context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
					{
						new Claim(ClaimTypes.PrimarySid, session.UserId), new Claim(ClaimTypes.PrimaryGroupSid, session.DepartmentId.ToString()),
						new Claim(ClaimTypes.Name, "user1")
					}, "test"));
					context.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
					{
						SessionId = session.UserSessionId, ClientApplication = session.ClientApplication, AuthenticationGeneration = session.AuthenticationGeneration
					};
				}

				await next();
			});
			app.MapControllers();
			try
			{
				await app.StartAsync();
				// Redirects are inspected, never followed: the brokered callback sends the browser to an app's own scheme.
				using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
				client.DefaultRequestHeaders.Add("X-Resgrid-Client", "unit");
				await test(client);
			}
			finally
			{
				await app.StopAsync();
			}
		}

		private static async Task<(HttpStatusCode Status, JObject Body)> Send(HttpResponseMessage response) =>
			(response.StatusCode, JObject.Parse(await response.Content.ReadAsStringAsync()));

		private static Task<HttpResponseMessage> PasswordGrant(HttpClient client, bool transaction = true, string totp = null)
		{
			var form = new Dictionary<string, string>
			{
				["grant_type"] = "password", ["username"] = "user1", ["password"] = Password, ["scope"] = "openid offline_access"
			};
			if (transaction) form["mfa_flow"] = "transaction";
			if (totp != null) form["totp_code"] = totp;
			return client.PostAsync(TokenRoute, new FormUrlEncodedContent(form));
		}

		private static Task<HttpResponseMessage> Redeem(HttpClient client, string transaction, string code) =>
			client.PostAsync(TokenRoute, new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = MfaLoginTransactions.CompletionGrantType, ["transaction"] = transaction, ["completion_code"] = code
			}));

		private static Task<HttpResponseMessage> Post(HttpClient client, string action, object body) =>
			client.PostAsync("/api/v4/Authentication/" + action, new StringContent(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json"));

		private static async Task<string> BeginTransaction(HttpClient client)
		{
			var (status, body) = await Send(await PasswordGrant(client));
			status.Should().Be(HttpStatusCode.BadRequest);
			body.Value<string>("error").Should().Be("mfa_required");
			return body.Value<string>("mfa_transaction");
		}

		private async Task<(UserPasskey Passkey, SoftPasskeyAuthenticator Authenticator)> RegisteredUnitPasskey()
		{
			var provider = new Fido2PasskeyProvider(_registry);
			var handle = RandomNumberGenerator.GetBytes(32);
			var authenticator = new SoftPasskeyAuthenticator();
			var options = provider.CreateRegistrationOptions(UserSessionClientApplication.Unit, handle, "user1", "user1", Array.Empty<byte[]>(), false);
			var registered = await provider.VerifyRegistrationAsync(UserSessionClientApplication.Unit, options,
				authenticator.Register(options, PasskeyProviderTests.Origin(UserSessionClientApplication.Unit)));
			var passkey = new UserPasskey
			{
				UserPasskeyId = "pk-unit", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Unit, RpId = "unit.resgrid.test",
				CredentialId = registered.CredentialId, CredentialIdHash = SHA256.HashData(registered.CredentialId), PublicKey = registered.PublicKey,
				UserHandle = registered.UserHandle, DisplayName = "Unit passkey", CreatedOnUtc = DateTime.UtcNow, StateVersion = 1
			};
			await _passkeys.TryInsertAsync(passkey);
			return (passkey, authenticator);
		}

		// ---- Legacy clients ------------------------------------------------------------------------------------------

		[Test]
		public async Task Legacy_clients_and_a_closed_gate_keep_the_existing_totp_contract()
		{
			await WithServer(async client =>
			{
				var (status, legacy) = await Send(await PasswordGrant(client, transaction: false));
				status.Should().Be(HttpStatusCode.BadRequest);
				legacy.Value<string>("error").Should().Be("mfa_required");
				legacy.ContainsKey("mfa_transaction").Should().BeFalse();

				TwoFactorConfig.LoginMfaTransactionEnabled = false;
				var (_, gated) = await Send(await PasswordGrant(client));
				gated.Value<string>("error").Should().Be("mfa_required");
				gated.ContainsKey("mfa_transaction").Should().BeFalse("the gate turns the transaction off");
				_transactions.Rows.Should().BeEmpty();

				var (okStatus, tokens) = await Send(await PasswordGrant(client, transaction: false, totp: "123456"));
				okStatus.Should().Be(HttpStatusCode.OK);
				tokens.Value<string>("access_token").Should().NotBeNullOrEmpty();
				_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.Totp);
			});
		}

		// ---- The legacy SSO exchange (plan section 7.7.4) -------------------------------------------------------------

		private static Task<HttpResponseMessage> ExternalToken(HttpClient client, Dictionary<string, string> form) =>
			client.PostAsync(Resgrid.Web.Services.Helpers.ResgridTokenEndpoints.ExternalTokenPath, new FormUrlEncodedContent(form));

		[Test]
		public async Task The_legacy_sso_exchange_issues_tokens_through_openiddict_as_older_app_builds_call_it()
		{
			// Workbook slice 9 found every successful exchange ending in a 500: this route was not an OpenIddict token endpoint.
			await WithServer(async client =>
			{
				var idToken = _idp.Token("legacy-nonce");
				var form = new Dictionary<string, string>
				{
					// No grant_type, as the apps send it.
					["provider"] = "oidc", ["external_token"] = idToken, ["department_code"] = "DEPT", ["totp_code"] = "123456",
					["scope"] = "openid email profile offline_access mobile"
				};
				var (status, tokens) = await Send(await ExternalToken(client, form));
				status.Should().Be(HttpStatusCode.OK, tokens.ToString());
				tokens.Value<string>("access_token").Should().NotBeNullOrEmpty();
				tokens.Value<string>("refresh_token").Should().NotBeNullOrEmpty("the app keeps signing in by refresh afterwards");
				_issued.Single().AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.OidcSso);

				_sessions.Setup(s => s.ValidateAsync(It.IsAny<SessionPrincipalContext>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(SessionValidationResult.Valid(new UserSession { UserSessionId = "session-1", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Unit }));
				var (refreshStatus, refreshed) = await Send(await client.PostAsync(TokenRoute, new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Value<string>("refresh_token")
				})));
				refreshStatus.Should().Be(HttpStatusCode.OK, refreshed.ToString());

				var (replayStatus, replayed) = await Send(await ExternalToken(client, form));
				replayStatus.Should().Be(HttpStatusCode.Unauthorized);
				replayed.Value<string>("error").Should().Be("invalid_grant", "an id_token signs in once");
			});
		}

		[Test]
		public async Task The_legacy_sso_route_only_ever_performs_the_sso_exchange()
		{
			await WithServer(async client =>
			{
				// A password or refresh request sent here is not a password sign-in or a refresh; it is an SSO exchange missing its token.
				var (status, body) = await Send(await ExternalToken(client, new Dictionary<string, string>
				{
					["grant_type"] = "password", ["username"] = "user1", ["password"] = Password, ["scope"] = "openid offline_access"
				}));
				status.Should().Be(HttpStatusCode.BadRequest);
				body.Value<string>("error").Should().Be("invalid_request");
				_issued.Should().BeEmpty();

				var misrouted = await client.PostAsync(TokenRoute, new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = Resgrid.Web.Services.Helpers.ResgridTokenEndpoints.ExternalTokenGrantType, ["provider"] = "oidc",
					["external_token"] = _idp.Token("n2"), ["department_code"] = "DEPT", ["totp_code"] = "123456"
				}));
				misrouted.StatusCode.Should().NotBe(HttpStatusCode.OK, "the exchange grant is only answered on its own route");
				_issued.Should().BeEmpty();
			});
		}

		// ---- Shared installations (plan section 12.5.3) ------------------------------------------------------------

		[Test]
		public async Task A_shared_installation_asks_for_a_shared_session_and_a_locked_session_cannot_refresh()
		{
			await WithServer(async client =>
			{
				var signIn = new HttpRequestMessage(HttpMethod.Post, TokenRoute)
				{
					Content = new FormUrlEncodedContent(new Dictionary<string, string>
					{
						["grant_type"] = "password", ["username"] = "user1", ["password"] = Password, ["scope"] = "openid offline_access", ["totp_code"] = "123456"
					})
				};
				signIn.Headers.Add(SharedSessionRules.InstallationHeader, "true");
				var (status, tokens) = await Send(await client.SendAsync(signIn));
				status.Should().Be(HttpStatusCode.OK, tokens.ToString());
				_issued.Single().SharedModeRequested.Should().BeTrue("the installation's request reaches the session service, which decides");

				var locked = new UserSession { UserSessionId = "session-1", UserId = UserId, SharedMode = true, LockVersion = 1, IsLocked = true };
				_sessions.Setup(s => s.ValidateAsync(It.IsAny<SessionPrincipalContext>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(SessionValidationResult.Locked(locked));
				var (refreshStatus, refused) = await Send(await client.PostAsync(TokenRoute, new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Value<string>("refresh_token")
				})));
				refreshStatus.Should().Be(HttpStatusCode.BadRequest, refused.ToString());
				refused.Value<string>("error").Should().Be("invalid_grant");
				refused.Value<bool>("shared_session_locked").Should().BeTrue("a locked session cannot refresh its way back to active, and keeps its tokens to unlock");

				_sessions.Setup(s => s.ValidateAsync(It.IsAny<SessionPrincipalContext>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(SessionValidationResult.Invalid("session_revoked"));
				var (_, revoked) = await Send(await client.PostAsync(TokenRoute, new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Value<string>("refresh_token")
				})));
				revoked.ContainsKey("shared_session_locked").Should().BeFalse("only a locked session is told to unlock");
			});
		}

		// ---- The transaction -----------------------------------------------------------------------------------------

		[Test]
		public async Task A_password_opens_a_transaction_that_issues_no_tokens()
		{
			await WithServer(async client =>
			{
				var (status, body) = await Send(await PasswordGrant(client));

				status.Should().Be(HttpStatusCode.BadRequest);
				body.Value<string>("error").Should().Be("mfa_required", body.ToString());
				body.Value<string>("mfa_transaction").Should().HaveLength(43);
				body.Value<string>("mfa_methods").Should().Be("totp passkey");
				body.Value<string>("mfa_enrolled").Should().Be("totp");
				body.Value<string>("mfa_preferred").Should().Be("totp");
				body.Value<int>("mfa_expires_in").Should().Be(TwoFactorConfig.LoginMfaTransactionLifetimeSeconds);
				body.ContainsKey("access_token").Should().BeFalse();
				_issued.Should().BeEmpty("no session exists until the second factor");

				var (_, wrong) = await Send(await client.PostAsync(TokenRoute, new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "password", ["username"] = "user1", ["password"] = "wrong", ["mfa_flow"] = "transaction"
				})));
				wrong.Value<string>("error").Should().Be("invalid_grant", "the password is checked first; no transaction for a wrong one");
				_transactions.Rows.Should().ContainSingle();
			});
		}

		[Test]
		public async Task A_totp_completion_redeems_once_for_tokens_and_a_session_with_the_original_first_factor_time()
		{
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);
				var firstFactorAt = _transactions.Rows.Single().FirstFactorVerifiedOnUtc;

				var (status, completed) = await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "123456" }));
				status.Should().Be(HttpStatusCode.OK, completed.ToString());
				var code = completed["Data"]!.Value<string>("CompletionCode");
				completed["Data"]!.Value<int>("ExpiresIn").Should().Be(TwoFactorConfig.LoginMfaCompletionCodeLifetimeSeconds);
				completed["Data"]!.Value<bool>("Recovery").Should().BeFalse();

				var (tokenStatus, tokens) = await Send(await Redeem(client, transaction, code));
				tokenStatus.Should().Be(HttpStatusCode.OK, tokens.ToString());
				tokens.Value<string>("access_token").Should().NotBeNullOrEmpty();
				tokens.Value<string>("refresh_token").Should().NotBeNullOrEmpty("offline_access was granted at the first factor");

				var session = _issued.Single();
				session.ClientApplication.Should().Be(UserSessionClientApplication.Unit);
				session.AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.LocalPassword);
				session.LoginMfaMethod.Should().Be(MfaEvidenceMethod.Totp);
				_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.FirstFactor).VerifiedOnUtc.Should().Be(firstFactorAt,
					"completing the login never makes the password look fresher");
				_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.SecondFactor).Method.Should().Be((int)MfaEvidenceMethod.Totp);
				var used = _activityRows.Rows.Should().ContainSingle("the verified sign-in is the account's recent activity").Subject;
				used.Successful.Should().BeTrue();
				used.Method.Should().Be((int)MfaEvidenceMethod.Totp);
				used.Purpose.Should().Be((int)MfaEvidencePurpose.Login);
				used.OccurredOnUtc.Should().Be(_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.SecondFactor).VerifiedOnUtc);
				used.SessionId.Should().Be("session-1", "the activity names the session the sign-in opened, so reporting it can end that session");

				var (againStatus, again) = await Send(await Redeem(client, transaction, code));
				againStatus.Should().Be(HttpStatusCode.BadRequest);
				again.Value<string>("error").Should().Be("mfa_transaction_invalid");
				_issued.Should().ContainSingle("a completion is redeemed once");
			});
		}

		[Test]
		public async Task A_passkey_bound_to_the_app_completes_the_sign_in()
		{
			var (passkey, authenticator) = await RegisteredUnitPasskey();
			await WithServer(async client =>
			{
				var (_, begun) = await Send(await PasswordGrant(client));
				begun.Value<string>("mfa_enrolled").Should().Be("totp passkey");
				var transaction = begun.Value<string>("mfa_transaction");

				var (optionsStatus, options) = await Send(await Post(client, "PasskeyOptions", new { Transaction = transaction }));
				optionsStatus.Should().Be(HttpStatusCode.OK, options.ToString());
				var requestId = options["Data"]!.Value<string>("RequestId");
				var assertion = authenticator.Assert(options["Data"]!["Options"]!.ToString(), PasskeyProviderTests.Origin(UserSessionClientApplication.Unit),
					passkey.UserHandle);

				var (status, completed) = await Send(await Post(client, "CompletePasskey",
					new { Transaction = transaction, RequestId = requestId, Credential = JObject.Parse(assertion) }));
				status.Should().Be(HttpStatusCode.OK, completed.ToString());

				var (tokenStatus, _) = await Send(await Redeem(client, transaction, completed["Data"]!.Value<string>("CompletionCode")));
				tokenStatus.Should().Be(HttpStatusCode.OK);

				var session = _issued.Single();
				session.LoginMfaMethod.Should().Be(MfaEvidenceMethod.Passkey);
				session.LoginMfaFactorReference.Should().Be("passkey:pk-unit", "so removing the passkey ends this session");
				_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.SecondFactor).FactorReference.Should().Be("passkey:pk-unit");
				_passkeys.Rows.Single().SignCount.Should().Be(1);
			});
		}

		[Test]
		public async Task A_passkey_is_refused_where_the_department_or_deployment_does_not_accept_it()
		{
			await RegisteredUnitPasskey();
			await WithServer(async client =>
			{
				_policy.AllowPasskeysForLoginMfa = false;
				var transaction = await BeginTransaction(client);
				var (status, body) = await Send(await Post(client, "PasskeyOptions", new { Transaction = transaction }));
				status.Should().Be(HttpStatusCode.BadRequest);
				body.Value<string>("type").Should().Be("mfa_method_not_allowed");

				_policy.AllowPasskeysForLoginMfa = true;
				_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(false);
				var (_, gated) = await Send(await Post(client, "PasskeyOptions", new { Transaction = await BeginTransaction(client) }));
				gated.Value<string>("type").Should().Be("mfa_method_not_allowed");
			});
		}

		[Test]
		public async Task A_recovery_code_completes_the_sign_in_as_recovery_only()
		{
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);

				var (status, completed) = await Send(await Post(client, "CompleteRecoveryCode", new { Transaction = transaction, Code = "ABCDE-12345" }));
				status.Should().Be(HttpStatusCode.OK, completed.ToString());
				completed["Data"]!.Value<bool>("Recovery").Should().BeTrue();

				(await Send(await Redeem(client, transaction, completed["Data"]!.Value<string>("CompletionCode")))).Status.Should().Be(HttpStatusCode.OK);

				_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.RecoveryCode);
				_evidence.Rows.Should().NotContain(e => e.Kind == (int)MfaEvidenceKind.SecondFactor, "recovery never counts as MFA later");
				_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.Recovery).Method.Should().Be((int)MfaEvidenceMethod.RecoveryCode);
			});
		}

		[Test]
		public async Task Failures_of_every_method_share_the_attempt_limit_and_the_account_lockout()
		{
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);

				var (status, wrong) = await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "000000" }));
				status.Should().Be(HttpStatusCode.Unauthorized);
				wrong.Value<string>("type").Should().Be("invalid_totp");
				(await Send(await Post(client, "CompleteRecoveryCode", new { Transaction = transaction, Code = "WRONG" }))).Body
					.Value<string>("type").Should().Be("invalid_recovery_code");
				for (var i = 2; i < TwoFactorConfig.LoginMfaTransactionMaxAttempts; i++)
					await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "000000" });

				_users.Verify(m => m.AccessFailedAsync(It.IsAny<IdentityUser>()), Times.Exactly(TwoFactorConfig.LoginMfaTransactionMaxAttempts));
				_activityRows.Rows.Should().HaveCount(TwoFactorConfig.LoginMfaTransactionMaxAttempts, "every refused attempt is the account's recent activity")
					.And.OnlyContain(a => !a.Successful && a.Purpose == (int)MfaEvidencePurpose.Login && a.UserId == UserId &&
						a.ClientApplication == (int)UserSessionClientApplication.Unit && a.DepartmentId == DepartmentId && a.SessionId == null);
				_activityRows.Rows.Select(a => a.Method).Should().Contain(new[] { (int)MfaEvidenceMethod.Totp, (int)MfaEvidenceMethod.RecoveryCode });
				var (spentStatus, spent) = await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "123456" }));
				spentStatus.Should().Be(HttpStatusCode.TooManyRequests);
				spent.Value<string>("type").Should().Be("too_many_attempts", "even the right code cannot finish a spent transaction");
			});
		}

		[Test]
		public async Task A_transaction_belongs_to_the_app_that_started_it()
		{
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);
				client.DefaultRequestHeaders.Remove("X-Resgrid-Client");
				client.DefaultRequestHeaders.Add("X-Resgrid-Client", "responder");

				var (status, body) = await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "123456" }));
				status.Should().Be(HttpStatusCode.BadRequest);
				body.Value<string>("type").Should().Be("mfa_transaction_invalid");
			});
		}

		[Test]
		public async Task Redemption_rechecks_what_allowed_the_first_factor()
		{
			await WithServer(async client =>
			{
				async Task<(string Transaction, string Code)> Completed()
				{
					var transaction = await BeginTransaction(client);
					var (_, body) = await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "123456" }));
					return (transaction, body["Data"]!.Value<string>("CompletionCode"));
				}

				var policyChange = await Completed();
				_policy.MfaPolicyVersion = 4;
				(await Send(await Redeem(client, policyChange.Transaction, policyChange.Code))).Body.Value<string>("error").Should().Be("policy_changed");
				_policy.MfaPolicyVersion = 3;

				var disabled = await Completed();
				_membership = new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId, IsDisabled = true };
				(await Send(await Redeem(client, disabled.Transaction, disabled.Code))).Body.Value<string>("error").Should().Be("invalid_grant");
				_membership = new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId };

				var revoked = await Completed();
				_user.AuthenticationGeneration = 5;
				(await Send(await Redeem(client, revoked.Transaction, revoked.Code))).Body.Value<string>("error").Should().Be("session_revoked");

				(await Send(await Redeem(client, "no-such-transaction", "no-such-code"))).Body.Value<string>("error").Should().Be("mfa_transaction_invalid");
				_issued.Should().BeEmpty();
			});
		}
	

		// ---- Brokered SSO (slice 9) ----------------------------------------------------------------------------------

		/// <summary>Runs Sso/Begin, plays the IdP, follows the callback's redirect and returns what the app would hold.</summary>
		private async Task<(string TransactionId, string Code, string Verifier)> SignInAtIdp(HttpClient client) =>
			(await RoundTrip(client, new { DepartmentCode = "DEPT" }, nonce => _idp.Token(nonce))).Held;

		/// <summary>
		/// Sso/Begin with the given purpose fields (the return target, state and PKCE are added), the IdP answering with
		/// <paramref name="token"/>; returns the authorize URL the app opened and what the app holds afterwards.
		/// </summary>
		private async Task<((string TransactionId, string Code, string Verifier) Held, string AuthorizeUrl)> RoundTrip(HttpClient client, object purpose,
			Func<string, string> token)
		{
			var verifier = FakeOidcProvider.Base64Url(RandomNumberGenerator.GetBytes(32));
			var challenge = FakeOidcProvider.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
			var body = JObject.FromObject(purpose);
			body["ReturnTarget"] = "resgridunit://sso-return";
			body["State"] = "app-csrf";
			body["CodeChallenge"] = challenge;
			body["CodeChallengeMethod"] = "S256";
			var (beginStatus, begun) = await Send(await client.PostAsync("/api/v4/Sso/Begin", new StringContent(body.ToString(), Encoding.UTF8, "application/json")));
			beginStatus.Should().Be(HttpStatusCode.OK, begun.ToString());

			var authorizeUrl = begun["Data"]!.Value<string>("AuthorizeUrl");
			var (state, nonce, idpChallenge, _, _) = FakeOidcProvider.Authorize(authorizeUrl);
			_idp.ExpectedChallenge = idpChallenge;
			_idp.NextIdToken = () => token(nonce);

			var callback = await client.GetAsync($"/api/v4/connect/oidc-callback?state={Uri.EscapeDataString(state)}&code={FakeOidcProvider.Code}");
			callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
			var location = callback.Headers.Location!.OriginalString;
			location.Should().StartWith("resgridunit://sso-return?sso_code=");
			var query = System.Web.HttpUtility.ParseQueryString(location[location.IndexOf('?')..]);
			query["state"].Should().Be("app-csrf");
			return ((begun["Data"]!.Value<string>("SsoTransactionId"), query["sso_code"], verifier), authorizeUrl);
		}

		private static StringContent Json(object body) => new(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json");

		[Test]
		public async Task Brokered_sso_signs_in_through_the_login_transaction()
		{
			await WithServer(async client =>
			{
				var (transactionId, code, verifier) = await SignInAtIdp(client);

				var (status, redeemed) = await Send(await client.PostAsync("/api/v4/Sso/Redeem", Json(new { SsoTransactionId = transactionId, SsoCode = code, CodeVerifier = verifier })));
				status.Should().Be(HttpStatusCode.OK, redeemed.ToString());
				redeemed["Data"]!.Value<string>("Outcome").Should().Be("mfa_required");
				var transaction = redeemed["Data"]!.Value<string>("Transaction");
				_transactions.Rows.Single().FirstFactorMethod.Should().Be((int)MfaEvidenceMethod.Sso);
				_transactions.Rows.Single().DepartmentSsoConfigId.Should().Be("oidc-config");

				var (_, completed) = await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "123456" }));
				var (tokenStatus, tokens) = await Send(await Redeem(client, transaction, completed["Data"]!.Value<string>("CompletionCode")));
				tokenStatus.Should().Be(HttpStatusCode.OK, tokens.ToString());

				var session = _issued.Single();
				session.AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.OidcSso);
				session.DepartmentSsoConfigId.Should().Be("oidc-config");
				session.LoginMfaMethod.Should().Be(MfaEvidenceMethod.Totp);
				_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.FirstFactor).Method.Should().Be((int)MfaEvidenceMethod.Sso);

				(await Send(await client.PostAsync("/api/v4/Sso/Redeem", Json(new { SsoTransactionId = transactionId, SsoCode = code, CodeVerifier = verifier }))))
					.Body.Value<string>("type").Should().Be("sso_transaction_invalid", "the sso_code redeems once");
			});
		}

		[Test]
		public async Task Brokered_sso_without_mfa_completes_directly_and_records_no_second_factor()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
			await WithServer(async client =>
			{
				var (transactionId, code, verifier) = await SignInAtIdp(client);

				var (_, redeemed) = await Send(await client.PostAsync("/api/v4/Sso/Redeem", Json(new { SsoTransactionId = transactionId, SsoCode = code, CodeVerifier = verifier })));
				redeemed["Data"]!.Value<string>("Outcome").Should().Be("completed");

				var (tokenStatus, _) = await Send(await Redeem(client, redeemed["Data"]!.Value<string>("Transaction"), redeemed["Data"]!.Value<string>("CompletionCode")));
				tokenStatus.Should().Be(HttpStatusCode.OK);
				_issued.Single().LoginMfaMethod.Should().BeNull();
				_evidence.Rows.Should().NotContain(e => e.Kind == (int)MfaEvidenceKind.SecondFactor);
			});
		}

		[Test]
		public async Task Brokered_sso_refuses_an_account_its_department_requires_mfa_for()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
			_policy.RequireMfa = true;
			await WithServer(async client =>
			{
				var (transactionId, code, verifier) = await SignInAtIdp(client);

				var (status, body) = await Send(await client.PostAsync("/api/v4/Sso/Redeem", Json(new { SsoTransactionId = transactionId, SsoCode = code, CodeVerifier = verifier })));
				status.Should().Be(HttpStatusCode.Conflict);
				body.Value<string>("type").Should().Be("mfa_enrollment_required");
				var setup = body.Value<string>("mfa_setup_transaction");
				setup.Should().NotBeNullOrEmpty("the app can enroll right here (plan section 6.2)");
				(await Send(await Post(client, "CompleteTotp", new { Transaction = setup, Code = "123456" }))).Body.Value<string>("type")
					.Should().Be("mfa_method_not_allowed", "a setup transaction only sets up an authenticator");
				_issued.Should().BeEmpty("no tokens without enrolled MFA");
			});
		}

		// ---- Provider step-up (slice 10) -----------------------------------------------------------------------------

		/// <summary>A tested mapping on the department's OIDC provider, accepted for sign-in, deployed, for a member with no Resgrid factor.</summary>
		private void ProviderMfaOnly()
		{
			_oidc.FederatedMfaMappingJson = new FederatedMfaMapping { RequestAcrValues = new() { "mfa-level" }, AcceptAmr = new() { "mfa" } }.Serialize();
			_oidc.FederatedMfaMappingVersion = 1;
			_oidc.FederatedMfaTestedVersion = 1;
			_policy.AllowFederatedMfaForLoginMfa = true;
			_policy.RequireMfa = true;
			TwoFactorConfig.RequireMfaEnforcementEnabled = true;
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
		}

		private static string Query(string url, string name) => System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)[name];

		[Test]
		public async Task A_brokered_sign_in_that_carries_the_providers_mapped_mfa_needs_no_resgrid_prompt()
		{
			ProviderMfaOnly();
			await WithServer(async client =>
			{
				var ((transactionId, code, verifier), authorizeUrl) = await RoundTrip(client, new { DepartmentCode = "DEPT" },
					nonce => _idp.Token(nonce, amr: new[] { "pwd", "mfa" }));
				Query(authorizeUrl, "acr_values").Should().Be("mfa-level", "the sign-in's own round trip asks for the mapped MFA");

				var (status, redeemed) = await Send(await client.PostAsync("/api/v4/Sso/Redeem", Json(new { SsoTransactionId = transactionId, SsoCode = code, CodeVerifier = verifier })));
				status.Should().Be(HttpStatusCode.OK, redeemed.ToString());
				redeemed["Data"]!.Value<string>("Outcome").Should().Be("completed", "RequireMfa is met by the provider's MFA");
				redeemed["Data"]!.Value<string>("MfaSatisfiedBy").Should().Be("federated");

				var (tokenStatus, tokens) = await Send(await Redeem(client, redeemed["Data"]!.Value<string>("Transaction"), redeemed["Data"]!.Value<string>("CompletionCode")));
				tokenStatus.Should().Be(HttpStatusCode.OK, tokens.ToString());
				var session = _issued.Single();
				session.LoginMfaMethod.Should().Be(MfaEvidenceMethod.Federated);
				session.LoginMfaFactorReference.Should().Be("federated:oidc-config:1", "the evidence names the configuration and mapping version");
				var second = _evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.SecondFactor);
				second.Method.Should().Be((int)MfaEvidenceMethod.Federated);
				second.FactorReference.Should().Be("federated:oidc-config:1");
			});
		}

		[Test]
		public async Task A_brokered_sign_in_without_the_mapped_mfa_still_needs_mfa_and_a_changed_mapping_voids_it()
		{
			ProviderMfaOnly();
			await WithServer(async client =>
			{
				var ((transactionId, code, verifier), _) = await RoundTrip(client, new { DepartmentCode = "DEPT" }, nonce => _idp.Token(nonce, amr: new[] { "pwd" }));
				var (status, refused) = await Send(await client.PostAsync("/api/v4/Sso/Redeem", Json(new { SsoTransactionId = transactionId, SsoCode = code, CodeVerifier = verifier })));
				status.Should().Be(HttpStatusCode.Conflict, "the provider did not assert MFA and the member has no Resgrid factor");
				refused.Value<string>("type").Should().Be("mfa_enrollment_required");

				var ((againId, againCode, againVerifier), _) = await RoundTrip(client, new { DepartmentCode = "DEPT" }, nonce => _idp.Token(nonce, amr: new[] { "mfa" }));
				var (_, completed) = await Send(await client.PostAsync("/api/v4/Sso/Redeem", Json(new { SsoTransactionId = againId, SsoCode = againCode, CodeVerifier = againVerifier })));
				completed["Data"]!.Value<string>("Outcome").Should().Be("completed");

				// The managing member changes the mapping before the app redeems: the provider MFA it verified no longer counts.
				_oidc.FederatedMfaMappingVersion = 2;
				_oidc.FederatedMfaTestedVersion = 2;
				(await Send(await Redeem(client, completed["Data"]!.Value<string>("Transaction"), completed["Data"]!.Value<string>("CompletionCode"))))
					.Body.Value<string>("error").Should().Be("policy_changed");
				_issued.Should().BeEmpty();
			});
		}

		[Test]
		public async Task A_password_sign_in_completes_with_provider_step_up_for_a_member_with_no_resgrid_factor()
		{
			ProviderMfaOnly();
			await WithServer(async client =>
			{
				var (legacyStatus, legacy) = await Send(await PasswordGrant(client, transaction: false));
				legacyStatus.Should().Be(HttpStatusCode.BadRequest);
				legacy.Value<string>("error").Should().Be("mfa_enrollment_required", "only the login transaction can carry provider step-up");

				var (status, started) = await Send(await PasswordGrant(client));
				status.Should().Be(HttpStatusCode.BadRequest);
				started.Value<string>("error").Should().Be("mfa_required", started.ToString());
				started.Value<string>("mfa_methods").Should().Be("totp passkey federated");
				started.Value<string>("mfa_enrolled").Should().Be("federated", "provider step-up is the member's only usable method");
				started.Value<string>("mfa_preferred").Should().Be("federated");
				var transaction = started.Value<string>("mfa_transaction");

				(await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "123456" }))).Body.Value<string>("type")
					.Should().Be("mfa_method_not_allowed", "the member has no authenticator app");

				var ((stepUpId, code, verifier), authorizeUrl) = await RoundTrip(client, new { Purpose = "step_up", Transaction = transaction },
					nonce => _idp.Token(nonce, authTime: DateTime.UtcNow, amr: new[] { "mfa" }));
				Query(authorizeUrl, "prompt").Should().Be("login");
				Query(authorizeUrl, "max_age").Should().Be("0");
				Query(authorizeUrl, "acr_values").Should().Be("mfa-level");

				(await Send(await client.PostAsync("/api/v4/Sso/Redeem", Json(new { SsoTransactionId = stepUpId, SsoCode = code, CodeVerifier = verifier }))))
					.Body.Value<string>("type").Should().Be("sso_transaction_invalid", "a step-up code is not a sign-in");

				var (completeStatus, completed) = await Send(await Post(client, "CompleteFederated",
					new { Transaction = transaction, SsoTransactionId = stepUpId, SsoCode = code, CodeVerifier = verifier }));
				completeStatus.Should().Be(HttpStatusCode.OK, completed.ToString());

				var (tokenStatus, tokens) = await Send(await Redeem(client, transaction, completed["Data"]!.Value<string>("CompletionCode")));
				tokenStatus.Should().Be(HttpStatusCode.OK, tokens.ToString());
				var session = _issued.Single();
				session.AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.LocalPassword);
				session.LoginMfaMethod.Should().Be(MfaEvidenceMethod.Federated);
				_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.FirstFactor).Method.Should().Be((int)MfaEvidenceMethod.Password);
				_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.SecondFactor).FactorReference.Should().Be("federated:oidc-config:1");
			});
		}

		[Test]
		public async Task A_provider_step_up_completes_only_the_sign_in_it_was_begun_for()
		{
			ProviderMfaOnly();
			await WithServer(async client =>
			{
				var first = await BeginTransaction(client);
				var second = await BeginTransaction(client);
				var ((stepUpId, code, verifier), _) = await RoundTrip(client, new { Purpose = "step_up", Transaction = first },
					nonce => _idp.Token(nonce, authTime: DateTime.UtcNow, amr: new[] { "mfa" }));

				var (status, refused) = await Send(await Post(client, "CompleteFederated",
					new { Transaction = second, SsoTransactionId = stepUpId, SsoCode = code, CodeVerifier = verifier }));
				status.Should().Be(HttpStatusCode.Unauthorized);
				refused.Value<string>("type").Should().Be("federated_mfa_not_satisfied");
				_transactions.Rows.Single(t => t.Attempts > 0).MfaLoginTransactionId.Should().Be(_transactions.Rows[1].MfaLoginTransactionId,
					"the misuse counts against the sign-in it was presented to");
				_users.Verify(m => m.AccessFailedAsync(It.IsAny<IdentityUser>()), Times.Once);

				var (_, spent) = await Send(await Post(client, "CompleteFederated",
					new { Transaction = first, SsoTransactionId = stepUpId, SsoCode = code, CodeVerifier = verifier }));
				spent.Value<string>("type").Should().Be("sso_transaction_invalid", "the step-up code was spent by the refused attempt");

				var ((staleId, staleCode, staleVerifier), _) = await RoundTrip(client, new { Purpose = "step_up", Transaction = first },
					nonce => _idp.Token(nonce, authTime: DateTime.UtcNow, amr: new[] { "mfa" }));
				_oidc.FederatedMfaMappingVersion = 2;
				_oidc.FederatedMfaTestedVersion = 2;
				(await Send(await Post(client, "CompleteFederated",
					new { Transaction = first, SsoTransactionId = staleId, SsoCode = staleCode, CodeVerifier = staleVerifier }))).Body.Value<string>("type")
					.Should().Be("federated_mfa_not_satisfied", "the mapping changed after the provider answered");
				_issued.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Provider_step_up_is_not_offered_where_the_deployment_or_department_does_not_accept_it()
		{
			ProviderMfaOnly();
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(false);
			await WithServer(async client =>
			{
				(await Send(await PasswordGrant(client))).Body.Value<string>("error").Should().Be("mfa_enrollment_required", "the deployment gate is off");

				_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
				_policy.AllowFederatedMfaForLoginMfa = false;
				(await Send(await PasswordGrant(client))).Body.Value<string>("error").Should().Be("mfa_enrollment_required", "the department switch is off");

				_policy.AllowFederatedMfaForLoginMfa = true;
				_oidc.FederatedMfaTestedVersion = null;
				var (_, untested) = await Send(await PasswordGrant(client));
				untested.Value<string>("error").Should().Be("mfa_enrollment_required", "the mapping is untested");
				untested.ContainsKey("mfa_transaction").Should().BeFalse("nothing but setup is offered");
				untested.Value<string>("mfa_setup_transaction").Should().NotBeNullOrEmpty();
				_issued.Should().BeEmpty();
			});
		}

		// ---- Responder approval (slice 11) ---------------------------------------------------------------------------

		/// <summary>The user's own Responder, signed in with an approval-enabled Responder passkey.</summary>
		private async Task<(UserPasskey Passkey, SoftPasskeyAuthenticator Authenticator)> ResponderApprover()
		{
			_gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			_responderSessions.Add(new UserSession
			{
				UserSessionId = "responder-1", UserId = UserId, DepartmentId = DepartmentId, ClientApplication = (int)UserSessionClientApplication.Responder,
				State = (int)UserSessionState.Active, ExpiresOn = DateTime.UtcNow.AddDays(1), AuthenticationGeneration = 4, DeviceName = "Pixel 8"
			});

			var provider = new Fido2PasskeyProvider(_registry);
			var authenticator = new SoftPasskeyAuthenticator();
			var options = provider.CreateRegistrationOptions(UserSessionClientApplication.Responder, RandomNumberGenerator.GetBytes(32), "user1", "user1",
				Array.Empty<byte[]>(), false);
			var registered = await provider.VerifyRegistrationAsync(UserSessionClientApplication.Responder, options,
				authenticator.Register(options, PasskeyProviderTests.Origin(UserSessionClientApplication.Responder)));
			var passkey = new UserPasskey
			{
				UserPasskeyId = "pk-responder", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Responder, RpId = "responder.resgrid.test",
				CredentialId = registered.CredentialId, CredentialIdHash = SHA256.HashData(registered.CredentialId), PublicKey = registered.PublicKey,
				UserHandle = registered.UserHandle, DisplayName = "Responder passkey", CreatedOnUtc = DateTime.UtcNow, StateVersion = 1
			};
			await _passkeys.TryInsertAsync(passkey);
			await _passkeys.TrySetApprovalEnabledAsync(passkey.UserPasskeyId, UserId, true);
			return (passkey, authenticator);
		}

		/// <summary>A call from the signed-in Responder session (as SessionValidationMiddleware would present it).</summary>
		private static Task<HttpResponseMessage> AsResponder(HttpClient client, HttpMethod method, string action, object body = null)
		{
			var message = new HttpRequestMessage(method, "/api/v4/MfaApproval/" + action);
			message.Headers.Add("X-Test-Session", "responder-1");
			if (body != null)
				message.Content = Json(body);
			return client.SendAsync(message);
		}

		private static Task<HttpResponseMessage> Approval(HttpClient client, string action, object body) =>
			client.PostAsync("/api/v4/MfaApproval/" + action, Json(body));

		[Test]
		public async Task A_responder_approval_completes_a_sign_in_on_another_app()
		{
			var (passkey, authenticator) = await ResponderApprover();
			await WithServer(async client =>
			{
				var (_, started) = await Send(await PasswordGrant(client));
				started.Value<string>("mfa_methods").Should().Be("totp passkey passkey_approval");
				started.Value<string>("mfa_enrolled").Should().Be("totp passkey_approval");
				var transaction = started.Value<string>("mfa_transaction");

				var (requestStatus, requested) = await Send(await Approval(client, "Request", new { Purpose = "login", Transaction = transaction }));
				requestStatus.Should().Be(HttpStatusCode.OK, requested.ToString());
				var approvalId = requested["Data"]!.Value<string>("ApprovalRequestId");
				var number = requested["Data"]!.Value<string>("MatchNumber");
				_novu.Verify(n => n.SendUserNotification("Sign-in approval requested", "Open Resgrid Responder to review.", UserId, "DEPT", "NA:" + approvalId,
					It.IsAny<string>()), Times.Once);

				(await Send(await Post(client, "CompleteApproval", new { Transaction = transaction, ApprovalRequestId = approvalId }))).Body.Value<string>("type")
					.Should().Be("approval_pending");
				(await Send(await Approval(client, "Status", new { ApprovalRequestId = approvalId, Transaction = transaction }))).Body["Data"]!
					.Value<string>("State").Should().Be("pending");

				// Responder: review, then approve with the number from the Unit screen and the Responder passkey.
				var (pendingStatus, pending) = await Send(await AsResponder(client, HttpMethod.Get, "Pending"));
				pendingStatus.Should().Be(HttpStatusCode.OK, pending.ToString());
				pending["Data"]!.Value<string>("ApprovalRequestId").Should().Be(approvalId);
				pending["Data"]!.Value<string>("RequestingApp").Should().Be("unit");
				pending["Data"]!.Value<string>("Purpose").Should().Be("login");
				pending["Data"]!.Children<JProperty>().Select(field => field.Value.ToString()).Should().NotContain(number,
					"the number is read from the requesting screen, never sent to Responder");

				var (_, options) = await Send(await AsResponder(client, HttpMethod.Post, "Options", new { ApprovalRequestId = approvalId }));
				var requestId = options["Data"]!.Value<string>("RequestId");
				var assertion = JObject.Parse(authenticator.Assert(options["Data"]!["Options"]!.ToString(),
					PasskeyProviderTests.Origin(UserSessionClientApplication.Responder), passkey.UserHandle));

				var wrong = number == "42" ? "43" : "42";
				(await Send(await AsResponder(client, HttpMethod.Post, "Approve",
					new { ApprovalRequestId = approvalId, MatchNumber = wrong, RequestId = requestId, Credential = assertion }))).Body.Value<string>("type")
					.Should().Be("approval_number_mismatch");
				var (approveStatus, approved) = await Send(await AsResponder(client, HttpMethod.Post, "Approve",
					new { ApprovalRequestId = approvalId, MatchNumber = number, RequestId = requestId, Credential = assertion }));
				approveStatus.Should().Be(HttpStatusCode.OK, approved.ToString());

				// Unit: completes through its own transaction.
				(await Send(await Approval(client, "Status", new { ApprovalRequestId = approvalId, Transaction = transaction }))).Body["Data"]!
					.Value<string>("State").Should().Be("approved");
				var (completeStatus, completed) = await Send(await Post(client, "CompleteApproval", new { Transaction = transaction, ApprovalRequestId = approvalId }));
				completeStatus.Should().Be(HttpStatusCode.OK, completed.ToString());
				var (tokenStatus, tokens) = await Send(await Redeem(client, transaction, completed["Data"]!.Value<string>("CompletionCode")));
				tokenStatus.Should().Be(HttpStatusCode.OK, tokens.ToString());

				var session = _issued.Single();
				session.ClientApplication.Should().Be(UserSessionClientApplication.Unit);
				session.LoginMfaMethod.Should().Be(MfaEvidenceMethod.PasskeyApproval);
				session.LoginMfaFactorReference.Should().Be("approval:pk-responder:responder-1");
				_evidence.Rows.Single(e => e.Kind == (int)MfaEvidenceKind.SecondFactor).Method.Should().Be((int)MfaEvidenceMethod.PasskeyApproval);
				_approvalRows.Rows.Single().RequestState.Should().Be(MfaApprovalRequestState.Consumed);
			});
		}

		/// <summary>Password, then Responder approval, then Authentication/CompleteApproval: the transaction and its completion code.</summary>
		private async Task<(string Transaction, string CompletionCode)> ApprovedSignIn(HttpClient client, UserPasskey passkey, SoftPasskeyAuthenticator authenticator)
		{
			var transaction = await BeginTransaction(client);
			var (_, requested) = await Send(await Approval(client, "Request", new { Purpose = "login", Transaction = transaction }));
			var approvalId = requested["Data"]!.Value<string>("ApprovalRequestId");
			var (_, options) = await Send(await AsResponder(client, HttpMethod.Post, "Options", new { ApprovalRequestId = approvalId }));
			var assertion = JObject.Parse(authenticator.Assert(options["Data"]!["Options"]!.ToString(),
				PasskeyProviderTests.Origin(UserSessionClientApplication.Responder), passkey.UserHandle));
			(await AsResponder(client, HttpMethod.Post, "Approve", new
			{
				ApprovalRequestId = approvalId, MatchNumber = requested["Data"]!.Value<string>("MatchNumber"),
				RequestId = options["Data"]!.Value<string>("RequestId"), Credential = assertion
			})).StatusCode.Should().Be(HttpStatusCode.OK);
			var (_, completed) = await Send(await Post(client, "CompleteApproval", new { Transaction = transaction, ApprovalRequestId = approvalId }));
			return (transaction, completed["Data"]!.Value<string>("CompletionCode"));
		}

		[Test]
		public async Task An_approval_whose_responder_signed_out_before_redemption_signs_nobody_in()
		{
			var (passkey, authenticator) = await ResponderApprover();
			await WithServer(async client =>
			{
				var (transaction, code) = await ApprovedSignIn(client, passkey, authenticator);
				_responderSessions.Single().State = (int)UserSessionState.Revoked;

				(await Send(await Redeem(client, transaction, code))).Body.Value<string>("error").Should().Be("session_revoked");
				_issued.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Not_me_in_responder_ends_the_sign_in_and_suspends_approval()
		{
			await ResponderApprover();
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);
				var (_, requested) = await Send(await Approval(client, "Request", new { Purpose = "login", Transaction = transaction }));
				var approvalId = requested["Data"]!.Value<string>("ApprovalRequestId");

				var (denyStatus, denied) = await Send(await AsResponder(client, HttpMethod.Post, "Deny", new { ApprovalRequestId = approvalId, Reason = "not_me" }));
				denyStatus.Should().Be(HttpStatusCode.OK, denied.ToString());

				(await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "123456" }))).Body.Value<string>("type")
					.Should().Be("too_many_attempts", "the sign-in someone else started is over");

				var another = await BeginTransaction(client);
				var (suspendedStatus, suspended) = await Send(await Approval(client, "Request", new { Purpose = "login", Transaction = another }));
				suspendedStatus.Should().Be(HttpStatusCode.TooManyRequests);
				suspended.Value<string>("type").Should().Be("approval_suspended");
				(await Send(await Post(client, "CompleteTotp", new { Transaction = another, Code = "123456" }))).Status
					.Should().Be(HttpStatusCode.OK, "every other method still works");
			});
		}

		[Test]
		public async Task Approval_requests_need_the_requesters_own_transaction_or_session()
		{
			await ResponderApprover();
			await WithServer(async client =>
			{
				(await Send(await Approval(client, "Request", new { Purpose = "step_up", Operation = MfaStepUpOperations.ChatExport }))).Body.Value<string>("type")
					.Should().Be("session_required");
				(await Send(await Approval(client, "Request", new { Purpose = "login", Transaction = "not-a-transaction" }))).Body.Value<string>("type")
					.Should().Be("mfa_transaction_invalid");
				(await Send(await Approval(client, "Request", new { Purpose = "sudo" }))).Body.Value<string>("type").Should().Be("invalid_request");
				(await Send(await client.GetAsync("/api/v4/MfaApproval/Pending"))).Body.Value<string>("type").Should().Be("session_required",
					"only a signed-in Responder reviews requests");

				var transaction = await BeginTransaction(client);
				var (_, requested) = await Send(await Approval(client, "Request", new { Purpose = "login", Transaction = transaction }));
				var approvalId = requested["Data"]!.Value<string>("ApprovalRequestId");
				var otherTransaction = await BeginTransaction(client);
				(await Send(await Approval(client, "Status", new { ApprovalRequestId = approvalId, Transaction = otherTransaction }))).Body.Value<string>("type")
					.Should().Be("approval_expired", "another sign-in cannot read it");
				(await Send(await Approval(client, "Cancel", new { ApprovalRequestId = approvalId, Transaction = transaction }))).Body["Data"]!
					.Value<string>("State").Should().Be("canceled");
				(await Send(await AsResponder(client, HttpMethod.Get, "Pending"))).Body["Data"]!.Type.Should().Be(JTokenType.Null);
			});
		}

		// ---- Setup transaction and factor recovery (slice 12) ------------------------------------------------------

		/// <summary>The current code of the key the host stages for every new authenticator.</summary>
		private static string NewAuthenticatorCode() => Resgrid.Repositories.DataRepository.Stores.TotpCalculator.ComputeCode(
			Resgrid.Repositories.DataRepository.Stores.TotpCalculator.Base32Decode(NewAuthenticatorKey),
			Resgrid.Repositories.DataRepository.Stores.TotpCalculator.CurrentTimeStep(DateTime.UtcNow)).ToString("D6");

		private static Task<HttpResponseMessage> Recovery(HttpClient client, string action, object body) =>
			client.PostAsync("/api/v4/AccountSecurity/" + action, Json(body));

		private void NoticeQueued(SecurityNoticeKind kind, Times times) =>
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.UserId == UserId && r.Kind == kind), It.IsAny<CancellationToken>()), times);

		[Test]
		public async Task A_member_who_must_have_mfa_sets_up_an_authenticator_inside_the_sign_in()
		{
			_twoFactor = false;
			_policy.RequireMfa = true;
			TwoFactorConfig.RequireMfaEnforcementEnabled = true;
			await WithServer(async client =>
			{
				var (legacyStatus, legacy) = await Send(await PasswordGrant(client, transaction: false));
				legacyStatus.Should().Be(HttpStatusCode.BadRequest);
				legacy.ContainsKey("mfa_setup_transaction").Should().BeFalse("older builds keep the enrollment error alone");

				var (_, refused) = await Send(await PasswordGrant(client));
				refused.Value<string>("error").Should().Be("mfa_enrollment_required");
				refused.ContainsKey("access_token").Should().BeFalse();
				var setup = refused.Value<string>("mfa_setup_transaction");
				setup.Should().HaveLength(43);

				var (optionsStatus, options) = await Send(await Post(client, "TotpSetupOptions", new { Transaction = setup }));
				optionsStatus.Should().Be(HttpStatusCode.OK, options.ToString());
				options["Data"]!.Value<string>("SharedKey").Replace(" ", "").ToUpperInvariant().Should().Be(NewAuthenticatorKey);
				options["Data"]!.Value<string>("AuthenticatorUri").Should().Contain("secret=" + NewAuthenticatorKey);
				_activeKey.Should().NotBe(NewAuthenticatorKey, "a staged key is not the authenticator until its code verifies");

				(await Send(await Post(client, "CompleteTotpSetup", new { Transaction = setup, Code = "000000" }))).Body.Value<string>("type")
					.Should().Be("invalid_totp");
				_twoFactor.Should().BeFalse();

				var (completeStatus, completed) = await Send(await Post(client, "CompleteTotpSetup", new { Transaction = setup, Code = NewAuthenticatorCode() }));
				completeStatus.Should().Be(HttpStatusCode.OK, completed.ToString());
				completed["Data"]!["RecoveryCodes"]!.Values<string>().Should().Equal("NEWCD-00001", "NEWCD-00002");
				_activeKey.Should().Be(NewAuthenticatorKey);
				_twoFactor.Should().BeTrue();
				NoticeQueued(SecurityNoticeKind.TotpEnabled, Times.Once());

				var (tokenStatus, tokens) = await Send(await Redeem(client, setup, completed["Data"]!.Value<string>("CompletionCode")));
				tokenStatus.Should().Be(HttpStatusCode.OK, tokens.ToString());
				_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.Totp, "the new authenticator is the sign-in's second factor");

				(await Send(await Post(client, "TotpSetupOptions", new { Transaction = setup }))).Body.Value<string>("type")
					.Should().Be("mfa_transaction_invalid", "the setup transaction is spent");
			});
		}

		[Test]
		public async Task A_setup_transaction_is_not_offered_to_an_account_that_already_has_an_authenticator()
		{
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);
				(await Send(await Post(client, "TotpSetupOptions", new { Transaction = transaction }))).Body.Value<string>("type")
					.Should().Be("mfa_method_not_allowed", "replacing an authenticator needs the account's own authority");
			});
		}

		[Test]
		public async Task A_lost_authenticator_is_replaced_through_a_restricted_recovery()
		{
			var (lost, _) = await RegisteredUnitPasskey();
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);
				(await Send(await Recovery(client, "BeginFactorRecovery", new { Transaction = transaction, Code = "WRONG-00000" }))).Body.Value<string>("type")
					.Should().Be("invalid_recovery_code");

				var (beginStatus, begun) = await Send(await Recovery(client, "BeginFactorRecovery", new { Transaction = transaction, Code = "ABCDE-12345" }));
				beginStatus.Should().Be(HttpStatusCode.OK, begun.ToString());
				var recovery = begun["Data"]!.Value<string>("Transaction");
				recovery.Should().HaveLength(43);
				begun["Data"]!["Passkeys"]!.Select(p => p.Value<string>("PasskeyId")).Should().Contain(lost.UserPasskeyId);
				NoticeQueued(SecurityNoticeKind.RecoveryCodeUsed, Times.Once());

				(await Send(await Post(client, "CompleteTotp", new { Transaction = transaction, Code = "123456" }))).Body.Value<string>("type")
					.Should().Be("too_many_attempts", "the sign-in that started recovery issues nothing");
				(await Send(await Redeem(client, recovery, "anything"))).Body.Value<string>("error")
					.Should().Be("mfa_transaction_invalid", "a recovery secret is no sign-in");

				(await Send(await Recovery(client, "FactorRecoveryStatus", new { Transaction = recovery }))).Body["Data"]!.Value<string>("State")
					.Should().Be("pending");
				(await Send(await Recovery(client, "CompleteFactorRecovery", new { Transaction = recovery, Code = NewAuthenticatorCode() }))).Body
					.Value<string>("type").Should().Be("setup_expired", "nothing is staged yet");

				(await Send(await Recovery(client, "PrepareReplacement", new { Transaction = recovery }))).Status.Should().Be(HttpStatusCode.OK);
				_activeKey.Should().NotBe(NewAuthenticatorKey, "the old authenticator works until the recovery completes");
				(await Send(await Recovery(client, "CompleteFactorRecovery", new { Transaction = recovery, Code = "000000" }))).Body
					.Value<string>("type").Should().Be("invalid_totp");
				(await Send(await Recovery(client, "CompleteFactorRecovery", new { Transaction = recovery, Code = NewAuthenticatorCode(),
					RemovePasskeyIds = new[] { "someone-elses" } }))).Body.Value<string>("type").Should().Be("passkey_not_found");

				var (completeStatus, completed) = await Send(await Recovery(client, "CompleteFactorRecovery",
					new { Transaction = recovery, Code = NewAuthenticatorCode(), RemovePasskeyIds = new[] { lost.UserPasskeyId } }));
				completeStatus.Should().Be(HttpStatusCode.OK, completed.ToString());
				completed["Data"]!.Value<bool>("SignInAgain").Should().BeTrue();
				completed["Data"]!["RecoveryCodes"]!.Values<string>().Should().Equal("NEWCD-00001", "NEWCD-00002");

				_activeKey.Should().Be(NewAuthenticatorKey);
				_user.AuthenticationGeneration.Should().Be(5, "every token and grant from before the recovery is void");
				_sessions.Verify(s => s.RevokeAllAfterCredentialChangeAsync(UserId, UserId, UserSessionRevocationReason.MfaChanged, It.IsAny<DateTime>(),
					It.IsAny<CancellationToken>()), Times.Once);
				var removed = await _passkeys.GetAsync(lost.UserPasskeyId);
				removed.IsActive.Should().BeFalse();
				removed.RevocationReason.Should().Be((int)PasskeyRevocationReason.FactorRecovery);
				NoticeQueued(SecurityNoticeKind.FactorRecoveryCompleted, Times.Once());

				(await Send(await Recovery(client, "CompleteFactorRecovery", new { Transaction = recovery, Code = NewAuthenticatorCode() }))).Body
					.Value<string>("type").Should().NotBeNull("a recovery completes once");
				_issued.Should().BeEmpty("recovery signs nobody in: the user signs in normally afterwards");
			});
		}

		[Test]
		public async Task A_canceled_recovery_changes_nothing_and_its_recovery_code_stays_spent()
		{
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);
				var (_, begun) = await Send(await Recovery(client, "BeginFactorRecovery", new { Transaction = transaction, Code = "ABCDE-12345" }));
				var recovery = begun["Data"]!.Value<string>("Transaction");

				(await Send(await Recovery(client, "CancelFactorRecovery", new { Transaction = recovery }))).Body["Data"]!.Value<string>("State")
					.Should().Be("canceled");
				(await Send(await Recovery(client, "PrepareReplacement", new { Transaction = recovery }))).Body.Value<string>("type")
					.Should().Be("recovery_transaction_invalid");
				_activeKey.Should().NotBe(NewAuthenticatorKey);
				_user.AuthenticationGeneration.Should().Be(4);
				_users.Verify(m => m.RedeemTwoFactorRecoveryCodeAsync(It.IsAny<IdentityUser>(), "ABCDE-12345"), Times.Once);
			});
		}

		[Test]
		public async Task A_recovery_is_bound_to_the_app_that_started_it_and_to_the_account_state()
		{
			await WithServer(async client =>
			{
				var transaction = await BeginTransaction(client);
				var (_, begun) = await Send(await Recovery(client, "BeginFactorRecovery", new { Transaction = transaction, Code = "ABCDE-12345" }));
				var recovery = begun["Data"]!.Value<string>("Transaction");

				var other = new HttpRequestMessage(HttpMethod.Post, "/api/v4/AccountSecurity/FactorRecoveryStatus") { Content = Json(new { Transaction = recovery }) };
				other.Headers.Remove("X-Resgrid-Client");
				client.DefaultRequestHeaders.Remove("X-Resgrid-Client");
				other.Headers.Add("X-Resgrid-Client", "responder");
				(await Send(await client.SendAsync(other))).Body.Value<string>("type").Should().Be("recovery_transaction_invalid");
				client.DefaultRequestHeaders.Add("X-Resgrid-Client", "unit");

				_user.AuthenticationGeneration = 5;
				(await Send(await Recovery(client, "FactorRecoveryStatus", new { Transaction = recovery }))).Body.Value<string>("type")
					.Should().Be("session_revoked", "a password change since voids the recovery");
			});
		}

		[Test]
		public async Task Discovery_names_the_department_and_whether_brokered_sso_is_available()
		{
			await WithServer(async client =>
			{
				var (status, body) = await Send(await client.GetAsync("/api/v4/connect/sso-config?departmentCode=DEPT"));
				status.Should().Be(HttpStatusCode.OK, body.ToString());
				body["Data"]!.Value<int>("DepartmentId").Should().Be(DepartmentId);
				body["Data"]!.Value<string>("DepartmentToken").Should().Be("enc:42:DEPT");
				body["Data"]!.Value<bool>("BrokeredSsoAvailable").Should().BeTrue();

				SsoConfig.BrokeredSsoEnabled = false;
				(await Send(await client.GetAsync("/api/v4/connect/sso-config?departmentCode=DEPT"))).Body["Data"]!.Value<bool>("BrokeredSsoAvailable").Should().BeFalse();
			});
		}
	}
}
