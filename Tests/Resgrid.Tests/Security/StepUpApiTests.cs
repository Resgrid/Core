using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.StepUp;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Round 10 A5: <c>StepUp/Verify</c> and <c>StepUp/Options</c>, the in-app step-up every Omni variant uses, and the one
	/// <c>step_up_required</c> refusal. Each method must be one the department allows before any secret is looked at; the code and password
	/// count against the shared account lockout and a per-user limit; an SSO re-sign-in must be this session's, account's, generation's and
	/// department's and fresh; protected data answers an ADP grant; and nothing secret is audited.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class StepUpApiTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "unit-session";

		private IHttpContextAccessor _previous;
		private IdentityUser _user;
		private Mock<UserManager<IdentityUser>> _users;
		private Mock<IStepUpPolicyService> _policy;
		private Mock<IMfaEvidenceService> _evidence;
		private List<(MfaEvidenceKind Kind, MfaEvidenceMethod Method, MfaEvidencePurpose Purpose, DateTime At, int? Department)> _recorded;
		private Mock<ISsoBrokerService> _broker;
		private Mock<IExternalIdentityLinkService> _links;
		private Mock<IAdpStepUpService> _adp;
		private Mock<ICacheProvider> _cache;
		private long _attempts;
		private List<SystemAudit> _systemAudits;
		private List<AuditEvent> _audits;
		private StepUpRule _rule;

		[SetUp]
		public void SetUp()
		{
			_previous = ApiClaims._httpContextAccessor;
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4, PasswordHash = "hash" };
			_users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			_users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(_user);
			_users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(true);
			_users.Setup(m => m.IsLockedOutAsync(_user)).ReturnsAsync(false);
			_users.Setup(m => m.HasPasswordAsync(_user)).ReturnsAsync(true);
			_users.Setup(m => m.VerifyTwoFactorTokenAsync(_user, It.IsAny<string>(), "123456")).ReturnsAsync(true);
			_users.Setup(m => m.CheckPasswordAsync(_user, "correct horse")).ReturnsAsync(true);
			_users.Setup(m => m.ResetAccessFailedCountAsync(_user)).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.AccessFailedAsync(_user)).ReturnsAsync(IdentityResult.Success);
			_rule = StepUpTestPolicies.Rule(StepUpArea.Signatures);
			_policy = new Mock<IStepUpPolicyService>();
			_policy.Setup(p => p.GetRuleAsync(DepartmentId, It.IsAny<StepUpArea>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, StepUpArea area, CancellationToken _) => new StepUpRule { Area = area, Required = _rule.Required, Methods = _rule.Methods, WindowMinutes = 15 });
			_recorded = new();
			_evidence = new Mock<IMfaEvidenceService>();
			_evidence.Setup(e => e.RecordAsync(UserId, "sid:" + SessionId, UserSessionClientApplication.Unit, It.IsAny<MfaEvidenceKind>(), It.IsAny<MfaEvidenceMethod>(),
					It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), 4, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string _, string _, UserSessionClientApplication _, MfaEvidenceKind kind, MfaEvidenceMethod method, MfaEvidencePurpose purpose, DateTime at, long _,
					int? department, string _, CancellationToken _) => _recorded.Add((kind, method, purpose, at, department)))
				.Returns(Task.CompletedTask);
			_broker = new Mock<ISsoBrokerService>();
			_links = new Mock<IExternalIdentityLinkService>();
			_links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_adp = new Mock<IAdpStepUpService>();
			_attempts = 0;
			_cache = new Mock<ICacheProvider>();
			_cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(() => ++_attempts);
			_systemAudits = new();
			_audits = new();
		}

		[TearDown]
		public void TearDown() => ApiClaims._httpContextAccessor = _previous;

		private DefaultHttpContext Http(bool withSession = true)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ClaimTypes.Name, "user1")
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			if (withSession)
				http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
				{
					SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4
				};
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;
			return http;
		}

		private StepUpController Api(bool withSession = true, IStepUpCapabilityService capabilities = null)
		{
			var systemAudits = new Mock<ISystemAuditsService>();
			systemAudits.Setup(s => s.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()))
				.Callback((SystemAudit a, CancellationToken _) => _systemAudits.Add(a)).ReturnsAsync((SystemAudit a, CancellationToken _) => a);
			var events = new Mock<IEventAggregator>();
			events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent a) => _audits.Add(a));
			return new StepUpController(_users.Object, _policy.Object, capabilities ?? StepUpTestPolicies.Capabilities(policy: _policy.Object), _evidence.Object,
				Mock.Of<IMfaPolicyService>(), Mock.Of<IPasskeyService>(), Mock.Of<IMfaApprovalService>(), _broker.Object, Mock.Of<IDepartmentSsoService>(), _links.Object,
				_adp.Object, _cache.Object, systemAudits.Object, Mock.Of<IMfaActivityService>(), events.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = Http(withSession) }
			};
		}

		private static ObjectResult Result(IConvertToActionResult result) => (ObjectResult)result.Convert();
		private static string Type(IConvertToActionResult result) => ((ProblemDetails)Result(result).Value).Type;
		private static int? Status(IConvertToActionResult result) => Result(result).StatusCode;
		private static StepUpVerifyResultData Data(ActionResult<StepUpVerifyResult> result) => result.Value.Data;

		private static StepUpVerifyInput Input(string method, string area = "signatures", string code = null, string password = null) => new()
		{
			Area = area, Method = method, Code = code, Password = password
		};

		// ── Contract shape ─────────────────────────────────────────────────────────

		[TestCase(null, "totp")]
		[TestCase("everything", "totp")]
		[TestCase("signatures", "recovery_code")]
		[TestCase("signatures", "sms")]
		public async Task Unknown_areas_and_methods_are_refused_without_counting_an_attempt(string area, string method)
		{
			var result = await Api().Verify(new StepUpVerifyInput { Area = area, Method = method, Code = "123456" }, CancellationToken.None);
			Status(result).Should().Be(StatusCodes.Status400BadRequest);
			_attempts.Should().Be(0);
			_recorded.Should().BeEmpty();
		}

		[Test]
		public async Task A_token_without_a_tracked_session_cannot_hold_a_step_up()
		{
			var result = await Api(withSession: false).Verify(Input("totp", code: "123456"), CancellationToken.None);
			Type(result).Should().Be("session_required");
			Status(result).Should().Be(StatusCodes.Status409Conflict);
		}

		[Test]
		public async Task A_kind_the_department_does_not_allow_is_refused_before_the_password_is_checked()
		{
			var result = await Api().Verify(Input("password", password: "correct horse"), CancellationToken.None);

			Type(result).Should().Be("step_up_method_not_allowed");
			_users.Verify(m => m.CheckPasswordAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()), Times.Never, "no password oracle where the password is not allowed");
			_attempts.Should().Be(0);
		}

		[Test]
		public async Task Attempts_past_the_limit_and_a_locked_account_are_429()
		{
			_attempts = StepUpController.MaxAttempts;
			Status(await Api().Verify(Input("totp", code: "123456"), CancellationToken.None)).Should().Be(StatusCodes.Status429TooManyRequests);

			_attempts = 0;
			_users.Setup(m => m.IsLockedOutAsync(_user)).ReturnsAsync(true);
			Type(await Api().Verify(Input("totp", code: "123456"), CancellationToken.None)).Should().Be("too_many_attempts");
			_users.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		// ── TOTP ───────────────────────────────────────────────────────────────────

		[Test]
		public async Task A_wrong_code_counts_against_the_lockout_and_is_audited_as_failed_without_the_code()
		{
			var result = await Api().Verify(Input("totp", code: "999999"), CancellationToken.None);

			Type(result).Should().Be("invalid_totp");
			Status(result).Should().Be(StatusCodes.Status401Unauthorized);
			_users.Verify(m => m.AccessFailedAsync(_user), Times.Once);
			_recorded.Should().BeEmpty();
			_systemAudits.Should().ContainSingle(a => !a.Successful && a.Type == (int)SystemAuditTypes.TwoFactorStepUpVerified);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.StepUpFailed && a.DepartmentId == DepartmentId);
			_systemAudits.Concat<object>(_audits).Select(a => Newtonsoft.Json.JsonConvert.SerializeObject(a)).Should().NotContain(s => s.Contains("999999"));
		}

		[Test]
		public async Task A_valid_code_is_second_factor_step_up_evidence_for_this_session_and_department()
		{
			var result = await Api().Verify(Input("totp", area: "approvals", code: "123 456"), CancellationToken.None);

			Data(result).Should().Match<StepUpVerifyResultData>(d => d.Area == "approvals" && d.MethodClass == "mfa" && d.GrantToken == null);
			var row = _recorded.Should().ContainSingle().Subject;
			row.Kind.Should().Be(MfaEvidenceKind.SecondFactor);
			row.Method.Should().Be(MfaEvidenceMethod.Totp);
			row.Purpose.Should().Be(MfaEvidencePurpose.StepUp);
			row.Department.Should().Be(DepartmentId);
			DateTime.Parse(Data(result).ExpiresAt).ToUniversalTime().Should().BeCloseTo(row.At.AddMinutes(15), TimeSpan.FromSeconds(1));
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.StepUpCompleted);
		}

		[Test]
		public async Task A_member_without_an_authenticator_is_told_to_enroll()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(false);
			Type(await Api().Verify(Input("totp", code: "123456"), CancellationToken.None)).Should().Be("mfa_enrollment_required");
		}

		// ── Password ───────────────────────────────────────────────────────────────

		[Test]
		public async Task The_password_where_allowed_is_recorded_as_reauthentication_never_as_a_second_factor()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, methods: StepUpMethodClasses.Password);

			var result = await Api().Verify(Input("password", password: "correct horse"), CancellationToken.None);

			Data(result).MethodClass.Should().Be("password");
			var row = _recorded.Should().ContainSingle().Subject;
			row.Kind.Should().Be(MfaEvidenceKind.FirstFactor);
			row.Method.Should().Be(MfaEvidenceMethod.Password);
			row.Purpose.Should().Be(MfaEvidencePurpose.Reauthentication);
			_systemAudits.Should().ContainSingle(a => a.Successful && a.Type == (int)SystemAuditTypes.AccountReauthenticated);
			_systemAudits.Single().Data.Should().NotContain("correct horse");
		}

		[Test]
		public async Task A_wrong_password_counts_against_the_lockout()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, methods: StepUpMethodClasses.Password);
			var result = await Api().Verify(Input("password", password: "wrong"), CancellationToken.None);
			Type(result).Should().Be("invalid_password");
			Status(result).Should().Be(StatusCodes.Status401Unauthorized);
			_users.Verify(m => m.AccessFailedAsync(_user), Times.Once);
			_recorded.Should().BeEmpty();
		}

		[Test]
		public async Task A_member_who_signs_in_only_through_the_provider_has_no_password_to_offer()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, methods: StepUpMethodClasses.Password);
			_links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

			Type(await Api().Verify(Input("password", password: "correct horse"), CancellationToken.None)).Should().Be("password_not_available");
			_users.Verify(m => m.CheckPasswordAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()), Times.Never);
		}

		// ── SSO re-sign-in ─────────────────────────────────────────────────────────

		private SsoLoginTransaction Transaction(Action<SsoLoginTransaction> change = null)
		{
			var transaction = new SsoLoginTransaction
			{
				SsoLoginTransactionId = "tx-1", DepartmentId = DepartmentId, SessionId = SessionId, ExpectedUserId = UserId, UserId = UserId, AuthenticationGeneration = 4,
				AuthenticatedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2), DepartmentSsoConfigId = "cfg-1",
				Purpose = (int)SsoTransactionPurpose.Reauthentication
			};
			change?.Invoke(transaction);
			_broker.Setup(b => b.RedeemAsync("tx-1", "code-1", It.IsAny<string>(), UserSessionClientApplication.Unit, It.IsAny<CancellationToken>(),
					It.Is<SsoTransactionPurpose[]>(p => p.Length == 1 && p[0] == SsoTransactionPurpose.Reauthentication)))
				.ReturnsAsync(new SsoRedemptionResult { Outcome = SsoBrokerOutcome.Succeeded, Transaction = transaction });
			return transaction;
		}

		private static StepUpVerifyInput Sso(string area = "signatures") => new()
		{
			Area = area, Method = "sso", SsoTransactionId = "tx-1", SsoCode = "code-1", CodeVerifier = new string('v', 43)
		};

		[Test]
		public async Task A_fresh_provider_sign_in_for_this_session_is_recorded_at_the_providers_own_time()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, methods: StepUpMethodClasses.Sso);
			var transaction = Transaction();

			var result = await Api().Verify(Sso(), CancellationToken.None);

			Data(result).MethodClass.Should().Be("sso");
			var row = _recorded.Should().ContainSingle().Subject;
			row.Should().Match<(MfaEvidenceKind Kind, MfaEvidenceMethod Method, MfaEvidencePurpose Purpose, DateTime At, int? Department)>(r =>
				r.Kind == MfaEvidenceKind.FirstFactor && r.Method == MfaEvidenceMethod.Sso && r.Purpose == MfaEvidencePurpose.Reauthentication);
			row.At.Should().BeCloseTo(transaction.AuthenticatedOnUtc.Value, TimeSpan.FromMilliseconds(1));
		}

		[TestCase("session")]
		[TestCase("user")]
		[TestCase("expected-user")]
		[TestCase("department")]
		[TestCase("generation")]
		[TestCase("stale")]
		[TestCase("future")]
		[TestCase("no-auth-time")]
		[TestCase("before-begin")]
		public async Task A_provider_sign_in_for_anything_else_or_too_old_is_refused(string why)
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, methods: StepUpMethodClasses.Sso);
			Transaction(t =>
			{
				switch (why)
				{
					case "session": t.SessionId = "another-device"; break;
					case "user": t.UserId = "user-2"; break;
					case "expected-user": t.ExpectedUserId = "user-2"; break;
					case "department": t.DepartmentId = 77; break;
					case "generation": t.AuthenticationGeneration = 3; break;
					case "stale": t.AuthenticatedOnUtc = DateTime.UtcNow.AddMinutes(-16); break;
					case "future": t.AuthenticatedOnUtc = DateTime.UtcNow.AddMinutes(2); break;
					case "no-auth-time": t.AuthenticatedOnUtc = null; break;
					case "before-begin": t.AuthenticatedOnUtc = t.CreatedOnUtc.AddMinutes(-3); break;
				}
			});

			var result = await Api().Verify(Sso(), CancellationToken.None);

			Type(result).Should().Be("sso_reauthentication_not_satisfied");
			Status(result).Should().Be(StatusCodes.Status401Unauthorized);
			_recorded.Should().BeEmpty();
		}

		[Test]
		public async Task A_replayed_provider_code_is_refused_by_the_one_time_redemption()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, methods: StepUpMethodClasses.Sso);
			_broker.Setup(b => b.RedeemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<CancellationToken>(),
				It.IsAny<SsoTransactionPurpose[]>())).ReturnsAsync(SsoRedemptionResult.Of(SsoBrokerOutcome.AlreadyUsed));

			var result = await Api().Verify(Sso(), CancellationToken.None);

			Status(result).Should().Be(StatusCodes.Status401Unauthorized);
			Type(result).Should().Be(SsoBrokerOutcomes.ErrorCode(SsoBrokerOutcome.AlreadyUsed));
			_recorded.Should().BeEmpty();
		}

		[Test]
		public async Task A_provider_sign_in_is_refused_where_only_the_password_is_allowed()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, methods: StepUpMethodClasses.Password);
			Transaction();
			Type(await Api().Verify(Sso(), CancellationToken.None)).Should().Be("step_up_method_not_allowed");
			_broker.Verify(b => b.RedeemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<CancellationToken>(),
				It.IsAny<SsoTransactionPurpose[]>()), Times.Never, "the one-time code is not spent on a refused method");
		}

		// ── Protected data ─────────────────────────────────────────────────────────

		private static AdpGrantIssue Grant => new() { Outcome = AdpGrantOutcome.Issued, GrantId = "grant-1", Token = "token-1", ExpiresOnUtc = DateTime.UtcNow.AddMinutes(15), WindowMinutes = 15 };

		[Test]
		public async Task Protected_data_by_password_records_the_reauthentication_and_answers_the_non_mfa_grant()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.ProtectedData, methods: StepUpMethodClasses.Password);
			_adp.Setup(a => a.IssueForReauthenticationAsync(It.Is<AdpStepUpCaller>(c => c.UserId == UserId && c.DepartmentId == DepartmentId && c.Session.SessionId == SessionId),
				MfaEvidenceMethod.Password, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Grant);

			var result = await Api().Verify(Input("password", area: "protected_data", password: "correct horse"), CancellationToken.None);

			Data(result).Should().Match<StepUpVerifyResultData>(d => d.GrantToken == "token-1" && d.GrantId == "grant-1" && d.MethodClass == "password");
			_recorded.Should().ContainSingle(r => r.Method == MfaEvidenceMethod.Password && r.Purpose == MfaEvidencePurpose.Reauthentication);
		}

		[Test]
		public async Task Protected_data_by_code_goes_through_the_one_grant_issuer()
		{
			_adp.Setup(a => a.IssueForTotpAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Grant);
			var result = await Api().Verify(Input("totp", area: "protected_data", code: "123456"), CancellationToken.None);
			Data(result).GrantToken.Should().Be("token-1");
			_recorded.Should().BeEmpty("the grant issuer records its own AdpStepUp evidence");
		}

		[Test]
		public async Task A_refused_protected_data_grant_keeps_the_issuers_code_and_status()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.ProtectedData, methods: StepUpMethodClasses.Password);
			_adp.Setup(a => a.IssueForReauthenticationAsync(It.IsAny<AdpStepUpCaller>(), MfaEvidenceMethod.Password, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.MethodNotAllowed));

			var result = await Api().Verify(Input("password", area: "protected_data", password: "correct horse"), CancellationToken.None);

			Type(result).Should().Be("mfa_method_not_allowed");
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.StepUpFailed);
		}

		// ── Options and the refusal ────────────────────────────────────────────────

		[Test]
		public async Task Options_describe_the_area_and_whether_this_session_already_satisfies_it()
		{
			_rule = StepUpTestPolicies.Rule(StepUpArea.Approvals, methods: StepUpMethodClasses.Password);
			var until = DateTime.UtcNow.AddMinutes(10);
			_policy.Setup(p => p.EvaluateAsync(It.Is<StepUpSubject>(s => s.UserId == UserId && s.DepartmentId == DepartmentId && s.SessionKey == "sid:" + SessionId),
				StepUpArea.Approvals, It.IsAny<CancellationToken>())).ReturnsAsync(new StepUpEvaluation { Satisfied = true, ExpiresOnUtc = until });

			var result = await Api(capabilities: StepUpTestPolicies.Capabilities(new StepUpCapabilities { TotpEnrolled = true, PasswordAvailable = true }, _policy.Object))
				.Options("approvals", CancellationToken.None);

			var data = result.Value.Data;
			data.Area.Should().Be("approvals");
			data.AllowedMethods.Should().Equal("mfa", "password");
			data.UsableMethods.Should().Equal("mfa", "password");
			data.MfaMethods.Should().Equal("totp");
			data.Satisfied.Should().BeTrue();
			data.CanComplete.Should().BeTrue();
		}

		[Test]
		public void The_refusal_is_403_step_up_required_with_the_area_and_methods_and_no_secrets()
		{
			var http = new DefaultHttpContext();
			var requirement = StepUpRequirement.For(StepUpTestPolicies.Rule(StepUpArea.Acknowledgments, methods: StepUpMethodClasses.Sso),
				new StepUpCapabilities { SsoAvailable = true });

			var refusal = ApiStepUp.Refusal(http, requirement);

			refusal.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			var problem = (ProblemDetails)refusal.Value;
			problem.Type.Should().Be("step_up_required");
			problem.Extensions["area"].Should().Be("acknowledgments");
			problem.Extensions["required"].Should().Be(true);
			((List<string>)problem.Extensions["allowedMethods"]).Should().Equal("mfa", "sso");
			((List<string>)problem.Extensions["usableMethods"]).Should().Equal("sso");
			((List<string>)problem.Extensions["mfaMethods"]).Should().BeEmpty();
			problem.Extensions["canComplete"].Should().Be(true);
			http.Response.Headers[ApiStepUp.AreaHeader].ToString().Should().Be("acknowledgments");
			http.Response.Headers["X-Resgrid-Reason"].ToString().Should().Be("step_up_required");
		}
	}
}
