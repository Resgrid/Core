using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Public-records and access-to-information requests over the v4 Records contract (RMS plan section 4.7, RMS-3).
	/// <para>
	/// Every endpoint requires <c>RecordDisclosure_Update</c>, including the reads: a public-records queue names who
	/// asked for what, which is itself restricted in most jurisdictions, so this is not a Record_View surface. A
	/// system principal is refused outright — a statutory release is a human act, and no service account is ever
	/// granted a disclosure policy.
	/// </para>
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize(Policy = ResgridResources.RecordDisclosure_Update)]
	public class DisclosuresController : V4AuthenticatedApiControllerbase
	{
		private readonly IRecordsDisclosureService _disclosures;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsApiIdempotencyService _idempotency;

		public DisclosuresController(IRecordsDisclosureService disclosures, IRecordsCutoverService cutoverService, IRecordsApiIdempotencyService idempotency, IRecordsAuthorizationService authorization)
		{
			_disclosures = disclosures;
			_authorization = authorization;
			_cutoverService = cutoverService;
			_idempotency = idempotency;
		}

		#region Reads

		/// <summary>The disclosure queue, newest first; <paramref name="state"/> filters to one state.</summary>
		[HttpGet("GetRequests")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DisclosureRequestsResult>> GetRequests(int? state, int skip = 0, int take = 50)
		{
			if (!await FlagOnAsync())
				return NotFound();

			var states = state.HasValue ? new List<RmsDisclosureState> { (RmsDisclosureState)state.Value } : null;
			var requests = await _disclosures.QueryAsync(DepartmentId, UserId, states, Math.Max(0, skip), Math.Max(1, Math.Min(RecordsController.MaxPageSize, take)));
			var canViewRestricted = await _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);

			var result = new DisclosureRequestsResult
			{
				Data = requests.Select(r => DisclosuresApiMapper.ToRequest(r, canViewRestricted)).ToList(),
				Status = ResponseHelper.Success
			};
			result.Total = skip + result.Data.Count;
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpGet("GetRequest")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<DisclosureRequestResult>> GetRequest(string id)
		{
			if (!await FlagOnAsync())
				return NotFound();

			var request = await _disclosures.GetAsync(DepartmentId, UserId, id);
			if (request == null)
				return NotFound();

			return Ok(await WrapAsync(request));
		}

		/// <summary>
		/// What the scope resolves to right now, through the same authorization path as the Records queue. Drafts
		/// are listed for separate review; automatic production requires a saved revision.
		/// </summary>
		[HttpGet("PreviewScope")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<DisclosureScopePreviewResult>> PreviewScope(string id, int take = 200)
		{
			if (!await FlagOnAsync())
				return NotFound();
			if (await _disclosures.GetAsync(DepartmentId, UserId, id) == null)
				return NotFound();

			try
			{
				var preview = await _disclosures.PreviewScopeAsync(DepartmentId, UserId, id, Math.Max(1, Math.Min(RecordsController.MaxPageSize, take)));
				var result = new DisclosureScopePreviewResult { Data = DisclosuresApiMapper.ToPreview(preview), PageSize = preview.Items.Count, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		/// <summary>The request's productions; the released content itself is only carried by GetProduction.</summary>
		[HttpGet("GetProductions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<DisclosureProductionsResult>> GetProductions(string requestId)
		{
			if (!await FlagOnAsync())
				return NotFound();
			if (await _disclosures.GetAsync(DepartmentId, UserId, requestId) == null)
				return NotFound();

			var productions = await _disclosures.GetProductionsAsync(DepartmentId, UserId, requestId);
			var result = new DisclosureProductionsResult { Data = productions.Select(p => DisclosuresApiMapper.ToProduction(p)).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// One production with its redacted artifact. The request id is required rather than inferred: a production
		/// is only meaningful as part of its request, and reading one is the act of re-opening a release.
		/// </summary>
		[HttpGet("GetProduction")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<DisclosureProductionResult>> GetProduction(string requestId, string id)
		{
			var production = await LoadProductionAsync(requestId, id);
			if (production == null)
				return NotFound();

			var result = new DisclosureProductionResult { Data = DisclosuresApiMapper.ToProduction(production, true), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Re-computes a production's checksum from its stored artifact; false means it was tampered with.</summary>
		[HttpGet("VerifyProduction")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<DisclosureVerifyResult>> VerifyProduction(string requestId, string id)
		{
			if (await LoadProductionAsync(requestId, id) == null)
				return NotFound();

			var intact = await _disclosures.VerifyProductionAsync(DepartmentId, id);
			var result = new DisclosureVerifyResult
			{
				Data = new DisclosureVerifyData { ProductionId = id, Intact = intact },
				PageSize = 1,
				Status = intact ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		#endregion

		#region Workflow

		/// <summary>Logs a request and starts the statutory clock.</summary>
		[HttpPost("Create")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult<DisclosureRequestResult>> Create(CreateDisclosureRequestInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var key = RecordsApiHelper.ResolveIdempotencyKey(input.IdempotencyKey, Request);
			if (key != null)
			{
				var replayed = await _idempotency.TryGetRecordIdAsync(DepartmentId, UserId, key, nameof(Create));
				if (!string.IsNullOrWhiteSpace(replayed))
				{
					var existing = await _disclosures.GetAsync(DepartmentId, UserId, replayed);
					if (existing != null)
						return Ok(await WrapAsync(existing));
				}
			}

			try
			{
				var created = await _disclosures.CreateRequestAsync(DepartmentId, UserId, new RmsDisclosureRequest
				{
					RequesterName = input.RequesterName,
					RequesterOrganization = input.RequesterOrganization,
					RequesterContact = input.RequesterContact,
					ReceivedOn = RecordsApiHelper.Utc(input.ReceivedOn) ?? DateTime.UtcNow,
					StatutoryDueOn = RecordsApiHelper.Utc(input.StatutoryDueOn),
					JurisdictionProfile = input.JurisdictionProfile,
					ScopeNarrative = input.ScopeNarrative,
					AssignedToUserId = input.AssignedToUserId,
					RedactionProfile = input.RedactionProfile
				}, cancellationToken);

				if (key != null)
					await _idempotency.RememberAsync(DepartmentId, UserId, key, nameof(Create), created.RmsDisclosureRequestId);

				return StatusCode(StatusCodes.Status201Created, await WrapAsync(created, ResponseHelper.Created));
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		/// <summary>Saves the scope query and narrative; refused once a production exists, because the scope is what was produced against.</summary>
		[HttpPost("SaveScope")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<ActionResult<DisclosureRequestResult>> SaveScope(SaveDisclosureScopeInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RequestId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;
			if (await _disclosures.GetAsync(DepartmentId, UserId, input.RequestId) == null)
				return NotFound();

			try
			{
				var saved = await _disclosures.SaveScopeAsync(DepartmentId, UserId, input.RequestId, input.ScopeNarrative,
					DisclosuresApiMapper.ToScopeQuery(input.Scope), input.RedactionProfile, cancellationToken);
				return Ok(await WrapAsync(saved, ResponseHelper.Updated));
			}
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		[HttpGet("GetReview")]
		public async Task<IActionResult> GetReview(string requestId, string profile = null)
		{
			var usable = await UsableAsync(); if (usable != null) return usable;
			try { Response.Headers["Cache-Control"] = "no-store"; return Ok(await _disclosures.GetReviewAsync(DepartmentId, UserId, requestId, profile)); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Problem(ex.Message, statusCode: 409); }
		}
		[HttpGet("ReviewAttachment")]
		public async Task<IActionResult> ReviewAttachment(string requestId, string recordId, string revisionId, string attachmentId, string profile)
		{
			var usable = await UsableAsync(); if (usable != null) return usable;
			try
			{
				var file = await _disclosures.GetReviewAttachmentAsync(DepartmentId, UserId, requestId, recordId, revisionId, attachmentId, profile);
				if (file == null) return NotFound(); Response.Headers["Cache-Control"] = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
				return File(file.Data, "application/octet-stream", file.FileName);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Problem(ex.Message, statusCode: 409); }
		}
		[HttpGet("Download")]
		public async Task<IActionResult> Download(string productionId, string format = "zip")
		{
			var usable = await UsableAsync(); if (usable != null) return usable;
			try { var packet = await _disclosures.DownloadAsync(DepartmentId, UserId, productionId, format); Response.Headers["Cache-Control"] = "no-store"; return File(packet.Data, packet.ContentType, packet.FileName); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Problem(ex.Message, statusCode: 409); }
		}
		/// <summary>Builds a new immutable production: redacted content, produced-set snapshot, redaction log and checksum.</summary>
		[HttpPost("Produce")]
		[RequestSizeLimit(100 * 1024 * 1024)]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult<DisclosureProductionResult>> Produce(DisclosureCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RequestId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;
			if (await _disclosures.GetAsync(DepartmentId, UserId, input.RequestId) == null)
				return NotFound();

			try
			{
				var production = await _disclosures.ProduceAsync(DepartmentId, UserId, input.RequestId, input.RedactionProfile, cancellationToken, input.Review);
				var result = new DisclosureProductionResult { Data = DisclosuresApiMapper.ToProduction(production), PageSize = 1, Status = ResponseHelper.Created };
				ResponseHelper.PopulateV4ResponseData(result);
				return StatusCode(StatusCodes.Status201Created, result);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		/// <summary>Releases a prepared production to the requester and closes the statutory clock.</summary>
		[HttpPost("Release")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DisclosureProductionResult>> Release(DisclosureCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RequestId) || string.IsNullOrWhiteSpace(input.ProductionId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;
			if (await LoadProductionAsync(input.RequestId, input.ProductionId) == null)
				return NotFound();

			try
			{
				var released = await _disclosures.ReleaseAsync(DepartmentId, UserId, input.ProductionId, cancellationToken, input.DeliveryMethod, input.DeliveryReference);
				var result = new DisclosureProductionResult { Data = DisclosuresApiMapper.ToProduction(released), PageSize = 1, Status = ResponseHelper.Updated };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		/// <summary>Closes a request without release — denied under an exemption, or withdrawn. A reason is required.</summary>
		[HttpPost("Close")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DisclosureRequestResult>> Close(DisclosureCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RequestId) || !input.Disposition.HasValue || string.IsNullOrWhiteSpace(input.Reason))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;
			if (await _disclosures.GetAsync(DepartmentId, UserId, input.RequestId) == null)
				return NotFound();

			try
			{
				var closed = await _disclosures.CloseAsync(DepartmentId, UserId, input.RequestId, (RmsDisclosureState)input.Disposition.Value, input.Reason, cancellationToken);
				return Ok(await WrapAsync(closed, ResponseHelper.Updated));
			}
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		#endregion

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

		/// <summary>A production is only reachable through its own request, so a stray id cannot walk another department's release.</summary>
		private async Task<RmsDisclosureProduction> LoadProductionAsync(string requestId, string productionId)
		{
			if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(productionId) || !await FlagOnAsync())
				return null;
			if (await _disclosures.GetAsync(DepartmentId, UserId, requestId) == null)
				return null;

			var productions = await _disclosures.GetProductionsAsync(DepartmentId, UserId, requestId);
			return productions.FirstOrDefault(p => string.Equals(p.RmsDisclosureProductionId, productionId, StringComparison.Ordinal));
		}

		private async Task<DisclosureRequestResult> WrapAsync(RmsDisclosureRequest request, string status = ResponseHelper.Success)
		{
			var result = new DisclosureRequestResult
			{
				Data = DisclosuresApiMapper.ToRequest(request, await _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords)),
				PageSize = 1,
				Status = status
			};
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, request.RowVersion);
			return result;
		}

		#endregion
	}
}
