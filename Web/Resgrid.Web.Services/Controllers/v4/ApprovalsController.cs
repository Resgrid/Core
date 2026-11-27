using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// e-Signature S4 approval chains for Omni (the older apps get no client change; their members approve in the browser from the
	/// notification link): the member's pending approvals, one request (subject, amount, content code, steps), approving at L2 with the
	/// session's server-side step-up evidence (v4 has no passkey-bound signing), rejecting with a reason, withdrawing, and submitting a
	/// subject. Every call goes through the approval service's own access rules.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class ApprovalsController : V4AuthenticatedApiControllerbase
	{
		private readonly IApprovalChainService _approvals;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _mfaPolicy;

		private readonly IStepUpCapabilityService _stepUp;

		public ApprovalsController(IApprovalChainService approvals, IMfaEvidenceService evidence, IMfaPolicyService mfaPolicy, IStepUpCapabilityService stepUp)
		{
			_stepUp = stepUp;
			_approvals = approvals;
			_evidence = evidence;
			_mfaPolicy = mfaPolicy;
		}

		/// <summary>The approvals waiting for the member (escalated first).</summary>
		[HttpGet("GetMyApprovals")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MyApprovalsResult>> GetMyApprovals()
		{
			var items = await _approvals.GetInboxAsync(DepartmentId, UserId);
			var result = new MyApprovalsResult
			{
				Data = items.Select(i => new ApprovalInboxData
				{
					ApprovalRequestId = i.ApprovalRequestId, SubjectKind = i.SubjectKind, SubjectKindName = ((SignatureSubjectKinds)i.SubjectKind).ToString(), SubjectId = i.SubjectId,
					Title = i.Title, Amount = i.Amount, Currency = i.Currency, StepLabel = i.StepLabel, SubmittedOn = i.SubmittedOn, ExpiresOn = i.ExpiresOn, Escalated = i.Escalated,
					ContentCode = i.ContentCode
				}).ToList(),
				PageSize = items.Count,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>One request as the member may see it (submitter, approver or department admin); 404 otherwise.</summary>
		[HttpGet("GetApproval")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ApprovalDetailResult>> GetApproval(int approvalRequestId)
		{
			var view = await _approvals.GetViewAsync(DepartmentId, approvalRequestId, UserId, ClaimsAuthorizationHelper.IsUserDepartmentAdmin());
			if (view == null)
				return NotFound();

			var r = view.Request;
			var result = new ApprovalDetailResult
			{
				Data = new ApprovalDetailData
				{
					ApprovalRequestId = r.ApprovalRequestId, SubjectKind = r.SubjectKind, SubjectKindName = ((SignatureSubjectKinds)r.SubjectKind).ToString(), SubjectId = r.SubjectId,
					Title = r.Title, Amount = r.Amount, Currency = r.Currency, Status = ((ApprovalRequestStatuses)r.Status).ToString(), ContentCode = SignaturePolicy.ShortCode(r.ContentSha256),
					ContentSha256 = r.ContentSha256, SubmittedByUserId = r.SubmittedByUserId, SubmittedOn = r.SubmittedOn, DecidedOn = r.DecidedOn, RejectReason = r.RejectReason,
					StatementText = view.SignatureRequest?.StatementText, ExpiresOn = view.SignatureRequest?.ExpiresOn, Changed = view.Changed, CanApprove = view.CanApprove,
					CanWithdraw = view.CanWithdraw, Sealed = view.Sealed?.IsSealed == true, SealedDocumentId = view.Sealed?.IsSealed == true ? view.Sealed.SealedDocumentId : (int?)null,
					Steps = view.Steps.Select(s => new ApprovalStepData
					{
						StepOrder = s.Step.StepOrder, Label = s.Step.Label, Status = s.Signer == null ? null : ((SignatureSignerStatuses)s.Signer.Status).ToString(),
						IsTurn = s.IsTurn, Escalated = s.Step.EscalatedOn.HasValue, Delegated = !string.IsNullOrWhiteSpace(s.Step.DelegatedToUserIds),
						ApprovedByUserId = s.Signer?.Status == (int)SignatureSignerStatuses.Signed ? s.Signer.UserId : null, SignedOn = s.Signer?.SignedOn
					}).ToList()
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Approve the member's step at L2: the session's step-up evidence is read server-side, never from the body.</summary>
		[HttpPost("Approve")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ApprovalOutcomeResult>> Approve([FromBody] ApprovalActionInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ApprovalRequestId <= 0)
				return BadRequest();
			var proof = await ApiSignatureProof.BuildAsync(HttpContext, UserId, DepartmentId, _evidence, _mfaPolicy, cancellationToken);
			var outcome = await _approvals.ApproveAsync(DepartmentId, input.ApprovalRequestId, proof, false, cancellationToken);
			// The one step-up refusal (403 step_up_required, area approvals): verify with StepUp/Verify and send the same request again.
			if (outcome.Refusal == ApprovalRefusals.StepUpRequired)
				return await ApiStepUp.RefuseAsync(HttpContext, _stepUp, DepartmentId, UserId, Model.Security.StepUpArea.Approvals, cancellationToken);
			return Outcome(outcome);
		}

		/// <summary>Reject the member's step; a reason is required.</summary>
		[HttpPost("Reject")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ApprovalOutcomeResult>> Reject([FromBody] ApprovalActionInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ApprovalRequestId <= 0)
				return BadRequest();
			return Outcome(await _approvals.RejectAsync(DepartmentId, input.ApprovalRequestId, UserId, input.Reason, cancellationToken));
		}

		[HttpPost("Withdraw")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ApprovalOutcomeResult>> Withdraw([FromBody] ApprovalActionInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ApprovalRequestId <= 0)
				return BadRequest();
			return Outcome(await _approvals.WithdrawAsync(DepartmentId, input.ApprovalRequestId, UserId, ClaimsAuthorizationHelper.IsUserDepartmentAdmin(), cancellationToken));
		}

		/// <summary>Submit (or re-submit after a change) a subject to its kind's chain.</summary>
		[HttpPost("Submit")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ApprovalOutcomeResult>> Submit([FromBody] ApprovalSubmitData input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.SubjectId) || !ApprovalUses.IsApprovalKind((SignatureSubjectKinds)input.SubjectKind))
				return BadRequest();
			return Outcome(await _approvals.SubmitAsync(new ApprovalSubmitInput
			{
				DepartmentId = DepartmentId,
				Kind = (SignatureSubjectKinds)input.SubjectKind,
				SubjectId = input.SubjectId,
				UserId = UserId,
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				Culture = CultureInfo.CurrentUICulture.Name,
				IpAddress = HttpContext?.Request == null ? null : IpAddressHelper.GetRequestIP(HttpContext.Request, true),
				UserAgent = HttpContext?.Request?.Headers["User-Agent"].ToString()
			}, cancellationToken));
		}

		private ActionResult<ApprovalOutcomeResult> Outcome(ApprovalResult outcome)
		{
			var result = new ApprovalOutcomeResult
			{
				Data = new ApprovalOutcomeData
				{
					Succeeded = outcome.Succeeded,
					Refusal = outcome.Refusal.ToString(),
					ApprovalRequestId = outcome.Request?.ApprovalRequestId,
					Status = outcome.Request == null ? null : ((ApprovalRequestStatuses)outcome.Request.Status).ToString()
				},
				PageSize = 1,
				Status = outcome.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}
	}

	public class MyApprovalsResult : Models.v4.StandardApiResponseV4Base
	{
		public List<ApprovalInboxData> Data { get; set; } = new List<ApprovalInboxData>();
	}

	public class ApprovalInboxData
	{
		public int ApprovalRequestId { get; set; }
		public int SubjectKind { get; set; }
		public string SubjectKindName { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		public string StepLabel { get; set; }
		public DateTime SubmittedOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		public bool Escalated { get; set; }
		public string ContentCode { get; set; }
	}

	public class ApprovalDetailResult : Models.v4.StandardApiResponseV4Base
	{
		public ApprovalDetailData Data { get; set; }
	}

	public class ApprovalDetailData
	{
		public int ApprovalRequestId { get; set; }
		public int SubjectKind { get; set; }
		public string SubjectKindName { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		/// <summary>Pending, Approved, Rejected, Withdrawn, Superseded or Expired.</summary>
		public string Status { get; set; }
		public string ContentCode { get; set; }
		public string ContentSha256 { get; set; }
		public string StatementText { get; set; }
		public string SubmittedByUserId { get; set; }
		public DateTime SubmittedOn { get; set; }
		public DateTime? DecidedOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string RejectReason { get; set; }
		/// <summary>The subject changed after it was submitted (or approved): the approval no longer counts.</summary>
		public bool Changed { get; set; }
		public bool CanApprove { get; set; }
		public bool CanWithdraw { get; set; }
		public bool Sealed { get; set; }
		public int? SealedDocumentId { get; set; }
		public List<ApprovalStepData> Steps { get; set; } = new List<ApprovalStepData>();
	}

	public class ApprovalStepData
	{
		public int StepOrder { get; set; }
		public string Label { get; set; }
		public string Status { get; set; }
		public bool IsTurn { get; set; }
		public bool Escalated { get; set; }
		public bool Delegated { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? SignedOn { get; set; }
	}

	public class ApprovalActionInput
	{
		public int ApprovalRequestId { get; set; }
		public string Reason { get; set; }
	}

	public class ApprovalSubmitData
	{
		public int SubjectKind { get; set; }
		public string SubjectId { get; set; }
	}

	public class ApprovalOutcomeResult : Models.v4.StandardApiResponseV4Base
	{
		public ApprovalOutcomeData Data { get; set; }
	}

	public class ApprovalOutcomeData
	{
		public bool Succeeded { get; set; }
		/// <summary>None, StepUpRequired, NotYourTurn, NotAnApprover, SubjectChanged, Closed, ReasonRequired…</summary>
		public string Refusal { get; set; }
		public int? ApprovalRequestId { get; set; }
		public string Status { get; set; }
	}
}
