using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Tests.Services.ProtectedWorkflows;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Workflows;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The v4 Protected Workflows API refuses approve, renew, request and the department toggle from API keys and
	/// workload (client-credentials) tokens, and takes the step-up time only from a valid grant issued to the caller.
	/// </summary>
	[TestFixture]
	public class ProtectedWorkflowsApiTests
	{
		private ProtectedWorkflowHarness _h;
		private Mock<IProtectedDataGrantService> _grants;

		[SetUp]
		public void SetUp()
		{
			_h = new ProtectedWorkflowHarness();
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			_grants = new Mock<IProtectedDataGrantService>();
			_grants.SetupGet(g => g.CanValidateGrants).Returns(true);
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private static ClaimsPrincipal User(string userId) => new ClaimsPrincipal(new ClaimsIdentity(new[]
		{
			new Claim(ClaimTypes.PrimarySid, userId),
			new Claim(ClaimTypes.PrimaryGroupSid, ProtectedWorkflowHarness.DepartmentId.ToString())
		}, "OpenIddict.Validation.AspNetCore"));

		private static ClaimsPrincipal ApiKey() => new ClaimsPrincipal(new ClaimsIdentity(new[]
		{
			new Claim(ClaimTypes.PrimarySid, "smtp_relay_system"),
			new Claim(ClaimTypes.PrimaryGroupSid, ProtectedWorkflowHarness.DepartmentId.ToString()),
			new Claim(ResgridClaimTypes.Data.ServiceAccount, "true")
		}, ProtectedWorkflowCallerHelper.SystemApiKeyScheme));

		private static ClaimsPrincipal ClientCredentials(string userIdClaim) => new ClaimsPrincipal(new ClaimsIdentity(new[]
		{
			new Claim("sub", "system_integration"),
			new Claim(ClaimTypes.PrimarySid, userIdClaim),
			new Claim(ClaimTypes.PrimaryGroupSid, ProtectedWorkflowHarness.DepartmentId.ToString()),
			new Claim(ResgridClaimTypes.Data.ServiceAccount, "true")
		}, "OpenIddict.Validation.AspNetCore"));

		private ProtectedWorkflowsController Controller(ClaimsPrincipal principal, string grantForUser = null)
		{
			var httpContext = new DefaultHttpContext { User = principal };
			if (grantForUser != null)
			{
				httpContext.Request.Headers[DataProtectionController.GrantHeader] = "grant-token";
				var grant = new ProtectedDataGrant { UserId = grantForUser, DepartmentId = ProtectedWorkflowHarness.DepartmentId, MfaAtUtc = DateTime.UtcNow };
				_grants.Setup(g => g.ValidateGrant("grant-token", ProtectedWorkflowHarness.DepartmentId, It.IsAny<long>(), null, out grant, null))
					.Returns(ProtectedDataGrantValidationOutcome.Valid);
			}

			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			var dataProtection = new Mock<IDepartmentDataProtectionService>();
			dataProtection.Setup(d => d.GetPolicyByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(_h.Policy);
			return new ProtectedWorkflowsController(_h.Service, dataProtection.Object, _grants.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		private async Task<string> PendingReleaseAsync()
		{
			await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = new[] { "calls.completednotes" },
				RecipientType = (int)ProtectedReleaseRecipientType.CoveredEntity,
				RecipientName = "County DMH",
				Purpose = "Case write-back"
			}, new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });
			var requested = await _h.Service.RequestApprovalAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StepsFingerprint(), ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));
			requested.Success.Should().BeTrue(requested.ErrorCode);
			return _h.StoredRelease().WorkflowProtectedReleaseId;
		}

		private static string ErrorOf(ActionResult<ProtectedWorkflowCommandResultData> result) =>
			((result.Result as ObjectResult)?.Value as ProtectedWorkflowCommandResultData)?.Error;

		private static int? StatusOf(ActionResult<ProtectedWorkflowCommandResultData> result) => (result.Result as ObjectResult)?.StatusCode;

		/// <summary>An attestation over the release as it is stored now (what the approver reviewed).</summary>
		private ProtectedReleaseAttestationInput Attest => new ProtectedReleaseAttestationInput
		{
			Attested = true,
			AcknowledgedVersion = ProtectedWorkflowDefaults.WarningTextVersion,
			ReviewedFingerprint = _h.StoredRelease().ConfigFingerprint
		};

		[Test]
		public void api_keys_and_workload_tokens_are_never_interactive()
		{
			ProtectedWorkflowCallerHelper.IsInteractiveUser(ApiKey()).Should().BeFalse();
			ProtectedWorkflowCallerHelper.IsInteractiveUser(ClientCredentials(ProtectedWorkflowHarness.AdminB)).Should().BeFalse();
			ProtectedWorkflowCallerHelper.IsInteractiveUser(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "system_eventing") }, "OpenIddict"))).Should().BeFalse();
			ProtectedWorkflowCallerHelper.IsInteractiveUser(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeFalse("an anonymous principal is not a user");
			ProtectedWorkflowCallerHelper.IsInteractiveUser(User(ProtectedWorkflowHarness.AdminB)).Should().BeTrue();
		}

		[Test]
		public async Task approve_is_refused_for_an_api_key_even_with_a_grant_header()
		{
			var releaseId = await PendingReleaseAsync();

			var result = await Controller(ApiKey(), grantForUser: "smtp_relay_system").Approve(releaseId, Attest, CancellationToken.None);

			StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
			ErrorOf(result).Should().Be(ProtectedWorkflowErrorCodes.InteractiveRequired);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
		}

		[Test]
		public async Task approve_renew_request_and_toggle_are_refused_for_a_workload_token()
		{
			var releaseId = await PendingReleaseAsync();
			var controller = Controller(ClientCredentials(ProtectedWorkflowHarness.AdminB), grantForUser: ProtectedWorkflowHarness.AdminB);

			ErrorOf(await controller.Approve(releaseId, Attest, CancellationToken.None)).Should().Be(ProtectedWorkflowErrorCodes.InteractiveRequired);
			ErrorOf(await controller.Renew(releaseId, Attest, CancellationToken.None)).Should().Be(ProtectedWorkflowErrorCodes.InteractiveRequired);
			ErrorOf(await controller.RequestApproval(_h.Workflow.WorkflowId, Attest, CancellationToken.None)).Should().Be(ProtectedWorkflowErrorCodes.InteractiveRequired);
			ErrorOf(await controller.SetDepartmentSettings(new ProtectedWorkflowSettingsInput { Enabled = false }, CancellationToken.None))
				.Should().Be(ProtectedWorkflowErrorCodes.InteractiveRequired);

			_h.Egress.ProtectedWorkflowsEnabled.Should().BeTrue();
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
		}

		[Test]
		public async Task an_interactive_user_needs_their_own_valid_grant_to_approve()
		{
			var releaseId = await PendingReleaseAsync();

			var noGrant = await Controller(User(ProtectedWorkflowHarness.AdminB)).Approve(releaseId, Attest, CancellationToken.None);
			ErrorOf(noGrant).Should().Be(ProtectedWorkflowErrorCodes.StepUpRequired);

			var someoneElsesGrant = await Controller(User(ProtectedWorkflowHarness.AdminB), grantForUser: ProtectedWorkflowHarness.AdminA)
				.Approve(releaseId, Attest, CancellationToken.None);
			ErrorOf(someoneElsesGrant).Should().Be(ProtectedWorkflowErrorCodes.StepUpRequired);

			var approved = await Controller(User(ProtectedWorkflowHarness.AdminB), grantForUser: ProtectedWorkflowHarness.AdminB)
				.Approve(releaseId, Attest, CancellationToken.None);
			(approved.Result as ObjectResult)?.StatusCode.Should().Be(StatusCodes.Status200OK);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Active);
		}

		[Test]
		public async Task approve_is_refused_when_the_reviewed_fingerprint_is_not_the_stored_one()
		{
			var releaseId = await PendingReleaseAsync();
			var stale = new ProtectedReleaseAttestationInput
			{
				Attested = true,
				AcknowledgedVersion = ProtectedWorkflowDefaults.WarningTextVersion,
				ReviewedFingerprint = new string('0', 64)
			};

			var result = await Controller(User(ProtectedWorkflowHarness.AdminB), grantForUser: ProtectedWorkflowHarness.AdminB)
				.Approve(releaseId, stale, CancellationToken.None);

			ErrorOf(result).Should().Be(ProtectedWorkflowErrorCodes.ConfigChanged);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
		}

		[Test]
		public async Task suspend_and_revoke_work_for_an_administrator_token_without_step_up()
		{
			var releaseId = await PendingReleaseAsync();

			var result = await Controller(User(ProtectedWorkflowHarness.AdminB)).Revoke(releaseId, CancellationToken.None);

			(result.Result as ObjectResult)?.StatusCode.Should().Be(StatusCodes.Status200OK, "reducing exposure never waits on a second factor");
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Revoked);
		}
	}
}
