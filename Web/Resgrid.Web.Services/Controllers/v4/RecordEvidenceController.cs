using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Evidence capture over the v4 Records contract (RMS plan section 4.5, RMS-3): readiness, run-card activation,
	/// tracking fixes, promoted chat, inventory usage and certification validity.
	/// <para>
	/// A capture is a write to an official record, so it needs Record_Create and a stated reason; reading one only
	/// needs Record_View plus visibility of the Record it supports. Artifacts are immutable: there is no update or
	/// delete endpoint, and a correction is a fresh capture that supersedes the old one.
	/// </para>
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RecordEvidenceController : V4AuthenticatedApiControllerbase, IActionFilter
	{
		private readonly IRecordsEvidenceService _evidence;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IRecordsApiIdempotencyService _idempotency;

		private SystemPrincipalRecordGrant _systemGrant;
		private bool _systemGrantResolved;

		public RecordEvidenceController(IRecordsEvidenceService evidence, IRecordsCutoverService cutoverService,
			IRecordsAuthorizationService recordsAuthorizationService, IFeatureToggleService featureToggleService, IRecordsApiIdempotencyService idempotency)
		{
			_evidence = evidence;
			_cutoverService = cutoverService;
			_recordsAuthorizationService = recordsAuthorizationService;
			_featureToggleService = featureToggleService;
			_idempotency = idempotency;
		}

		/// <summary>Same system-principal gate as the other Records controllers (registry section 4.4).</summary>
		public void OnActionExecuting(ActionExecutingContext context)
		{
			if (!RecordsSystemPrincipal.IsSystemPrincipal(User))
				return;

			if (SystemGrant == null)
				context.Result = Problem(statusCode: StatusCodes.Status403Forbidden,
					title: "This system principal has no configured Record grant for this department.", type: "record_grant_missing");
		}

		public void OnActionExecuted(ActionExecutedContext context)
		{
		}

		private SystemPrincipalRecordGrant SystemGrant
		{
			get
			{
				if (!_systemGrantResolved)
				{
					_systemGrant = RecordsSystemPrincipal.ResolveGrant(User, DepartmentId);
					_systemGrantResolved = true;
				}

				return _systemGrant;
			}
		}

		/// <summary>
		/// Which of the six sources can produce evidence for this department right now. A source that is not built
		/// or not configured answers "unavailable, and here is why" — which is a different answer from "there was
		/// no evidence" and is why the list always carries all six.
		/// </summary>
		[HttpGet("GetSources")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordEvidenceSourcesResult>> GetSources()
		{
			if (!await FlagOnAsync())
				return NotFound();

			var states = await _evidence.GetSourceStatesAsync(DepartmentId);
			var result = new RecordEvidenceSourcesResult { Data = states.Select(RecordEvidenceApiMapper.ToSource).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Artifacts on the working draft, or on a revision when one is named. Manifests are not carried in a list.</summary>
		[HttpGet("GetEvidence")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordEvidenceListResult>> GetEvidence(string recordId, string revisionId, bool includeSuperseded = false)
		{
			if (!await FlagOnAsync())
				return NotFound();
			if (string.IsNullOrWhiteSpace(recordId) || !await CanViewRecordAsync(recordId))
				return NotFound();

			var artifacts = await _evidence.GetForRecordAsync(DepartmentId, recordId, revisionId, includeSuperseded);
			var canViewRestricted = ClaimsAuthorizationHelper.CanViewRestrictedRecords();
			var result = new RecordEvidenceListResult
			{
				Data = artifacts.Select(a => RecordEvidenceApiMapper.ToArtifact(a, canViewRestricted)).ToList(),
				Status = ResponseHelper.Success
			};
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>One artifact with its manifest; the manifest is withheld and flagged when it is classified above the caller.</summary>
		[HttpGet("GetArtifact")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordEvidenceResult>> GetArtifact(string id)
		{
			var artifact = await LoadAuthorizedAsync(id);
			if (artifact == null)
				return NotFound();

			var result = new RecordEvidenceResult
			{
				Data = RecordEvidenceApiMapper.ToArtifact(artifact, ClaimsAuthorizationHelper.CanViewRestrictedRecords(), true),
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Re-computes the checksum from the stored manifest; false means the artifact was tampered with.</summary>
		[HttpGet("Verify")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordEvidenceVerifyResult>> Verify(string id)
		{
			var artifact = await LoadAuthorizedAsync(id);
			if (artifact == null)
				return NotFound();

			var intact = await _evidence.VerifyAsync(DepartmentId, id);
			var result = new RecordEvidenceVerifyResult
			{
				Data = new RecordEvidenceVerifyData { ArtifactId = id, Intact = intact, Checksum = artifact.Checksum },
				PageSize = 1,
				Status = intact ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Captures one artifact. 409 when the source has nothing to give for this department — that is a real
		/// answer about the source, not a client error, and the reason is carried in the problem detail.
		/// </summary>
		[HttpPost("Capture")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordEvidenceResult>> Capture(CaptureRecordEvidenceInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RecordId) || string.IsNullOrWhiteSpace(input.CaptureReason))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var origin = RecordsApiHelper.ResolveOrigin(input.OriginClient);
			var gate = await FieldClientGateAsync(origin);
			if (gate != null)
				return gate;

			if (!await CanViewRecordAsync(input.RecordId))
				return NotFound();
			if (!ClaimsAuthorizationHelper.CanCreateRecord())
				return Forbid();

			var key = RecordsApiHelper.ResolveIdempotencyKey(input.IdempotencyKey, Request);
			if (key != null)
			{
				var replayed = await _idempotency.TryGetRecordIdAsync(DepartmentId, UserId, key, nameof(Capture));
				if (!string.IsNullOrWhiteSpace(replayed))
				{
					var existing = await _evidence.GetAsync(DepartmentId, replayed);
					if (existing != null)
						return Ok(Wrap(existing, ResponseHelper.Success));
				}
			}

			try
			{
				var request = RecordEvidenceApiMapper.ToCaptureRequest(input, DepartmentId, UserId, origin);
				var artifact = await _evidence.CaptureAsync(request, ClaimsAuthorizationHelper.CanViewRestrictedRecords(), cancellationToken);
				if (artifact == null)
					return Problem(statusCode: StatusCodes.Status409Conflict, title: "That evidence source has nothing to capture for this record.", type: "evidence_unavailable");

				if (key != null)
					await _idempotency.RememberAsync(DepartmentId, UserId, key, nameof(Capture), artifact.RmsEvidenceArtifactId);

				return StatusCode(StatusCodes.Status201Created, Wrap(artifact, ResponseHelper.Created));
			}
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
			catch (UnauthorizedAccessException ex)
			{
				return Problem(statusCode: StatusCodes.Status403Forbidden, title: ex.Message, type: "record_restricted");
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		#region Helpers

		private async Task<bool> FlagOnAsync()
		{
			return (await _cutoverService.GetModuleStateAsync(DepartmentId)).FlagEnabled;
		}

		private async Task<ActionResult> UsableAsync()
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();
			if (!moduleState.RecordsUsable)
				return Problem(statusCode: StatusCodes.Status409Conflict, title: "Records is not activated for this department.", type: "records_not_activated");
			return null;
		}

		private async Task<ActionResult> FieldClientGateAsync(RmsOriginClient origin)
		{
			var flag = RecordsApiHelper.FieldFlagFor(origin);
			if (flag == null || await _featureToggleService.IsEnabledAsync(flag, DepartmentId))
				return null;
			return Problem(statusCode: StatusCodes.Status403Forbidden, title: $"Field Records are not enabled for the {origin} app in this department.", type: "field_records_disabled");
		}

		private async Task<bool> CanViewRecordAsync(string recordId)
		{
			var grant = SystemGrant;
			if (grant != null)
				return await _recordsAuthorizationService.CanSystemPrincipalViewRecordAsync(grant, recordId);

			return await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, recordId, DepartmentId);
		}

		/// <summary>An artifact is only as visible as the Record it supports.</summary>
		private async Task<RmsEvidenceArtifact> LoadAuthorizedAsync(string artifactId)
		{
			if (string.IsNullOrWhiteSpace(artifactId) || !await FlagOnAsync())
				return null;

			var artifact = await _evidence.GetAsync(DepartmentId, artifactId);
			if (artifact == null || !await CanViewRecordAsync(artifact.RecordId))
				return null;

			return artifact;
		}

		private RecordEvidenceResult Wrap(RmsEvidenceArtifact artifact, string status)
		{
			var result = new RecordEvidenceResult
			{
				Data = RecordEvidenceApiMapper.ToArtifact(artifact, ClaimsAuthorizationHelper.CanViewRestrictedRecords(), true),
				PageSize = 1,
				Status = status
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion
	}
}
