using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Protocols;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Round 10 A5: every v4 action that can need a step-up answers the one refusal — 403, problem type <c>step_up_required</c>, its area —
	/// instead of its old per-feature shape, and the retried request (after <c>StepUp/Verify</c>) reads the session's evidence again.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class StepUpRefusalWiringTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private IHttpContextAccessor _previous;

		[SetUp]
		public void SetUp() => _previous = ApiClaims._httpContextAccessor;

		[TearDown]
		public void TearDown() => ApiClaims._httpContextAccessor = _previous;

		private static DefaultHttpContext Http()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
			{
				SessionId = "s-1", ClientApplication = (int)UserSessionClientApplication.Responder, AuthenticationGeneration = 1
			};
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;
			return http;
		}

		private static void AssertRefusal(IConvertToActionResult result, DefaultHttpContext http, string area)
		{
			var objectResult = (ObjectResult)result.Convert();
			objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			var problem = objectResult.Value.Should().BeOfType<ProblemDetails>().Subject;
			problem.Type.Should().Be("step_up_required");
			problem.Extensions["area"].Should().Be(area);
			http.Response.Headers["X-Resgrid-Step-Up"].ToString().Should().Be(area);
		}

		[Test]
		public async Task Approvals_approve_refuses_with_the_approvals_area()
		{
			var approvals = new Mock<IApprovalChainService>();
			approvals.Setup(a => a.ApproveAsync(DepartmentId, 9, It.IsAny<SignatureProof>(), false, It.IsAny<CancellationToken>()))
				.ReturnsAsync(Resgrid.Model.Approvals.ApprovalResult.Refused(Resgrid.Model.Approvals.ApprovalRefusals.StepUpRequired));
			var http = Http();
			var controller = new ApprovalsController(approvals.Object, Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>(), StepUpTestPolicies.Capabilities())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};

			AssertRefusal(await controller.Approve(new ApprovalActionInput { ApprovalRequestId = 9 }, CancellationToken.None), http, "approvals");
		}

		[Test]
		public async Task Read_and_sign_refuses_with_the_acknowledgments_area()
		{
			var acknowledgments = new Mock<IProtocolAcknowledgmentService>();
			acknowledgments.Setup(a => a.AcknowledgeAsync(It.IsAny<ProtocolReader>(), 5, "hash", It.IsAny<SignatureProof>(), false, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.StepUpRequired));
			var http = Http();
			var controller = new ProtocolAcknowledgmentsController(Mock.Of<IProtocolAccessService>(), acknowledgments.Object, Mock.Of<IProtocolDocumentService>(),
				Mock.Of<IProtocolLibraryService>(), Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>(), Mock.Of<IProtocolLearningExtensions>(),
				StepUpTestPolicies.Capabilities())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};

			AssertRefusal(await controller.Acknowledge(new AcknowledgeProtocolInput { ProtocolVersionId = 5, ContentHash = "hash" }, CancellationToken.None), http,
				"acknowledgments");
		}

		[Test]
		public async Task A_non_step_up_outcome_keeps_its_own_answer()
		{
			var acknowledgments = new Mock<IProtocolAcknowledgmentService>();
			acknowledgments.Setup(a => a.AcknowledgeAsync(It.IsAny<ProtocolReader>(), 5, "hash", It.IsAny<SignatureProof>(), false, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.ContentChanged));
			var controller = new ProtocolAcknowledgmentsController(Mock.Of<IProtocolAccessService>(), acknowledgments.Object, Mock.Of<IProtocolDocumentService>(),
				Mock.Of<IProtocolLibraryService>(), Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>(), Mock.Of<IProtocolLearningExtensions>(),
				StepUpTestPolicies.Capabilities())
			{
				ControllerContext = new ControllerContext { HttpContext = Http() }
			};

			var result = (ObjectResult)((IConvertToActionResult)await controller.Acknowledge(new AcknowledgeProtocolInput { ProtocolVersionId = 5, ContentHash = "hash" },
				CancellationToken.None)).Convert();
			result.StatusCode.Should().Be(StatusCodes.Status200OK);
		}
	}
}
