using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// e-Signature S3 for Omni (the older apps get no client change; their members sign in the browser from the notification link):
	/// the member's pending steps, one request's step view (the revision hash and statement an offline in-person capture binds
	/// to), signing a member step (step-up from the session's server-side evidence — v4 has no passkey-bound signing) and
	/// declining it. Offline in-person capture for an ordered request's external step is CaptureOffline with the request and
	/// step ids.
	/// </summary>
	public partial class SignaturesController
	{
		private readonly IReadinessAccessService _readiness;
		private readonly IWorkOrderAuthorizationService _workOrderAuthorization;
		private readonly IInventoryAuthorizationService _inventoryAuthorization;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUserProfileService _profiles;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _mfaPolicy;

		/// <summary>The member's steps whose turn it is, in open requests.</summary>
		[HttpGet("GetMyPendingSignatures")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PendingSignaturesResult>> GetMyPendingSignatures()
		{
			var items = await _requests.GetPendingForUserAsync(DepartmentId, UserId);
			var result = new PendingSignaturesResult
			{
				Data = items.Select(i => new PendingSignatureData
				{
					SignatureRequestId = i.SignatureRequestId,
					SignatureRequestSignerId = i.SignatureRequestSignerId,
					SubjectKind = i.SubjectKind,
					SubjectId = i.SubjectId,
					Title = i.Title,
					Role = i.Role,
					StepLabel = i.StepLabel,
					RequiredLevel = i.RequiredLevel,
					ExpiresOn = i.ExpiresOn,
					ContentCode = i.ContentCode
				}).ToList(),
				PageSize = items.Count,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// One request as the member sees it: the statement and revision hash every signature binds to, the member's step, every
		/// step's state (outside signers' contacts never leave the server) and why the member cannot sign now.
		/// </summary>
		[HttpGet("GetSignatureStep")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<SignatureStepResult>> GetSignatureStep(int requestId)
		{
			var view = await _requests.GetStepViewAsync(DepartmentId, requestId, UserId);
			if (view == null || !await MaySeeAsync(view))
				return NotFound();

			var names = await NamesAsync(view.Signers);
			var mine = view.MyStep ?? view.MyWaitingStep;
			var culture = CultureInfo.CurrentUICulture.Name;
			var result = new SignatureStepResult
			{
				Data = new SignatureStepData
				{
					SignatureRequestId = view.Request.SignatureRequestId,
					SubjectKind = view.Request.SubjectKind,
					SubjectId = view.Request.SubjectId,
					Title = view.Request.Title,
					Status = view.Request.Status,
					Ordered = view.Request.Ordered,
					Channel = view.Request.Channel,
					ExpiresOn = view.Request.ExpiresOn,
					ArtifactSha256 = view.Request.ArtifactSha256,
					ContentCode = SignaturePolicy.ShortCode(view.Request.ArtifactSha256),
					StatementText = view.Request.StatementText,
					ConsentText = SignatureTexts.Get("ConsentText", culture),
					ConsentVersion = SignatureTokens.ConsentTextVersion,
					CurrentRevisionSha256 = view.CurrentRevisionSha256,
					MySignerId = mine?.SignatureRequestSignerId,
					MyLevel = mine == null ? (int?)null : (int)view.MyLevel,
					CanSign = view.MyStep != null,
					Blocked = view.Blocked.ToString(),
					Steps = view.Signers.OrderBy(s => s.SignerOrder).ThenBy(s => s.SignatureRequestSignerId).Select(s => new SignatureStepSignerData
					{
						SignatureRequestSignerId = s.SignatureRequestSignerId,
						Order = s.SignerOrder,
						Role = s.Role,
						Kind = s.SignerKind,
						StepLabel = s.StepLabel,
						// Member names are operational; an outside signer shows the printed name only (no contact).
						Name = s.IsMember ? (s.UserId != null && names.TryGetValue(s.UserId, out var n) ? n : null) : s.ExternalName,
						AnyEligibleMember = s.IsMember && string.IsNullOrWhiteSpace(s.UserId),
						Level = (int)SignatureOrdering.StepLevel(s),
						Status = s.Status,
						SignedOn = s.SignedOn,
						IsTurn = SignatureOrdering.IsTurn(s, view.Signers, view.Request.Ordered)
					}).ToList()
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Signs the member's step at its level: L2 needs a step-up recorded for this session (no passkey on v4).</summary>
		[HttpPost("SignStep")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<SignatureStepOutcomeResult>> SignStep([FromBody] SignatureStepInputData input, CancellationToken cancellationToken)
		{
			if (input == null || input.SignatureRequestId <= 0)
				return BadRequest();

			var proof = await ApiSignatureProof.BuildAsync(HttpContext, UserId, DepartmentId, _evidence, _mfaPolicy, cancellationToken);
			var outcome = await _requests.SignStepAsync(new SignatureStepSignInput
			{
				DepartmentId = DepartmentId,
				SignatureRequestId = input.SignatureRequestId,
				SignatureRequestSignerId = input.SignatureRequestSignerId > 0 ? input.SignatureRequestSignerId : null,
				Proof = proof
			}, cancellationToken);
			return Outcome(outcome);
		}

		[HttpPost("DeclineStep")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<SignatureStepOutcomeResult>> DeclineStep([FromBody] SignatureStepInputData input, CancellationToken cancellationToken)
		{
			if (input == null || input.SignatureRequestId <= 0)
				return BadRequest();
			var outcome = await _requests.DeclineStepAsync(DepartmentId, input.SignatureRequestId, input.SignatureRequestSignerId > 0 ? input.SignatureRequestSignerId : null,
				UserId, input.Reason, cancellationToken);
			return Outcome(outcome);
		}

		private ActionResult<SignatureStepOutcomeResult> Outcome(SignatureRequestResult outcome)
		{
			var result = new SignatureStepOutcomeResult
			{
				Data = new SignatureStepOutcomeData
				{
					Succeeded = outcome.Succeeded,
					Refusal = outcome.Refusal.ToString(),
					SignatureRequestId = outcome.Request?.SignatureRequestId,
					RequestStatus = outcome.Request?.Status,
					SignatureRecordId = outcome.Record?.SignatureRecordId
				},
				PageSize = 1,
				Status = outcome.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			if (outcome.Refusal == SignatureRequestRefusals.RevisionChanged)
				return Conflict(result);
			return outcome.Succeeded ? Ok(result) : BadRequest(result);
		}

		/// <summary>A signer of the request, or a member who may act on its subject in its module.</summary>
		private async Task<bool> MaySeeAsync(SignatureStepView view) =>
			view.MyStep != null || view.MyWaitingStep != null ||
			view.Signers.Any(s => !string.IsNullOrWhiteSpace(s.UserId) && string.Equals(s.UserId.Trim(), UserId, StringComparison.OrdinalIgnoreCase)) ||
			await CanActOnRequestAsync(view.Request);

		/// <summary>The subject's module rule; for an evaluation also the member who requested it (T4 registers its own rule).</summary>
		private async Task<bool> CanActOnRequestAsync(SignatureRequestRecord request) =>
			request != null && (await CanActOnAsync((SignatureSubjectKinds)request.SubjectKind, request.SubjectId)
				|| request.SubjectKind == (int)SignatureSubjectKinds.SkillEvaluation && !string.IsNullOrWhiteSpace(request.CreatedByUserId)
					&& string.Equals(request.CreatedByUserId.Trim(), UserId, StringComparison.OrdinalIgnoreCase));

		/// <summary>The web pages' Readiness Pro rules: work-order managers of the order's group; members holding Issue Inventory.</summary>
		private async Task<bool> CanActOnReadinessAsync(SignatureSubjectKinds kind, string subjectId)
		{
			var info = await _requests.DescribeSubjectAsync(DepartmentId, kind, subjectId, UserId);
			if (info == null)
				return false;
			var admin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			if (kind == SignatureSubjectKinds.WorkOrderVendorAcceptance)
			{
				int? group = int.TryParse(info.ContextId, NumberStyles.None, CultureInfo.InvariantCulture, out var g) ? g : (int?)null;
				return admin || await _workOrderAuthorization.CanManageAsync(new ChecklistActor { DepartmentId = DepartmentId, UserId = UserId }, group);
			}

			var groupId = (await _groups.GetGroupForUserAsync(UserId, DepartmentId))?.DepartmentGroupId;
			try
			{
				await _inventoryAuthorization.RequireAsync(new InventoryActor { DepartmentId = DepartmentId, UserId = UserId }, true, PermissionTypes.IssueInventory, groupId);
				return true;
			}
			catch (InventoryException ex) when (ex.StatusCode is 403 or 404 or 409)
			{
				return false;
			}
		}

		private async Task<Dictionary<string, string>> NamesAsync(IEnumerable<SignatureRequestSigner> signers)
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var id in signers.Where(s => s.IsMember && !string.IsNullOrWhiteSpace(s.UserId)).Select(s => s.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				try
				{
					var name = (await _profiles.GetProfileByUserIdAsync(id))?.FullName?.AsFirstNameLastName;
					if (!string.IsNullOrWhiteSpace(name))
						names[id] = name.Trim();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "A signer's name could not be read.");
				}
			}
			return names;
		}
	}

	public class PendingSignaturesResult : Resgrid.Web.Services.Models.v4.StandardApiResponseV4Base
	{
		public List<PendingSignatureData> Data { get; set; } = new List<PendingSignatureData>();
	}

	public class PendingSignatureData
	{
		public int SignatureRequestId { get; set; }
		public int SignatureRequestSignerId { get; set; }
		public int SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public int Role { get; set; }
		public string StepLabel { get; set; }
		/// <summary>1 attest (session), 2 step-up.</summary>
		public int RequiredLevel { get; set; }
		public DateTime ExpiresOn { get; set; }
		public string ContentCode { get; set; }
	}

	public class SignatureStepResult : Resgrid.Web.Services.Models.v4.StandardApiResponseV4Base
	{
		public SignatureStepData Data { get; set; }
	}

	public class SignatureStepData
	{
		public int SignatureRequestId { get; set; }
		public int SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public int Status { get; set; }
		public bool Ordered { get; set; }
		/// <summary>How external steps sign: 0 remote link, 1 in person (Omni may capture these offline).</summary>
		public int Channel { get; set; }
		public DateTime ExpiresOn { get; set; }
		/// <summary>The revision hash every signature of the request binds to (an offline capture sends it back).</summary>
		public string ArtifactSha256 { get; set; }
		public string ContentCode { get; set; }
		public string StatementText { get; set; }
		public string ConsentText { get; set; }
		public string ConsentVersion { get; set; }
		/// <summary>The subject's hash now; when it differs from ArtifactSha256 the request can no longer be signed.</summary>
		public string CurrentRevisionSha256 { get; set; }
		public int? MySignerId { get; set; }
		public int? MyLevel { get; set; }
		public bool CanSign { get; set; }
		/// <summary>None, NotYourTurn, NotEligible, NotASigner, RevisionChanged, RequestClosed…</summary>
		public string Blocked { get; set; }
		public List<SignatureStepSignerData> Steps { get; set; } = new List<SignatureStepSignerData>();
	}

	public class SignatureStepSignerData
	{
		public int SignatureRequestSignerId { get; set; }
		public int Order { get; set; }
		public int Role { get; set; }
		/// <summary>0 external (L4), 1 member (L1/L2).</summary>
		public int Kind { get; set; }
		public string StepLabel { get; set; }
		public string Name { get; set; }
		public bool AnyEligibleMember { get; set; }
		public int Level { get; set; }
		public int Status { get; set; }
		public DateTime? SignedOn { get; set; }
		public bool IsTurn { get; set; }
	}

	public class SignatureStepInputData
	{
		public int SignatureRequestId { get; set; }
		public int? SignatureRequestSignerId { get; set; }
		/// <summary>DeclineStep only.</summary>
		public string Reason { get; set; }
	}

	public class SignatureStepOutcomeResult : Resgrid.Web.Services.Models.v4.StandardApiResponseV4Base
	{
		public SignatureStepOutcomeData Data { get; set; }
	}

	public class SignatureStepOutcomeData
	{
		public bool Succeeded { get; set; }
		/// <summary>None, StepUpRequired, NotYourTurn, NotEligible, NotASigner, RevisionChanged, RequestClosed…</summary>
		public string Refusal { get; set; }
		public int? SignatureRequestId { get; set; }
		public int? RequestStatus { get; set; }
		public int? SignatureRecordId { get; set; }
	}
}
