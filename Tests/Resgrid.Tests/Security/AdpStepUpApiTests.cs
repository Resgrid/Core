using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.DataProtection;
using Resgrid.Web.Services.Models.v4.MfaApproval;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using WebClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using ApiDataProtectionController = Resgrid.Web.Services.Controllers.v4.DataProtectionController;
using WebDataProtectionController = Resgrid.Web.Areas.User.Controllers.DataProtectionController;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 18: the API and Web ADP endpoints go through the one grant issuer, pass it the
	/// validated session (never client values), and map its outcomes. A verification without signing material is a 503,
	/// never a token-less success.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AdpStepUpApiTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "unit-session";

		private IHttpContextAccessor _previousApi;
		private IHttpContextAccessor _previousWeb;
		private Mock<IAdpStepUpService> _adp;
		private AdpStepUpCaller _caller;
		private IdentityUser _user;

		[SetUp]
		public void SetUp()
		{
			_previousApi = ApiClaims._httpContextAccessor;
			_previousWeb = WebClaims._httpContextAccessor;
			_adp = new Mock<IAdpStepUpService>();
			_caller = null;
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
		}

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = _previousApi;
			WebClaims._httpContextAccessor = _previousWeb;
		}

		private static AdpGrantIssue Issued => new()
		{
			Outcome = AdpGrantOutcome.Issued, GrantId = "grant-1", Token = "token-1", ExpiresOnUtc = new DateTime(2026, 9, 29, 12, 15, 0, DateTimeKind.Utc),
			WindowMinutes = 15
		};

		private DefaultHttpContext Http(bool withSession = true)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ClaimTypes.Name, "user1"),
					new Claim(SessionClaimTypes.SessionId, "claimed-by-token")
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
			WebClaims._httpContextAccessor = accessor.Object;
			return http;
		}

		private Mock<UserManager<IdentityUser>> Users(bool codeValid = true)
		{
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(_user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(true);
			users.Setup(m => m.IsLockedOutAsync(_user)).ReturnsAsync(false);
			users.Setup(m => m.VerifyTwoFactorTokenAsync(_user, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(codeValid);
			users.Setup(m => m.ResetAccessFailedCountAsync(_user)).ReturnsAsync(IdentityResult.Success);
			users.Setup(m => m.AccessFailedAsync(_user)).ReturnsAsync(IdentityResult.Success);
			return users;
		}

		private ApiDataProtectionController Api(bool withSession = true, bool codeValid = true)
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			return new ApiDataProtectionController(Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IDepartmentLockService>(),
				Mock.Of<IProtectedFieldCatalog>(), Mock.Of<IDepartmentsService>(), Mock.Of<IFeatureToggleService>(), Users(codeValid).Object, cache.Object,
				Mock.Of<IProtectedDataGrantService>(), Mock.Of<IAdpReleaseService>(), Mock.Of<IAdpAuditRepository>(), Mock.Of<IMfaEvidenceService>(),
				Mock.Of<IMfaPolicyService>(), _adp.Object, Mock.Of<IMfaCredentialStateService>())
			{
				ControllerContext = new ControllerContext { HttpContext = Http(withSession) }
			};
		}

		private static ProblemDetails Problem(IConvertToActionResult result) => (ProblemDetails)((ObjectResult)result.Convert()).Value;
		private static int? Status(IConvertToActionResult result) => ((ObjectResult)result.Convert()).StatusCode;

		private void Capture(AdpGrantIssue answer)
		{
			_adp.Setup(a => a.IssueForTotpAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.Callback((AdpStepUpCaller c, DateTime _, CancellationToken _) => _caller = c).ReturnsAsync(answer);
		}

		[Test]
		public async Task A_verified_code_is_issued_through_the_one_issuer_bound_to_the_validated_session()
		{
			Capture(Issued);

			var result = (await Api().VerifyStepUp(new VerifyStepUpInput { Code = "123456" })).Value;

			result.GrantId.Should().Be("grant-1");
			result.GrantToken.Should().Be("token-1");
			result.StepUpExpiresOnUtc.Should().Be("2026-09-29T12:15:00.0000000Z");
			result.StepUpWindowMinutes.Should().Be(15);
			_caller.UserId.Should().Be(UserId);
			_caller.DepartmentId.Should().Be(DepartmentId);
			_caller.Session.SessionId.Should().Be(SessionId, "the validated session, never the token's claim");
			_caller.Client.Should().Be(UserSessionClientApplication.Unit);
			_caller.EvidenceKey.Should().Be(MfaEvidence.TrackedSessionKey(SessionId));
			_caller.AccountAuthenticationGeneration.Should().Be(4);
		}

		[Test]
		public async Task A_verified_code_without_signing_material_is_unavailable_not_a_token_less_success()
		{
			Capture(AdpGrantIssue.Of(AdpGrantOutcome.NotConfigured));

			var result = await Api().VerifyStepUp(new VerifyStepUpInput { Code = "123456" });

			Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable);
			Problem(result).Type.Should().Be("grants_not_configured");
		}

		[Test]
		public async Task A_wrong_code_never_reaches_the_issuer()
		{
			var result = await Api(codeValid: false).VerifyStepUp(new VerifyStepUpInput { Code = "000000" });

			Problem(result).Type.Should().Be("invalid_totp");
			_adp.Verify(a => a.IssueForTotpAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_passkey_approval_and_provider_step_up_endpoints_map_the_issuers_outcomes()
		{
			_adp.Setup(a => a.CompletePasskeyAsync(It.Is<AdpStepUpCaller>(c => c.Session.SessionId == SessionId), "req-1", It.Is<string>(s => s.Contains("\"id\"")),
				It.IsAny<CancellationToken>())).ReturnsAsync(Issued);
			(await Api().VerifyPasskey(new AdpPasskeyStepUpInput { RequestId = "req-1", Credential = JObject.Parse("{\"id\":\"abc\"}") }, CancellationToken.None))
				.Value.GrantToken.Should().Be("token-1");

			_adp.Setup(a => a.CompleteApprovalAsync(It.IsAny<AdpStepUpCaller>(), "approval-1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.ApprovalPending));
			var pending = await Api().CompleteApproval(new AdpApprovalStepUpInput { ApprovalRequestId = "approval-1" }, CancellationToken.None);
			Status(pending).Should().Be(StatusCodes.Status409Conflict);
			Problem(pending).Type.Should().Be("approval_pending");

			_adp.Setup(a => a.CompleteFederatedAsync(It.IsAny<AdpStepUpCaller>(), "sso-1", "code", "verifier", It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.VerificationFailed, "federated_mfa_not_satisfied"));
			var federated = await Api().CompleteFederated(new AdpFederatedStepUpInput { SsoTransactionId = "sso-1", SsoCode = "code", CodeVerifier = "verifier" },
				CancellationToken.None);
			Status(federated).Should().Be(StatusCodes.Status401Unauthorized);
			Problem(federated).Type.Should().Be("federated_mfa_not_satisfied");

			_adp.Setup(a => a.CompletePasskeyAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.SessionRequired));
			var untracked = await Api(withSession: false).VerifyPasskey(new AdpPasskeyStepUpInput { RequestId = "req-1", Credential = JObject.Parse("{}") },
				CancellationToken.None);
			Status(untracked).Should().Be(StatusCodes.Status409Conflict);
			Problem(untracked).Type.Should().Be("session_required");

			Problem(await Api().VerifyPasskey(new AdpPasskeyStepUpInput { RequestId = "req-1" }, CancellationToken.None)).Type.Should().Be("invalid_request");
			Problem(await Api().CompleteApproval(new AdpApprovalStepUpInput(), CancellationToken.None)).Type.Should().Be("invalid_request");
			Problem(await Api().CompleteFederated(new AdpFederatedStepUpInput { SsoTransactionId = "sso-1" }, CancellationToken.None)).Type.Should().Be("invalid_request");
		}

		[Test]
		public async Task Step_up_methods_are_those_the_caller_has_and_the_department_accepts()
		{
			_adp.Setup(a => a.GetMethodChoiceAsync(It.IsAny<AdpStepUpCaller>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(new MfaMethodChoice
			{
				EnrolledMethods = new[] { MfaMethodNames.Totp, MfaMethodNames.Passkey },
				AllowedMethods = new[] { MfaMethodNames.Totp, MfaMethodNames.PasskeyApproval },
				Preferred = MfaMethodNames.Totp
			});

			var data = (await Api().StepUpMethods(CancellationToken.None)).Value.Data;

			data.Methods.Should().Equal(MfaMethodNames.Totp);
			data.Preferred.Should().Be(MfaMethodNames.Totp);
		}

		[Test]
		public async Task Passkey_options_come_from_the_issuer_for_this_session()
		{
			_adp.Setup(a => a.BeginPasskeyAsync(It.Is<AdpStepUpCaller>(c => c.Session.SessionId == SessionId && c.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = "req-1", OptionsJson = "{\"challenge\":\"abc\"}" });
			var options = (await Api().PasskeyOptions(CancellationToken.None)).Value.Data;
			options.RequestId.Should().Be("req-1");
			options.Options.ToString().Should().Contain("challenge");

			_adp.Setup(a => a.BeginPasskeyAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>())).ReturnsAsync(PasskeyCeremonyStart.Of(PasskeyOutcome.Unavailable));
			Problem(await Api().PasskeyOptions(CancellationToken.None)).Type.Should().Be(PasskeyOutcomes.ErrorCode(PasskeyOutcome.Unavailable));
		}

		[Test]
		public async Task An_exempt_grant_request_is_refused_as_step_up_required_for_any_other_client()
		{
			_adp.Setup(a => a.IssueExemptAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>())).ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired));
			_adp.Setup(a => a.IssueFromRecentEvidenceAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired));
			var refused = await Api().RequestGrant();
			Status(refused).Should().Be(StatusCodes.Status401Unauthorized);
			Problem(refused).Type.Should().Be("step_up_required");

			_adp.Setup(a => a.IssueExemptAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>())).ReturnsAsync(Issued);
			(await Api().RequestGrant()).Value.GrantId.Should().Be("grant-1");
		}

		[Test]
		public async Task Asking_for_protected_data_reuses_this_sessions_recent_mfa_before_prompting()
		{
			// Slice 23 (plan section 9.1): not exempt, but the session's recent sign-in MFA qualifies.
			_adp.Setup(a => a.IssueExemptAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>())).ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired));
			AdpStepUpCaller reusedFor = null;
			_adp.Setup(a => a.IssueFromRecentEvidenceAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>()))
				.Callback((AdpStepUpCaller c, CancellationToken _) => reusedFor = c).ReturnsAsync(Issued);

			(await Api().RequestGrant()).Value.GrantId.Should().Be("grant-1");
			reusedFor.Session.SessionId.Should().Be("unit-session", "only this session's own evidence");
			(await Api().RequestGrantFromRecentMfa()).Value.GrantId.Should().Be("grant-1");

			// Evidence that no longer qualifies (a revoked credential, say) means verifying again, never an error.
			_adp.Setup(a => a.IssueFromRecentEvidenceAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.CredentialRevoked));
			Problem(await Api().RequestGrant()).Type.Should().Be("step_up_required");
			_adp.Setup(a => a.IssueFromRecentEvidenceAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired));
			Problem(await Api().RequestGrantFromRecentMfa()).Type.Should().Be("step_up_required");
		}

		[Test]
		public async Task An_approval_for_protected_data_is_asked_for_through_the_issuer_by_the_session_in_its_department()
		{
			AdpStepUpCaller asked = null;
			_adp.Setup(a => a.RequestApprovalAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>()))
				.Callback((AdpStepUpCaller c, CancellationToken _) => asked = c)
				.ReturnsAsync(new MfaApprovalStart { Outcome = MfaApprovalOutcome.Succeeded, ApprovalRequestId = "approval-1", MatchNumber = "42", ExpiresInSeconds = 120 });
			var approvals = new Mock<IMfaApprovalService>();
			approvals.SetupGet(a => a.IsEnabled).Returns(true);
			MfaApprovalController Controller(bool withSession) => new(approvals.Object, Mock.Of<IMfaLoginTransactionService>(), Mock.Of<IDepartmentsService>(),
				Users().Object, Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>(), _adp.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = Http(withSession) }
			};

			var started = (await Controller(true).RequestApproval(new MfaApprovalRequestInput { Purpose = "adp" }, CancellationToken.None)).Value.Data;

			started.ApprovalRequestId.Should().Be("approval-1");
			started.MatchNumber.Should().Be("42");
			asked.DepartmentId.Should().Be(DepartmentId);
			asked.Session.SessionId.Should().Be(SessionId);
			approvals.Verify(a => a.RequestAsync(It.IsAny<MfaApprovalRequester>(), It.IsAny<CancellationToken>()), Times.Never,
				"the issuer checks the ADP switches before asking");

			Problem(await Controller(false).RequestApproval(new MfaApprovalRequestInput { Purpose = "adp" }, CancellationToken.None)).Type
				.Should().Be(MfaApprovalOutcomes.ErrorCode(MfaApprovalOutcome.SessionRequired));
		}

		[Test]
		public async Task The_web_reveal_dialog_gets_the_issuers_grant_or_its_value_free_code()
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			var controller = new WebDataProtectionController(Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IDepartmentLockService>(),
				Mock.Of<IAdpSizingService>(), Mock.Of<IProtectedDataBrokerClient>(), Mock.Of<IDepartmentsService>(), Users().Object,
				Mock.Of<IProtectedDataGrantService>(), Mock.Of<IAdpReleaseService>(), Mock.Of<IAdpAccessStore>(), Mock.Of<IAdpAuditRepository>(), cache.Object,
				Mock.Of<IEventAggregator>(), Mock.Of<IProtectedWorkflowService>(), Mock.Of<Resgrid.Chatbot.Interfaces.IChatbotDepartmentConfigService>(), Mock.Of<IMfaEvidenceService>(),
				Mock.Of<IMfaActivityService>(), _adp.Object, Mock.Of<IMfaCredentialStateService>(), Mock.Of<IMfaApprovalService>())
			{
				ControllerContext = new ControllerContext { HttpContext = Http() }
			};

			Capture(Issued);
			var granted = JObject.FromObject(((JsonResult)await controller.VerifyStepUp("123456")).Value);
			granted.Value<bool>("success").Should().BeTrue();
			granted.Value<string>("grantToken").Should().Be("token-1");
			granted.Value<string>("expiresOnUtc").Should().Be("2026-09-29T12:15:00.0000000Z");
			_caller.Client.Should().Be(UserSessionClientApplication.Unit, "the validated session decides the client");
			_caller.AuditSystem.Should().Be(SystemAuditSystems.Website);

			Capture(AdpGrantIssue.Of(AdpGrantOutcome.NotConfigured));
			var refused = JObject.FromObject(((JsonResult)await controller.VerifyStepUp("123456")).Value);
			refused.Value<bool>("success").Should().BeFalse();
			refused.Value<string>("error").Should().Be("grants_not_configured");

			// Revealing asks the issuer for an exemption, then for this session's recent sign-in MFA (slice 23), before any prompt.
			_adp.Setup(a => a.IssueExemptAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>())).ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired));
			_adp.Setup(a => a.IssueFromRecentEvidenceAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>())).ReturnsAsync(Issued);
			JObject.FromObject(((JsonResult)await controller.RequestGrant()).Value).Value<string>("grantToken").Should().Be("token-1");
			_adp.Setup(a => a.IssueFromRecentEvidenceAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.CredentialRevoked));
			JObject.FromObject(((JsonResult)await controller.RequestGrant()).Value).Value<string>("error").Should().Be("step_up_required",
				"the dialog then prompts as before");
		}
	}
}
