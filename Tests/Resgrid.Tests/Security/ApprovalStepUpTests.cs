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
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Mfa;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 11: Responder approval as an API step-up (plan section 7.6 rows 8 and 10). The approval
	/// must be this session's, for this operation, and the operation must accept approval; the evidence names the approving
	/// passkey and Responder session and carries the approval time. Sign-in approval is proven over HTTP in
	/// <see cref="LoginMfaTransactionApiTests"/>.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class ApprovalStepUpTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "unit-session";

		private IHttpContextAccessor _previousAccessor;
		private Mock<IMfaApprovalService> _approvals;
		private Mock<IMfaEvidenceService> _evidence;
		private DepartmentSecurityPolicy _policy;
		private MfaApprovalRequest _request;
		private MfaController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ApiClaims._httpContextAccessor;
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
			{
				SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4
			};
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;

			var user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);

			_policy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId };
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(true);
			gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);

			_request = new MfaApprovalRequest
			{
				MfaApprovalRequestId = "approval-1", UserId = UserId, RequesterKind = (int)MfaApprovalRequesterKind.Session, RequesterId = SessionId,
				Purpose = (int)MfaApprovalPurpose.StepUp, Operation = MfaStepUpOperations.ChatExport, AuthenticationGeneration = 4,
				State = (int)MfaApprovalRequestState.Consumed, DecidedOnUtc = DateTime.UtcNow.AddSeconds(-20), ApproverPasskeyId = "pk-responder",
				ApproverSessionId = "responder-1"
			};
			_approvals = new Mock<IMfaApprovalService>();
			_approvals.Setup(a => a.IsAvailableAsync(UserId, UserSessionClientApplication.Unit, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_approvals.Setup(a => a.GetForRequesterAsync("approval-1", MfaApprovalRequesterKind.Session, SessionId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, _request));
			_approvals.Setup(a => a.ConsumeAsync("approval-1", MfaApprovalRequesterKind.Session, SessionId, UserId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, _request));
			_evidence = new Mock<IMfaEvidenceService>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);

			_controller = new MfaController(users.Object, new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), gates.Object), _evidence.Object,
				cache.Object, Mock.Of<ISystemAuditsService>(), Mock.Of<IPasskeyService>(), sso.Object, Mock.Of<ISsoBrokerService>(), _approvals.Object,
				Mock.Of<IMfaActivityService>())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[TearDown]
		public void TearDown() => ApiClaims._httpContextAccessor = _previousAccessor;

		private static VerifyStepUpInput Input(string operation = MfaStepUpOperations.ChatExport) =>
			new() { Operation = operation, Method = MfaMethodNames.PasskeyApproval, ApprovalRequestId = "approval-1" };

		private static string ProblemType(IConvertToActionResult result) =>
			result.Convert() is ObjectResult { Value: ProblemDetails problem } ? problem.Type : null;

		private void VerifyNothingRecorded() =>
			_evidence.Verify(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<MfaEvidenceKind>(),
				It.IsAny<MfaEvidenceMethod>(), It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int?>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

		[Test]
		public async Task An_approval_becomes_evidence_naming_its_approver_at_the_approval_time()
		{
			var result = await _controller.VerifyStepUp(Input(), CancellationToken.None);

			ProblemType(result).Should().BeNull();
			_evidence.Verify(e => e.RecordAsync(UserId, "sid:" + SessionId, UserSessionClientApplication.Unit, MfaEvidenceKind.SecondFactor,
				MfaEvidenceMethod.PasskeyApproval, MfaEvidencePurpose.StepUp, _request.DecidedOnUtc!.Value, 4, DepartmentId,
				"approval:pk-responder:responder-1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_approval_requested_before_a_shared_session_locked_is_refused_unspent()
		{
			// Plan section 7.9: on a shared session the request is bound to the lock version; a lock since then voids it.
			_controller.HttpContext.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
			{
				SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4, SharedMode = true,
				SessionLockVersion = 2
			};
			_request.LockVersion = 1;
			ProblemType(await _controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be("approval_expired");
			_approvals.Verify(a => a.ConsumeAsync(It.IsAny<string>(), It.IsAny<MfaApprovalRequesterKind>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyNothingRecorded();

			_request.LockVersion = 2;
			ProblemType(await _controller.VerifyStepUp(Input(), CancellationToken.None)).Should().BeNull("requested at the current lock version");
		}

		[Test]
		public async Task An_approval_for_another_operation_is_refused_unspent()
		{
			ProblemType(await _controller.VerifyStepUp(Input(MfaStepUpOperations.AdpManagement), CancellationToken.None)).Should().Be("approval_expired");
			_approvals.Verify(a => a.ConsumeAsync(It.IsAny<string>(), It.IsAny<MfaApprovalRequesterKind>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyNothingRecorded();
		}

		[TestCase(MfaStepUpOperations.SecurityChange)]
		[TestCase(MfaStepUpOperations.AccountSecurity)]
		public async Task Security_changes_and_account_factors_never_accept_approval(string operation)
		{
			_request.Operation = operation;
			ProblemType(await _controller.VerifyStepUp(Input(operation), CancellationToken.None)).Should().Be("mfa_method_not_allowed");
			VerifyNothingRecorded();
		}

		[Test]
		public async Task A_department_that_turned_approval_off_does_not_accept_it()
		{
			_policy.AllowResponderApproval = false;
			ProblemType(await _controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be("mfa_method_not_allowed");
			VerifyNothingRecorded();
		}

		[TestCase(MfaApprovalOutcome.Pending, "approval_pending")]
		[TestCase(MfaApprovalOutcome.Denied, "approval_denied")]
		[TestCase(MfaApprovalOutcome.Expired, "approval_expired")]
		public async Task An_approval_that_cannot_be_used_records_nothing(MfaApprovalOutcome outcome, string problem)
		{
			_approvals.Setup(a => a.ConsumeAsync("approval-1", MfaApprovalRequesterKind.Session, SessionId, UserId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(outcome));

			ProblemType(await _controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be(problem);
			VerifyNothingRecorded();
		}

		[Test]
		public async Task Step_up_options_offer_approval_only_where_it_counts()
		{
			(await _controller.StepUpOptions(MfaStepUpOperations.ChatExport, CancellationToken.None)).Value.Data.Methods
				.Should().Equal(MfaMethodNames.Totp, MfaMethodNames.PasskeyApproval);
			(await _controller.StepUpOptions(MfaStepUpOperations.AdpManagement, CancellationToken.None)).Value.Data.Methods
				.Should().Contain(MfaMethodNames.PasskeyApproval);
			(await _controller.StepUpOptions(MfaStepUpOperations.SecurityChange, CancellationToken.None)).Value.Data.Methods
				.Should().NotContain(MfaMethodNames.PasskeyApproval);
			(await _controller.StepUpOptions(MfaStepUpOperations.AccountSecurity, CancellationToken.None)).Value.Data.Methods
				.Should().NotContain(MfaMethodNames.PasskeyApproval);

			_approvals.Setup(a => a.IsAvailableAsync(UserId, UserSessionClientApplication.Unit, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			(await _controller.StepUpOptions(MfaStepUpOperations.ChatExport, CancellationToken.None)).Value.Data.Methods
				.Should().NotContain(MfaMethodNames.PasskeyApproval, "no eligible Responder");
		}
	}
}
