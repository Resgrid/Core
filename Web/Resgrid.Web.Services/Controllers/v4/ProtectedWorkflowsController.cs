using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Workflows;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// ADP Protected Workflows over the v4 API. Reads, drafts, suspension and revocation work for any caller holding
	/// the ADP egress permission; approve, renew, request and the department toggle additionally require an INTERACTIVE
	/// user (never an API key, client-credentials token or other service account) presenting a currently-valid,
	/// non-exempt Protected Data Grant for themselves in X-Resgrid-Protected-Grant, whose step-up time is checked for
	/// freshness by the service. Every rule lives in <see cref="IProtectedWorkflowService"/>.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ProtectedWorkflowsController : V4AuthenticatedApiControllerbase
	{
		private readonly IProtectedWorkflowService _protectedWorkflows;
		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IProtectedDataGrantService _grantService;

		public ProtectedWorkflowsController(IProtectedWorkflowService protectedWorkflows, IDepartmentDataProtectionService dataProtectionService,
			IProtectedDataGrantService grantService)
		{
			_protectedWorkflows = protectedWorkflows;
			_dataProtectionService = dataProtectionService;
			_grantService = grantService;
		}

		/// <summary>The department's Protected Workflows settings.</summary>
		[HttpGet("Settings")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedWorkflowSettingsResult>> Settings()
		{
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Forbid();

			var settings = await _protectedWorkflows.GetDepartmentSettingsAsync(DepartmentId);
			return Ok(new ProtectedWorkflowSettingsResult
			{
				Data = new ProtectedWorkflowSettingsData
				{
					Enabled = settings.Enabled,
					RequireSecondApprover = settings.RequireSecondApprover,
					AckVersion = settings.AckVersion,
					AckOn = settings.AckOn,
					AdpState = (int)settings.AdpState,
					CurrentWarningVersion = ProtectedWorkflowDefaults.WarningTextVersion,
					SecondApproverRelaxRequestedOn = settings.RelaxRequestedOn
				}
			});
		}

		/// <summary>Turns Protected Workflows on or off. Interactive users with a fresh step-up grant only.</summary>
		[HttpPost("SetDepartmentSettings")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedWorkflowCommandResultData>> SetDepartmentSettings([FromBody] ProtectedWorkflowSettingsInput input, CancellationToken ct)
		{
			if (input == null)
				return BadRequest();
			return Command(await _protectedWorkflows.SetDepartmentSettingsAsync(DepartmentId, input.Enabled, input.RequireSecondApprover,
				input.AcknowledgedVersion, await ActorAsync(), ct));
		}

		/// <summary>Every release in the department (current and historical).</summary>
		[HttpGet("GetReleases")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedReleasesResult>> GetReleases()
		{
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Forbid();

			var releases = await _protectedWorkflows.GetReleasesForDepartmentAsync(DepartmentId);
			return Ok(new ProtectedReleasesResult { Data = releases.Select(Map).ToList() });
		}

		/// <summary>A workflow's current release with its validation and fingerprint status.</summary>
		[HttpGet("GetRelease/{workflowId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedReleaseViewResult>> GetRelease(string workflowId, CancellationToken ct)
		{
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Forbid();

			var view = await _protectedWorkflows.GetReleaseViewAsync(DepartmentId, workflowId, UserId, ct);
			if (view == null)
				return NotFound();

			return Ok(new ProtectedReleaseViewResult
			{
				Data = new ProtectedReleaseViewData
				{
					Release = view.Release == null ? null : Map(view.Release),
					TriggerSupported = view.TriggerSupported,
					FingerprintMatches = view.FingerprintMatches,
					StepsFingerprint = view.StepsFingerprint,
					DestinationHost = view.Validation?.DestinationHost,
					TokenHost = view.TokenHost,
					ValidationErrors = view.Validation?.Errors.Select(e => e.Code).Distinct().ToList() ?? new List<string>(),
					AvailableFieldIds = view.AvailableFields.Select(f => f.FieldId).ToList()
				}
			});
		}

		[HttpPost("SaveDraft")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedWorkflowCommandResultData>> SaveDraft([FromBody] ProtectedReleaseDraftInput input, CancellationToken ct)
		{
			if (input == null)
				return BadRequest();
			return Command(await _protectedWorkflows.SaveDraftAsync(DepartmentId, input.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = input.FieldIds ?? new List<string>(),
				RecipientType = input.RecipientType,
				RecipientName = input.RecipientName,
				Purpose = input.Purpose
			}, await ActorAsync(), ct));
		}

		/// <summary>Requests (single approver: approves) the workflow's current configuration. Interactive step-up only.</summary>
		[HttpPost("RequestApproval/{workflowId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedWorkflowCommandResultData>> RequestApproval(string workflowId, [FromBody] ProtectedReleaseAttestationInput input, CancellationToken ct) =>
			Command(await _protectedWorkflows.RequestApprovalAsync(DepartmentId, workflowId, input?.Attested ?? false, input?.AcknowledgedVersion, input?.ReviewedFingerprint, await ActorAsync(), Sensitive(input), ct));

		/// <summary>Second-administrator approval. Interactive step-up only; the requester cannot approve.</summary>
		[HttpPost("Approve/{releaseId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedWorkflowCommandResultData>> Approve(string releaseId, [FromBody] ProtectedReleaseAttestationInput input, CancellationToken ct) =>
			Command(await _protectedWorkflows.ApproveAsync(DepartmentId, releaseId, input?.Attested ?? false, input?.AcknowledgedVersion, input?.ReviewedFingerprint, await ActorAsync(), Sensitive(input), ct));

		/// <summary>Renews an Active or Expired release. Interactive step-up and re-attestation only.</summary>
		[HttpPost("Renew/{releaseId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedWorkflowCommandResultData>> Renew(string releaseId, [FromBody] ProtectedReleaseAttestationInput input, CancellationToken ct) =>
			Command(await _protectedWorkflows.RenewAsync(DepartmentId, releaseId, input?.Attested ?? false, input?.AcknowledgedVersion, input?.ReviewedFingerprint, await ActorAsync(), Sensitive(input), ct));

		private static ProtectedSensitiveAttestation Sensitive(ProtectedReleaseAttestationInput input) => input == null ? null : new ProtectedSensitiveAttestation
		{
			Restricted = input.RestrictedAttested,
			RestrictedVersion = input.RestrictedAcknowledgedVersion,
			Part2 = input.Part2Attested,
			Part2Version = input.Part2AcknowledgedVersion
		};

		[HttpPost("Suspend/{releaseId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedWorkflowCommandResultData>> Suspend(string releaseId, CancellationToken ct) =>
			Command(await _protectedWorkflows.SuspendAsync(DepartmentId, releaseId, await ActorAsync(), ct));

		[HttpPost("Revoke/{releaseId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedWorkflowCommandResultData>> Revoke(string releaseId, CancellationToken ct) =>
			Command(await _protectedWorkflows.RevokeAsync(DepartmentId, releaseId, await ActorAsync(), ct));

		/// <summary>The value-free disclosure log (metadata only), newest first.</summary>
		[HttpGet("GetDisclosures")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedDisclosuresResult>> GetDisclosures(string workflowId = null, string callId = null, DateTime? fromUtc = null, DateTime? toUtc = null)
		{
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Forbid();

			var rows = await _protectedWorkflows.GetDisclosuresAsync(DepartmentId, new ProtectedWorkflowDisclosureFilter
			{
				WorkflowId = workflowId,
				EntityId = callId,
				FromUtc = fromUtc,
				ToUtc = toUtc,
				MaxRows = 5000
			});

			return Ok(new ProtectedDisclosuresResult
			{
				Data = rows.Select(r => new ProtectedDisclosureData
				{
					ChainSequence = r.ChainSequence,
					RecordType = r.RecordType,
					EventType = r.EventType,
					Outcome = r.Outcome,
					IsTest = r.IsTest,
					ActorUserId = r.ActorUserId,
					WorkflowId = r.WorkflowId,
					WorkflowRunId = r.WorkflowRunId,
					WorkflowStepId = r.WorkflowStepId,
					WorkflowProtectedReleaseId = r.WorkflowProtectedReleaseId,
					EntityType = r.EntityType,
					EntityId = r.EntityId,
					FieldIds = WorkflowProtectedRelease.ParseFieldIds(r.FieldIds).ToList(),
					DestinationHost = r.DestinationHost,
					PayloadSha256 = r.PayloadSha256,
					PayloadBytes = r.PayloadBytes,
					HttpStatus = r.HttpStatus,
					BrokerRequestId = r.BrokerRequestId,
					Detail = r.Detail,
					ContentType = r.ContentType,
					CapturedKeys = WorkflowProtectedRelease.ParseFieldIds(r.CapturedKeys).ToList(),
					OccurredOn = r.OccurredOn,
					PrevHash = r.PrevHash,
					Hash = r.Hash
				}).ToList()
			});
		}

		/// <summary>Re-verifies the department's disclosure hash chain.</summary>
		[HttpGet("VerifyChain")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtectedChainVerificationResult>> VerifyChain()
		{
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Forbid();

			var verification = await _protectedWorkflows.VerifyChainAsync(DepartmentId);
			return Ok(new ProtectedChainVerificationResult
			{
				IsValid = verification.IsValid,
				RecordCount = verification.RecordCount,
				FirstInvalidSequence = verification.FirstInvalidSequence
			});
		}

		// ── Helpers ───────────────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// The caller as the service sees it. A service account or API key is never interactive. The step-up time is
		/// taken ONLY from a valid, non-exempt grant issued to this same user (fail closed when grants are unavailable).
		/// </summary>
		private async Task<ProtectedWorkflowActor> ActorAsync()
		{
			var interactive = ProtectedWorkflowCallerHelper.IsInteractiveUser(User);
			DateTime? stepUpAt = null;

			if (interactive && _grantService.CanValidateGrants)
			{
				var token = Request.Headers[DataProtectionController.GrantHeader].ToString();
				if (!string.IsNullOrWhiteSpace(token))
				{
					var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId, bypassCache: true);
					var outcome = _grantService.ValidateGrant(token, DepartmentId, policy?.PolicyEpoch ?? 0, requiredScope: null, out var grant);
					if (outcome == ProtectedDataGrantValidationOutcome.Valid && grant != null && !grant.StepUpExempt &&
						string.Equals(grant.UserId, UserId, StringComparison.OrdinalIgnoreCase))
						stepUpAt = grant.MfaAtUtc;
				}
			}

			return new ProtectedWorkflowActor { UserId = UserId, IsInteractive = interactive, StepUpVerifiedAtUtc = stepUpAt };
		}

		private ActionResult<ProtectedWorkflowCommandResultData> Command(ProtectedWorkflowCommandResult result)
		{
			var data = new ProtectedWorkflowCommandResultData
			{
				Success = result.Success,
				Error = result.ErrorCode,
				PendingConfirmation = result.PendingConfirmation,
				ValidationErrors = result.ValidationErrors.Select(e => e.Code).Distinct().ToList(),
				Release = result.Release == null ? null : Map(result.Release)
			};

			if (result.Success)
				return Ok(data);

			return result.ErrorCode switch
			{
				ProtectedWorkflowErrorCodes.PermissionDenied or ProtectedWorkflowErrorCodes.InteractiveRequired or
					ProtectedWorkflowErrorCodes.StepUpRequired or ProtectedWorkflowErrorCodes.SelfApproval => StatusCode(StatusCodes.Status403Forbidden, data),
				ProtectedWorkflowErrorCodes.NotFound => NotFound(data),
				_ => UnprocessableEntity(data)
			};
		}

		private static ProtectedReleaseData Map(WorkflowProtectedRelease release) => new ProtectedReleaseData
		{
			WorkflowProtectedReleaseId = release.WorkflowProtectedReleaseId,
			WorkflowId = release.WorkflowId,
			State = release.State,
			StateName = release.ReleaseState.ToString(),
			SuspendedReason = release.SuspendedReason,
			AllowedFieldIds = release.GetAllowedFieldIds().ToList(),
			DestinationHost = release.DestinationHost,
			TokenHost = release.TokenHost,
			WorkflowCredentialId = release.WorkflowCredentialId,
			RecipientType = release.RecipientType,
			RecipientName = release.RecipientName,
			Purpose = release.Purpose,
			AckVersion = release.AckVersion,
			AuthMethod = release.AuthMethod,
			AllowsRestricted = release.AllowsRestricted,
			AllowsPart2 = release.AllowsPart2,
			RequestedByUserId = release.RequestedByUserId,
			RequestedOn = release.RequestedOn,
			ApprovedByUserId = release.ApprovedByUserId,
			ApprovedOn = release.ApprovedOn,
			ExpiresOn = release.ExpiresOn,
			RevokedOn = release.RevokedOn,
			ConfigFingerprint = release.ConfigFingerprint
		};
	}
}
