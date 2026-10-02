using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OpenIddict.Abstractions;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 13 over real HTTP: the real session validation middleware, routing and the shared-session
	/// endpoints. A locked shared session reaches only its status, lock, unlock and end-shift endpoints; everything else gets
	/// <c>shared_session_locked</c>, and the unlock resumes the same session.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SharedSessionApiTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 22;
		private const string Code = "123456";

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = DateTime.UtcNow;
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		/// <summary>Stands in for OpenIddict validation: <c>Authorization: Test {sessionId}</c> is a valid token for that session.</summary>
		private sealed class TestTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public TestTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
				: base(options, logger, encoder)
			{
			}

			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				var header = Request.Headers.Authorization.ToString();
				if (!header.StartsWith("Test ", StringComparison.Ordinal))
					return Task.FromResult(AuthenticateResult.NoResult());

				var identity = new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.NameIdentifier, UserId), new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ClaimTypes.Name, "user1"),
					new Claim(SessionClaimTypes.SessionId, header.Substring(5)), new Claim(SessionClaimTypes.AuthenticationGeneration, "4"),
					new Claim(OpenIddictConstants.Claims.IssuedAt, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString())
				}, Scheme.Name);
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
			}
		}

		private Clock _clock;
		private InMemoryUserSessionsRepository _rows;
		private InMemoryMfaEvidenceRepository _evidenceRows;
		private DepartmentSecurityPolicy _policy;
		private IdentityUser _user;
		private List<SystemAudit> _audited;
		private IHttpContextAccessor _previousAccessor;
		private int _failedCodes;
		private int _codeChecks;
		private bool _totpEnrolled;
		private bool _federatedAvailable;
		private Mock<ISsoBrokerService> _broker;
		private SsoLoginTransaction _redeemed;

		private static DepartmentSsoConfig Tested() => new()
		{
			DepartmentSsoConfigId = "cfg", DepartmentId = DepartmentId, SsoProviderType = (int)SsoProviderType.Oidc, IsEnabled = true,
			FederatedMfaMappingJson = new FederatedMfaMapping { AcceptAmr = new() { "mfa" } }.Serialize(), FederatedMfaMappingVersion = 3,
			FederatedMfaTestedVersion = 3
		};

		[SetUp]
		public void SetUp()
		{
			_clock = new Clock();
			_rows = new InMemoryUserSessionsRepository();
			_evidenceRows = new InMemoryMfaEvidenceRepository();
			_policy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId };
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_audited = new List<SystemAudit>();
			_failedCodes = 0;
			_codeChecks = 0;
			_totpEnrolled = true;
			_federatedAvailable = false;
			_broker = new Mock<ISsoBrokerService>();
			_broker.Setup(b => b.BeginAsync(It.IsAny<SsoBeginRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new SsoBeginResult { Outcome = SsoBrokerOutcome.Succeeded, AuthorizeUrl = "https://idp.example.test/authorize", TransactionId = "sso-1",
					ExpiresInSeconds = 600 });
			_broker.Setup(b => b.RedeemAsync("sso-1", "code", "verifier", UserSessionClientApplication.Unit, It.IsAny<CancellationToken>(),
					It.Is<SsoTransactionPurpose[]>(p => p.SequenceEqual(new[] { SsoTransactionPurpose.StepUp }))))
				.ReturnsAsync(() => _redeemed == null ? SsoRedemptionResult.Of(SsoBrokerOutcome.AlreadyUsed) : SsoRedemptionResult.Of(SsoBrokerOutcome.Succeeded, _redeemed));
			_previousAccessor = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor;
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor();
		}

		[TearDown]
		public void TearDown() => Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private UserSession Seed(bool shared = true)
		{
			var session = new UserSession
			{
				UserSessionId = Guid.NewGuid().ToString("N"), UserId = UserId, DepartmentId = DepartmentId, AuthenticationGeneration = 4,
				State = (int)UserSessionState.Active, ClientApplication = (int)UserSessionClientApplication.Unit, DeviceName = "Engine 7 tablet",
				AuthenticationMethod = (int)UserSessionAuthenticationMethod.LocalPassword, CreatedOn = _clock.Now, LastActiveOn = _clock.Now,
				ExpiresOn = _clock.Now.AddHours(12), SharedMode = shared, SharedModeSource = shared ? (int)SharedModeSource.InstallationRequested : 0,
				SharedIdleLockMinutes = shared ? 5 : null, LastOperatorActivityOn = shared ? _clock.Now : null
			};
			_rows.Add(session);
			return session;
		}

		private async Task WithServer(Func<HttpClient, Task> test)
		{
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddApiVersioning();
			var scheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
			builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions, TestTokenHandler>(scheme, _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddControllers().AddApplicationPart(typeof(SessionsController).Assembly)
				.AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());

			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
			sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DepartmentSsoConfig>());
			sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _federatedAvailable);
			sso.Setup(s => s.GetTestedFederatedMfaConfigAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _federatedAvailable ? Tested() : null);
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(UserId, DepartmentId, true))
				.ReturnsAsync(new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId });
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(true);
			gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			var audits = new Mock<ISystemAuditsService>();
			audits.Setup(a => a.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()))
				.Callback((SystemAudit audit, CancellationToken _) => { lock (_audited) _audited.Add(audit); })
				.ReturnsAsync((SystemAudit audit, CancellationToken _) => audit);

			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(() => _user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(() => _totpEnrolled);
			users.Setup(m => m.IsLockedOutAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
			users.Setup(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string _, string code) => { Interlocked.Increment(ref _codeChecks); return code == Code; });
			users.Setup(m => m.AccessFailedAsync(It.IsAny<IdentityUser>())).Callback(() => _failedCodes++).ReturnsAsync(IdentityResult.Success);
			users.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			var attempts = 0L;
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(() => Interlocked.Increment(ref attempts));

			var sessions = new UserSessionService(_rows, identity.Object, Mock.Of<IIdentityRepository>(), departments.Object, sso.Object,
				new ClientSessionMetadataParser(), Mock.Of<IIpLocationProvider>(), gates.Object, audits.Object, Mock.Of<ISessionEventPublisher>(), _clock);
			var evidence = new MfaEvidenceService(_evidenceRows, new InMemoryUserMfaStateRepository(), Mock.Of<IUserPasskeyRepository>(), _rows,
				new InMemoryMfaActivityRepository(), _clock);
			builder.Services.AddSingleton<IUserSessionService>(sessions);
			builder.Services.AddSingleton<ISharedSessionService>(new SharedSessionService(_rows, sessions, evidence, sso.Object, audits.Object,
				Mock.Of<IMfaActivityService>(), _clock));
			builder.Services.AddSingleton(audits.Object);
			builder.Services.AddSingleton(users.Object);
			builder.Services.AddSingleton<IMfaPolicyService>(new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), gates.Object));
			builder.Services.AddSingleton(Mock.Of<IPasskeyService>());
			builder.Services.AddSingleton(Mock.Of<IMfaApprovalService>());
			builder.Services.AddSingleton(cache.Object);
			builder.Services.AddSingleton(_broker.Object);
			builder.Services.AddSingleton(sso.Object);
			builder.Services.AddSingleton(departments.Object);

			await using var app = builder.Build();
			app.UseRouting();
			app.UseAuthentication();
			app.UseMiddleware<Resgrid.Web.Services.Middleware.SessionValidationMiddleware>();
			app.UseAuthorization();
			app.MapControllers();
			try
			{
				await app.StartAsync();
				using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
				await test(client);
			}
			finally
			{
				await app.StopAsync();
			}
		}

		private static HttpRequestMessage As(UserSession session, HttpMethod method, string path, object body = null, bool activity = false)
		{
			var request = new HttpRequestMessage(method, "/api/v4/" + path);
			request.Headers.TryAddWithoutValidation("Authorization", "Test " + session.UserSessionId);
			if (activity)
				request.Headers.Add(SharedSessionRules.ActivityHeader, "1");
			if (body != null)
				request.Content = new StringContent(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json");
			return request;
		}

		private static async Task<(HttpStatusCode Status, JObject Body, HttpResponseMessage Response)> Send(HttpClient client, HttpRequestMessage request)
		{
			var response = await client.SendAsync(request);
			var text = await response.Content.ReadAsStringAsync();
			return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? new JObject() : JToken.Parse(text) as JObject ?? new JObject(), response);
		}

		[Test]
		public async Task A_locked_shared_session_reaches_only_its_own_status_unlock_and_end_shift()
		{
			var session = Seed();
			await WithServer(async client =>
			{
				(await Send(client, As(session, HttpMethod.Get, "sessions"))).Status.Should().Be(HttpStatusCode.OK, "unlocked, it is an ordinary session");

				var (lockStatus, locked, _) = await Send(client, As(session, HttpMethod.Post, "sessions/lock"));
				lockStatus.Should().Be(HttpStatusCode.OK, locked.ToString());
				locked["Data"]!.Value<long>("LockVersion").Should().Be(1);

				var (refused, body, response) = await Send(client, As(session, HttpMethod.Get, "sessions"));
				refused.Should().Be(HttpStatusCode.Unauthorized);
				body.Value<string>("error").Should().Be("shared_session_locked");
				body.Value<long>("lock_version").Should().Be(1);
				response.Headers.WwwAuthenticate.ToString().Should().Contain("shared_session_locked");
				(await Send(client, As(session, HttpMethod.Delete, "sessions/" + session.UserSessionId))).Status.Should().Be(HttpStatusCode.Unauthorized,
					"revoking other things is not a locked-session endpoint");

				var (currentStatus, current, _) = await Send(client, As(session, HttpMethod.Get, "Sessions/Current"));
				currentStatus.Should().Be(HttpStatusCode.OK, "the path check ignores case like routing does");
				current["Data"]!.Value<bool>("Locked").Should().BeTrue();
				current["Data"]!.Value<string>("LockReason").Should().Be("explicit");

				var (optionsStatus, options, _) = await Send(client, As(session, HttpMethod.Post, "sessions/unlock-options", new { LockVersion = 1 }));
				optionsStatus.Should().Be(HttpStatusCode.OK, options.ToString());
				options["Data"]!["Methods"]!.Values<string>().Should().Equal("totp");
				options["Data"]!.Value<string>("Operator").Should().Be("user1");

				var (staleStatus, stale, _) = await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock",
					new { LockVersion = 0, Method = "totp", Code }));
				staleStatus.Should().Be(HttpStatusCode.Conflict);
				stale.Value<string>("type").Should().Be("shared_session_lock_changed");
				_codeChecks.Should().Be(0, "a stale unlock is refused before any code is checked or its time step spent");

				var (wrongStatus, wrong, _) = await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock",
					new { LockVersion = 1, Method = "totp", Code = "000000" }));
				wrongStatus.Should().Be(HttpStatusCode.Unauthorized);
				wrong.Value<string>("type").Should().Be("invalid_totp");
				_failedCodes.Should().Be(1, "a wrong code counts toward the account lockout");
				_rows.Row(session.UserSessionId).IsLocked.Should().BeTrue();

				var (unlockStatus, unlocked, _) = await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock",
					new { LockVersion = 1, Method = "totp", Code }));
				unlockStatus.Should().Be(HttpStatusCode.OK, unlocked.ToString());
				unlocked["Data"]!.Value<bool>("Locked").Should().BeFalse();
				unlocked["Data"]!.Value<long>("LockVersion").Should().Be(1);
				_evidenceRows.Rows.Single().Purpose.Should().Be((int)MfaEvidencePurpose.SharedUnlock);
				_audited.Should().Contain(a => a.Type == (int)SystemAuditTypes.SharedSessionUnlocked && a.Successful)
					.And.Contain(a => a.Type == (int)SystemAuditTypes.SharedSessionUnlocked && !a.Successful);

				(await Send(client, As(session, HttpMethod.Get, "sessions"))).Status.Should().Be(HttpStatusCode.OK, "the same session resumed");
				(await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock", new { LockVersion = 1, Method = "totp", Code })))
					.Body.Value<string>("type").Should().Be("shared_session_not_locked");
			});
		}

		[Test]
		public async Task The_server_idle_deadline_locks_the_session_and_only_marked_requests_count_as_activity()
		{
			var session = Seed();
			await WithServer(async client =>
			{
				_clock.Now = _clock.Now.AddMinutes(4);
				(await Send(client, As(session, HttpMethod.Get, "sessions"))).Status.Should().Be(HttpStatusCode.OK);
				_rows.Row(session.UserSessionId).LastOperatorActivityOn.Should().Be(_clock.Now.AddMinutes(-4), "polling is not operator activity");

				(await Send(client, As(session, HttpMethod.Get, "sessions", activity: true))).Status.Should().Be(HttpStatusCode.OK);
				_rows.Row(session.UserSessionId).LastOperatorActivityOn.Should().Be(_clock.Now);

				_clock.Now = _clock.Now.AddMinutes(5);
				var (status, body, _) = await Send(client, As(session, HttpMethod.Get, "sessions", activity: true));
				status.Should().Be(HttpStatusCode.Unauthorized, "the deadline passed even though the client never locked");
				body.Value<string>("error").Should().Be("shared_session_locked");
				_rows.Row(session.UserSessionId).LockReason.Should().Be((int)SharedSessionLockReason.Idle);
				_audited.Should().ContainSingle(a => a.Type == (int)SystemAuditTypes.SharedSessionLocked);
			});
		}

		[Test]
		public async Task End_shift_ends_the_session_for_every_later_request()
		{
			var session = Seed();
			await WithServer(async client =>
			{
				await Send(client, As(session, HttpMethod.Post, "sessions/lock"));
				var (status, ended, _) = await Send(client, As(session, HttpMethod.Post, "sessions/end-shift", new { SwitchOperator = true }));
				status.Should().Be(HttpStatusCode.OK, "a locked session can still end its shift");
				ended["Data"]!.Value<bool>("Ended").Should().BeTrue();
				_rows.Row(session.UserSessionId).RevocationReason.Should().Be((int)UserSessionRevocationReason.OperatorSwitched);

				var (after, body, response) = await Send(client, As(session, HttpMethod.Get, "sessions/current"));
				after.Should().Be(HttpStatusCode.Unauthorized);
				response.Headers.WwwAuthenticate.ToString().Should().NotContain("shared_session", "an ended session is simply invalid");
				body.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Personal_sessions_cannot_lock_and_a_passed_shift_is_reported_as_such()
		{
			var personal = Seed(shared: false);
			var shared = Seed();
			await WithServer(async client =>
			{
				var (status, body, _) = await Send(client, As(personal, HttpMethod.Post, "sessions/lock"));
				status.Should().Be(HttpStatusCode.Conflict);
				body.Value<string>("type").Should().Be("not_shared_session");
				var (currentStatus, current, _) = await Send(client, As(personal, HttpMethod.Get, "sessions/current"));
				currentStatus.Should().Be(HttpStatusCode.OK);
				current["Data"]!.Value<bool>("Shared").Should().BeFalse();

				_clock.Now = _clock.Now.AddHours(12);
				var (expired, expiredBody, response) = await Send(client, As(shared, HttpMethod.Get, "sessions/current"));
				expired.Should().Be(HttpStatusCode.Unauthorized);
				expiredBody.Value<string>("error").Should().Be("shared_session_expired");
				response.Headers.WwwAuthenticate.ToString().Should().Contain("shared_session_expired");
			});
		}

		[Test]
		public async Task An_sso_operator_unlocks_with_the_identity_providers_mfa_begun_during_this_lock()
		{
			// Plan section 12.5.3: a permitted provider step-up unlocks the same session. The operator has no Resgrid factor.
			_totpEnrolled = false;
			_federatedAvailable = true;
			_policy.AllowFederatedMfaForLoginMfa = true;
			var session = Seed();
			await WithServer(async client =>
			{
				await Send(client, As(session, HttpMethod.Post, "sessions/lock"));
				var lockedOn = _rows.Row(session.UserSessionId).LockedOnUtc!.Value;

				var (optionsStatus, options, _) = await Send(client, As(session, HttpMethod.Post, "sessions/unlock-options", new { LockVersion = 1 }));
				optionsStatus.Should().Be(HttpStatusCode.OK, options.ToString());
				options["Data"]!["Methods"]!.Values<string>().Should().Equal("federated");

				var (beginStatus, begun, _) = await Send(client, As(session, HttpMethod.Post, "sessions/unlock-sso", new
				{
					LockVersion = 1, Platform = "ios", ReturnTarget = "resgridunit://sso-return", State = "s", CodeChallenge = new string('a', 43),
					CodeChallengeMethod = "S256"
				}));
				beginStatus.Should().Be(HttpStatusCode.OK, begun.ToString());
				begun["Data"]!.Value<string>("SsoTransactionId").Should().Be("sso-1");
				_broker.Verify(b => b.BeginAsync(It.Is<SsoBeginRequest>(r => r.Purpose == SsoTransactionPurpose.StepUp &&
					r.Operation == SsoLoginTransaction.SharedUnlockOperation && r.SessionId == session.UserSessionId && r.UserId == UserId &&
					r.SharedInstallation && r.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()), Times.Once);

				SsoLoginTransaction Redeemed(DateTime createdOn, string operation = SsoLoginTransaction.SharedUnlockOperation) => new()
				{
					SsoLoginTransactionId = "sso-1", Purpose = (int)SsoTransactionPurpose.StepUp, DepartmentId = DepartmentId, DepartmentSsoConfigId = "cfg",
					Operation = operation, SessionId = session.UserSessionId, ExpectedUserId = UserId, UserId = UserId, AuthenticationGeneration = 4,
					FederatedMappingVersion = 3, FederatedMfaValue = "amr:mfa", CreatedOnUtc = createdOn, AuthenticatedOnUtc = createdOn.AddSeconds(20)
				};
				object Complete() => new { LockVersion = 1, Method = "federated", SsoTransactionId = "sso-1", SsoCode = "code", CodeVerifier = "verifier" };

				_redeemed = Redeemed(lockedOn.AddSeconds(-5));
				var (early, earlyBody, _) = await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock", Complete()));
				early.Should().Be(HttpStatusCode.Unauthorized);
				earlyBody.Value<string>("type").Should().Be("federated_mfa_not_satisfied", "begun before this lock");

				_redeemed = Redeemed(lockedOn.AddSeconds(5), MfaStepUpOperations.SecurityChange);
				(await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock", Complete()))).Body.Value<string>("type")
					.Should().Be("federated_mfa_not_satisfied", "a step-up for another purpose never unlocks");
				_rows.Row(session.UserSessionId).IsLocked.Should().BeTrue();

				_redeemed = Redeemed(lockedOn.AddSeconds(5));
				var (status, unlocked, _) = await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock", Complete()));
				status.Should().Be(HttpStatusCode.OK, unlocked.ToString());
				unlocked["Data"]!.Value<bool>("Locked").Should().BeFalse();
				var evidence = _evidenceRows.Rows.Single();
				evidence.Method.Should().Be((int)MfaEvidenceMethod.Federated);
				evidence.Purpose.Should().Be((int)MfaEvidencePurpose.SharedUnlock);
				evidence.FactorReference.Should().Be("federated:cfg:3");
			});
		}

		[Test]
		public async Task Provider_unlock_is_offered_only_where_the_department_accepts_it()
		{
			_totpEnrolled = false;
			_federatedAvailable = true;
			var session = Seed();
			await WithServer(async client =>
			{
				await Send(client, As(session, HttpMethod.Post, "sessions/lock"));
				(await Send(client, As(session, HttpMethod.Post, "sessions/unlock-options", new { LockVersion = 1 }))).Body["Data"]!["Methods"]!
					.Values<string>().Should().BeEmpty("the department does not accept provider MFA, and there is no Resgrid factor");
				var (status, body, _) = await Send(client, As(session, HttpMethod.Post, "sessions/unlock-sso", new { LockVersion = 1 }));
				status.Should().Be(HttpStatusCode.BadRequest);
				body.Value<string>("type").Should().Be("mfa_method_not_allowed");
				_broker.Verify(b => b.BeginAsync(It.IsAny<SsoBeginRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			});
		}

		[Test]
		public async Task Unlock_attempts_are_limited_per_session()
		{
			var session = Seed();
			var max = PasskeyConfig.SharedUnlockMaxAttempts;
			PasskeyConfig.SharedUnlockMaxAttempts = 2;
			try
			{
				await WithServer(async client =>
				{
					await Send(client, As(session, HttpMethod.Post, "sessions/lock"));
					for (var i = 0; i < 2; i++)
						(await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock", new { LockVersion = 1, Method = "totp", Code = "000000" })))
							.Status.Should().Be(HttpStatusCode.Unauthorized);

					var (status, body, _) = await Send(client, As(session, HttpMethod.Post, "sessions/complete-unlock",
						new { LockVersion = 1, Method = "totp", Code }));
					status.Should().Be(HttpStatusCode.TooManyRequests);
					body.Value<string>("type").Should().Be("too_many_attempts");
					_rows.Row(session.UserSessionId).IsLocked.Should().BeTrue();
				});
			}
			finally
			{
				PasskeyConfig.SharedUnlockMaxAttempts = max;
			}
		}
	}
}
