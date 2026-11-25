using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.ServicesCore.Helpers;
using AspNetAuthorization = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// External signatures for apps (e-Signature S2). Built for Omni (no client changes to the older apps): read a subject's
	/// signatures and requests, fetch what an offline in-person capture must bind to (the revision hash and the statement), and
	/// submit an offline in-person capture (Method InPersonOffline), which is refused when the subject's revision changed since
	/// the device bound it. Every call re-checks the member's right to act on the subject in its own module.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public partial class SignaturesController : V4AuthenticatedApiControllerbase
	{
		private readonly ISignatureRequestService _requests;
		private readonly IFeatureToggleService _flags;
		private readonly ITimeTrackingService _timeTracking;
		private readonly IDeploymentService _deployments;
		private readonly IBusinessOperationsAccessService _businessOperations;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsInvestigationsService _investigations;
		private readonly AspNetAuthorization _authorization;
		private readonly IReadOnlyList<ISignatureSubjectAccessRule> _accessRules;

		public SignaturesController(ISignatureRequestService requests, IFeatureToggleService flags, ITimeTrackingService timeTracking, IDeploymentService deployments,
			IBusinessOperationsAccessService businessOperations, IRecordsCutoverService cutover, IRecordsInvestigationsService investigations, AspNetAuthorization authorization,
			IReadinessAccessService readiness, IWorkOrderAuthorizationService workOrderAuthorization, IInventoryAuthorizationService inventoryAuthorization,
			IDepartmentGroupsService groups, IUserProfileService profiles, IMfaEvidenceService evidence, IMfaPolicyService mfaPolicy,
			IEnumerable<ISignatureSubjectAccessRule> accessRules)
		{
			_accessRules = (accessRules ?? Enumerable.Empty<ISignatureSubjectAccessRule>()).ToList();
			_readiness = readiness;
			_workOrderAuthorization = workOrderAuthorization;
			_inventoryAuthorization = inventoryAuthorization;
			_groups = groups;
			_profiles = profiles;
			_evidence = evidence;
			_mfaPolicy = mfaPolicy;
			_requests = requests;
			_flags = flags;
			_timeTracking = timeTracking;
			_deployments = deployments;
			_businessOperations = businessOperations;
			_cutover = cutover;
			_investigations = investigations;
			_authorization = authorization;
		}

		/// <summary>The subject's external-signer requests (contacts masked) and signatures, with the current revision hash.</summary>
		[HttpGet("GetSubjectSignatures")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<SubjectSignaturesResult>> GetSubjectSignatures(int kind, string subjectId)
		{
			var subjectKind = (SignatureSubjectKinds)kind;
			if (ExternalSignatureUse.For(subjectKind) == null || !await CanActOnAsync(subjectKind, subjectId))
				return NotFound();

			var summary = await _requests.GetSubjectSummaryAsync(DepartmentId, subjectKind, subjectId, UserId);
			var result = new SubjectSignaturesResult { Data = new SubjectSignaturesData
			{
				Kind = kind,
				SubjectId = subjectId,
				CurrentRevisionSha256 = summary.CurrentRevisionSha256,
				RemoteAvailable = summary.RemoteAvailable,
				SealedDocumentId = summary.LatestSealed?.IsSealed == true ? summary.LatestSealed.SealedDocumentId : (int?)null,
				Requests = summary.Requests.Select(r => new SubjectSignatureRequestData
				{
					SignatureRequestId = r.SignatureRequestId,
					Status = r.Status,
					Channel = r.Channel,
					ExpiresOn = r.ExpiresOn,
					CompletedOn = r.CompletedOn,
					ContactMasked = summary.Signers.FirstOrDefault(s => s.SignatureRequestId == r.SignatureRequestId)?.ContactMasked
				}).ToList(),
				Signatures = summary.Signatures.Select(s => new SubjectSignatureData
				{
					SignatureRecordId = s.Record.SignatureRecordId,
					SignerName = s.Record.SignerNameSnapshot,
					Level = s.Record.Level,
					Method = s.Record.Method,
					SignedOn = s.Record.SignedOn,
					ArtifactSha256 = s.Record.ArtifactSha256,
					State = (int)s.State
				}).ToList()
			}};
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>What an in-person capture binds to: the title, the revision hash, the statement and the consent text.</summary>
		[HttpGet("GetSigningArtifact")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<SigningArtifactResult>> GetSigningArtifact(int kind, string subjectId)
		{
			var subjectKind = (SignatureSubjectKinds)kind;
			if (ExternalSignatureUse.For(subjectKind) == null || !await CanActOnAsync(subjectKind, subjectId))
				return NotFound();

			var info = await _requests.DescribeSubjectAsync(DepartmentId, subjectKind, subjectId, UserId);
			if (info == null)
				return NotFound();

			var culture = CultureInfo.CurrentUICulture.Name;
			var result = new SigningArtifactResult { Data = new SigningArtifactData
			{
				Kind = kind,
				SubjectId = info.SubjectId,
				Title = info.Title,
				RevisionId = info.RevisionId,
				RevisionSha256 = info.RevisionSha256,
				ContentCode = SignaturePolicy.ShortCode(info.RevisionSha256),
				StatementText = SignatureTexts.Get("Statement" + subjectKind, culture, info.Title, SignaturePolicy.ShortCode(info.RevisionSha256)),
				ConsentText = SignatureTexts.Get("ConsentText", culture),
				ConsentVersion = SignatureTokens.ConsentTextVersion
			}};
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>An offline in-person capture (Omni outbox): accepted only if the subject is still at the bound revision.</summary>
		[HttpPost("CaptureOffline")]
		[RequestSizeLimit(2 * 1024 * 1024)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<ActionResult<CaptureOfflineResult>> CaptureOffline([FromBody] CaptureOfflineInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			var subjectKind = (SignatureSubjectKinds)input.Kind;
			// S3: a capture for an existing request's step is allowed to whoever may act on that request.
			var stepRequest = input.SignatureRequestId > 0 ? await _requests.GetAsync(DepartmentId, input.SignatureRequestId.Value) : null;
			if (ExternalSignatureUse.For(subjectKind) == null || !(stepRequest != null ? await CanActOnRequestAsync(stepRequest) : await CanActOnAsync(subjectKind, input.SubjectId)))
				return NotFound();

			var outcome = await _requests.CaptureOfflineAsync(new OfflineSignatureCapture
			{
				DepartmentId = DepartmentId,
				SubjectKind = subjectKind,
				SubjectId = input.SubjectId,
				BoundRevisionSha256 = input.BoundRevisionSha256,
				PrintedName = input.PrintedName,
				SignatureImageDataUrl = input.SignatureImage,
				WitnessName = input.WitnessName,
				Role = input.Role.HasValue && Enum.IsDefined(typeof(SignatureSignerRoles), input.Role.Value) ? (SignatureSignerRoles)input.Role.Value : (SignatureSignerRoles?)null,
				CapturedOnUtc = input.CapturedOnUtc,
				DeviceInfo = input.DeviceInfo,
				CapturedByUserId = UserId,
				IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
				UserAgent = Request.Headers["User-Agent"].ToString(),
				// S3: an ordered request's external step (null keeps the S2 one-signer capture).
				SignatureRequestId = input.SignatureRequestId > 0 ? input.SignatureRequestId : null,
				SignatureRequestSignerId = input.SignatureRequestSignerId > 0 ? input.SignatureRequestSignerId : null
			}, cancellationToken);

			var result = new CaptureOfflineResult { Data = new CaptureOfflineData
			{
				Succeeded = outcome.Succeeded,
				Refusal = outcome.Refusal.ToString(),
				SignatureRequestId = outcome.Request?.SignatureRequestId,
				SignatureRecordId = outcome.Record?.SignatureRecordId,
				SealedDocumentId = outcome.Request?.SealedDocumentId
			}};
			result.Status = outcome.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure;
			ResponseHelper.PopulateV4ResponseData(result);
			if (outcome.Refusal == SignatureRequestRefusals.RevisionChanged)
				return Conflict(result);
			return outcome.Succeeded ? Ok(result) : BadRequest(result);
		}

		/// <summary>The member may act on the subject in its own module (the web pages' rule).</summary>
		[NonAction]
		public async Task<bool> CanActOnAsync(SignatureSubjectKinds kind, string subjectId)
		{
			if (string.IsNullOrWhiteSpace(subjectId))
				return false;
			var admin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			try
			{
				switch (kind)
				{
					case SignatureSubjectKinds.TimeReport:
					{
						if (!await _flags.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId))
							return false;
						var report = await _timeTracking.GetTimeReportByIdAsync(subjectId, DepartmentId);
						var deployment = report == null || report.DepartmentId != DepartmentId ? null : await _deployments.GetDeploymentByIdAsync(report.DeploymentId, DepartmentId);
						return deployment != null && (await _deployments.GetTimeAccessAsync(deployment, UserId, admin || ClaimsAuthorizationHelper.CanManageDeployments())).CanActOn(report);
					}
					case SignatureSubjectKinds.Bid:
						return (admin || ClaimsAuthorizationHelper.CanManageBids()) && await _businessOperations.CanUseContractorBillingAsync(DepartmentId);
					case SignatureSubjectKinds.ServiceContract:
						return (admin || ClaimsAuthorizationHelper.CanManageContracts()) && await _businessOperations.CanUseContractorBillingAsync(DepartmentId);
					case SignatureSubjectKinds.RecordsInspection:
						return await RecordsModuleOnAsync(FeatureFlagKeys.RecordsPreventionInspections)
							&& (await _authorization.AuthorizeAsync(User, ResgridResources.Record_PreventionAdmin)).Succeeded
							&& await _requests.DescribeSubjectAsync(DepartmentId, kind, subjectId, UserId) != null;
					case SignatureSubjectKinds.CustodyTransfer:
					{
						if (!await RecordsModuleOnAsync(FeatureFlagKeys.RecordsInvestigations) || !(await _authorization.AuthorizeAsync(User, ResgridResources.RecordRestricted_View)).Succeeded)
							return false;
						var info = await _requests.DescribeSubjectAsync(DepartmentId, kind, subjectId, UserId);
						if (info?.ContextId == null)
							return false;
						var chain = await _investigations.GetCustodyChainAsync(DepartmentId, UserId, info.ContextId) ?? new List<RmsInvestigationCustody>();
						return chain.Any(c => string.Equals(c.RmsInvestigationCustodyId, subjectId, StringComparison.OrdinalIgnoreCase));
					}
					case SignatureSubjectKinds.WorkOrderVendorAcceptance:
					case SignatureSubjectKinds.InventoryIssueReceipt:
						return await CanActOnReadinessAsync(kind, subjectId);
					default:
					{
						// A module's own rule (T4 registers SkillEvaluation).
						var rule = _accessRules.FirstOrDefault(r => r.Kind == kind);
						return rule != null && await rule.CanActOnAsync(DepartmentId, UserId, subjectId);
					}
				}
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Signature subject access could not be checked.");
				return false;
			}
		}

		private async Task<bool> RecordsModuleOnAsync(string flag)
		{
			var state = await _cutover.GetModuleStateAsync(DepartmentId);
			return state != null && state.FlagEnabled && await _flags.IsEnabledAsync(flag, DepartmentId);
		}
	}

	public class SubjectSignaturesResult : Resgrid.Web.Services.Models.v4.StandardApiResponseV4Base
	{
		public SubjectSignaturesData Data { get; set; }
	}

	public class SubjectSignaturesData
	{
		public int Kind { get; set; }
		public string SubjectId { get; set; }
		public string CurrentRevisionSha256 { get; set; }
		public bool RemoteAvailable { get; set; }
		public int? SealedDocumentId { get; set; }
		public List<SubjectSignatureRequestData> Requests { get; set; } = new List<SubjectSignatureRequestData>();
		public List<SubjectSignatureData> Signatures { get; set; } = new List<SubjectSignatureData>();
	}

	public class SubjectSignatureRequestData
	{
		public int SignatureRequestId { get; set; }
		public int Status { get; set; }
		public int Channel { get; set; }
		public DateTime ExpiresOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		/// <summary>Masked ("j***@example.com"); the full contact never leaves the server.</summary>
		public string ContactMasked { get; set; }
	}

	public class SubjectSignatureData
	{
		public int SignatureRecordId { get; set; }
		public string SignerName { get; set; }
		public int Level { get; set; }
		public int Method { get; set; }
		public DateTime SignedOn { get; set; }
		public string ArtifactSha256 { get; set; }
		/// <summary>SignatureVerificationStates: 0 valid, 1 content changed, 2 voided, 3 subject missing.</summary>
		public int State { get; set; }
	}

	public class SigningArtifactResult : Resgrid.Web.Services.Models.v4.StandardApiResponseV4Base
	{
		public SigningArtifactData Data { get; set; }
	}

	public class SigningArtifactData
	{
		public int Kind { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public string RevisionId { get; set; }
		public string RevisionSha256 { get; set; }
		public string ContentCode { get; set; }
		public string StatementText { get; set; }
		public string ConsentText { get; set; }
		public string ConsentVersion { get; set; }
	}

	public class CaptureOfflineInput
	{
		public int Kind { get; set; }
		public string SubjectId { get; set; }
		/// <summary>The revision hash the device showed (from GetSigningArtifact while it was online).</summary>
		public string BoundRevisionSha256 { get; set; }
		public string PrintedName { get; set; }
		/// <summary>data:image/png;base64,… (at most 512 KB).</summary>
		public string SignatureImage { get; set; }
		public string WitnessName { get; set; }
		public int? Role { get; set; }
		public DateTime? CapturedOnUtc { get; set; }
		public string DeviceInfo { get; set; }
		/// <summary>S3 (optional): the ordered request whose external step this capture answers (from GetSignatureStep).</summary>
		public int? SignatureRequestId { get; set; }
		/// <summary>S3 (optional): the external step; null takes the first external step whose turn it is.</summary>
		public int? SignatureRequestSignerId { get; set; }
	}

	public class CaptureOfflineResult : Resgrid.Web.Services.Models.v4.StandardApiResponseV4Base
	{
		public CaptureOfflineData Data { get; set; }
	}

	public class CaptureOfflineData
	{
		public bool Succeeded { get; set; }
		/// <summary>None, RevisionChanged, SubjectNotFound, InvalidRequest…</summary>
		public string Refusal { get; set; }
		public int? SignatureRequestId { get; set; }
		public int? SignatureRecordId { get; set; }
		public int? SealedDocumentId { get; set; }
	}
}
