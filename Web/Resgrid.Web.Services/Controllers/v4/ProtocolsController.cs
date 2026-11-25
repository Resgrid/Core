using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Models.v4.Protocols;
using System.Collections.Generic;
using Resgrid.Framework;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The options for Protocols in the Resgrid system: the department's protocols, matching them to a call, writing them
	/// (create, update in place, disable) and, from P1, their versions and lifecycle (draft, submit, approve with an L2
	/// signature, publish, review). Every read applies the protocol read rule (R-P4): the older apps' endpoints return only
	/// published protocols in the caller's audience. Attachment bytes leave only through <see cref="GetFile"/>.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ProtocolsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IProtocolsService _protocolsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly ICallsService _callsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IProtocolAccessService _protocolAccess;
		private readonly IProtocolLibraryService _library;
		private readonly ISignatureService _signatures;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _mfaPolicy;

		public ProtocolsController(IProtocolsService protocolsService, IAuthorizationService authorizationService = null, ICallsService callsService = null, IEventAggregator eventAggregator = null,
			IProtocolAccessService protocolAccess = null, IProtocolLibraryService library = null, ISignatureService signatures = null, IMfaEvidenceService evidence = null,
			IMfaPolicyService mfaPolicy = null)
		{
			_protocolsService = protocolsService;
			_authorizationService = authorizationService;
			_callsService = callsService;
			_eventAggregator = eventAggregator;
			_protocolAccess = protocolAccess;
			_library = library;
			_signatures = signatures;
			_evidence = evidence;
			_mfaPolicy = mfaPolicy;
		}
		#endregion Members and Constructors

		private async Task<ProtocolReader> ReaderAsync() =>
			_protocolAccess == null ? ProtocolReader.Nobody(DepartmentId) : await _protocolAccess.GetReaderAsync(DepartmentId, UserId);

		/// <summary>
		/// Gets the department's protocols the caller may read: enabled and published, in the caller's audience (or Dispatch
		/// protocols for dispatchers). Drafts, disabled, retired and deleted protocols are left out. No attachment bytes: a file
		/// comes from <see cref="GetFile"/>.
		/// </summary>
		[HttpGet("GetAllProtocols")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetAllProtocolsResult>> GetAllProtocols()
		{
			var result = new GetAllProtocolsResult();
			result.Data = new List<ProtocolResultData>();

			var reader = await ReaderAsync();
			var protocols = _protocolAccess == null
				? new List<DispatchProtocol>()
				: await _protocolAccess.FilterReadableAsync(reader, await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId, true));
			var versions = await CurrentVersionsAsync(protocols);

			if (protocols != null && protocols.Any())
			{
				foreach (var p in protocols)
				{
					result.Data.Add(ConvertProtocolData(p, versions.TryGetValue(p.DispatchProtocolId, out var v) ? v : null));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets a single protocol by id when the caller may read it. A deleted protocol is still returned inside its department
		/// (calls that cite it keep showing it); a draft or an out-of-audience protocol is a 404 for members.
		/// </summary>
		[HttpGet("GetProtocol")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolResult>> GetProtocol(int protocolId)
		{
			var result = new GetProtocolResult();
			var protocol = await _protocolsService.GetProtocolByIdAsync(protocolId);

			if (protocol == null)
				return NotFound();

			if (protocol.DepartmentId != DepartmentId)
				return Unauthorized();

			if (_protocolAccess == null || !await _protocolAccess.CanReadAsync(await ReaderAsync(), protocol))
				return NotFound();

			var versions = await CurrentVersionsAsync(new List<DispatchProtocol> { protocol });
			result.Data = ConvertProtocolData(protocol, versions.TryGetValue(protocol.DispatchProtocolId, out var v) ? v : null);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			return Ok(result);
		}

		/// <summary>
		/// Gets a protocol attachment by its id: the one endpoint the bytes leave through. Unknown, another department's, or of a
		/// protocol the caller may not read, is a 404.
		/// </summary>
		/// <param name="protocolAttachmentId">ID of the protocol attachment</param>
		[HttpGet("GetFile")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult> GetFile(int protocolAttachmentId)
		{
			var attachment = await _protocolsService.GetAttachmentForDepartmentAsync(DepartmentId, protocolAttachmentId);

			if (attachment == null || attachment.Data == null)
				return NotFound();

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(attachment.DispatchProtocolId);
			if (_protocolAccess == null || !await _protocolAccess.CanReadAsync(await ReaderAsync(), protocol))
				return NotFound();

			return File(attachment.Data, string.IsNullOrWhiteSpace(attachment.FileType) ? "application/octet-stream" : attachment.FileType, attachment.FileName);
		}

		/// <summary>
		/// Every enabled, published Dispatch or Procedure protocol evaluated for a call: the matcher the web call forms use
		/// (D18). Pass the call's priority id and its type (by id, or by name), and optionally the call's logged time in UTC (now
		/// when left out) or an existing call's id, whose logged time is then used and whose protocols are left out.
		/// </summary>
		[HttpGet("GetProtocolsForCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolsForCallResult>> GetProtocolsForCall(int priority, int? callTypeId = null, string type = null, DateTime? loggedOnUtc = null, int? callId = null,
			string nature = null, double? latitude = null, double? longitude = null, int? locationPoiId = null)
		{
			var at = loggedOnUtc.HasValue ? (DateTime?)DateTime.SpecifyKind(loggedOnUtc.Value.Kind == DateTimeKind.Local ? loggedOnUtc.Value.ToUniversalTime() : loggedOnUtc.Value, DateTimeKind.Utc) : null;
			var onCall = new HashSet<int>();
			// Protocols P4 (optional, Omni): the facts the shared trigger engine reads besides priority, type and time.
			var details = new DispatchTriggerCallInput { Nature = nature, Latitude = latitude, Longitude = longitude, LocationPoiId = locationPoiId };

			if (callId.HasValue && callId.Value > 0 && _callsService != null && _authorizationService != null && await _authorizationService.CanUserViewCallAsync(UserId, callId.Value))
			{
				var call = await _callsService.GetCallByIdAsync(callId.Value);
				if (call != null && call.DepartmentId == DepartmentId)
				{
					at = call.LoggedOn;
					onCall = new HashSet<int>((await _callsService.GetCallProtocolsByCallIdAsync(call.CallId) ?? new List<CallProtocol>()).Select(x => x.DispatchProtocolId));
					var stored = DispatchTriggerCallInput.FromCall(call);
					details = new DispatchTriggerCallInput
					{
						Nature = string.IsNullOrWhiteSpace(nature) ? stored.Nature : nature,
						Notes = stored.Notes,
						GeoLocationData = latitude.HasValue && longitude.HasValue ? null : stored.GeoLocationData,
						Latitude = latitude,
						Longitude = longitude,
						LocationPoiId = locationPoiId ?? stored.LocationPoiId,
						CallSource = stored.CallSource
					};
				}
			}

			details.LoggedOnUtc = at;
			var context = await _protocolsService.BuildCallContextWithFactsAsync(DepartmentId, priority, callTypeId, type, details);
			var matches = await _protocolsService.GetProtocolsForCallAsync(DepartmentId, context);

			var result = new GetProtocolsForCallResult();
			foreach (var match in matches.Where(x => !onCall.Contains(x.Protocol.DispatchProtocolId)))
			{
				result.Data.Add(new ProtocolCallMatchData
				{
					ProtocolId = match.Protocol.DispatchProtocolId.ToString(),
					Name = match.Protocol.Name,
					Code = match.Protocol.Code,
					MinimumWeight = match.Protocol.MinimumWeight,
					State = (int)match.State,
					MatchedTriggerId = match.MatchedTrigger?.DispatchProtocolTriggerId.ToString(),
					MatchedTriggerType = match.MatchedTrigger?.Type,
					Questions = ConvertQuestions(match.Protocol)
				});
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Creates a protocol with its triggers, questions and answers. From P1 it is a draft: its text is the first draft's
		/// content, and nothing is in force until a version is approved and published.
		/// </summary>
		[HttpPost("SaveProtocol")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<ActionResult<SaveProtocolResult>> SaveProtocol([FromBody] ProtocolInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			if (_library == null)
				return StatusCode(StatusCodes.Status503ServiceUnavailable);

			var protocol = await BuildAsync(input, null, true);
			var errors = ProtocolRules.Validate(protocol);
			errors.AddRange(await ValidateReferencesAsync(protocol, null));

			if (errors.Count > 0)
				return BadRequest(Refused(errors));

			protocol.DispatchProtocolId = 0;
			protocol.DepartmentId = DepartmentId;
			protocol.CreatedOn = DateTime.UtcNow;
			protocol.CreatedByUserId = UserId;
			protocol.UpdatedOn = DateTime.UtcNow;
			protocol.UpdatedByUserId = UserId;

			var created = await _library.CreateAsync(new ProtocolCreateRequest { DepartmentId = DepartmentId, Protocol = protocol, ContentHtml = input.ProtocolText, UserId = UserId }, cancellationToken);
			if (!created.Succeeded)
				return BadRequest(Refused(new List<string> { created.Error.ToString() }));

			return Ok(Saved(await _protocolsService.GetProtocolByIdAsync(created.Protocol.DispatchProtocolId) ?? created.Protocol));
		}

		/// <summary>
		/// Updates a protocol in place: its triggers, questions and answers are matched by id (changed, added or removed), so ids
		/// stay stable. An id that is not one of this protocol's rows is refused (400). The text goes to the open draft (P1); the
		/// text in force changes only when a version is published.
		/// </summary>
		[HttpPost("UpdateProtocol")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<ActionResult<SaveProtocolResult>> UpdateProtocol([FromBody] ProtocolInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ProtocolId <= 0)
				return BadRequest();

			if (_authorizationService == null || !await _authorizationService.CanUserModifyProtocolAsync(UserId, input.ProtocolId))
				return Unauthorized();

			var current = await _protocolsService.GetProtocolByIdAsync(input.ProtocolId);
			if (current == null || current.DepartmentId != DepartmentId)
				return NotFound();

			var edited = await BuildAsync(input, current, false);
			edited.DispatchProtocolId = current.DispatchProtocolId;
			edited.DepartmentId = DepartmentId;

			var removeIds = (input.RemoveAttachmentIds ?? new List<int>()).Distinct().ToList();
			if (ProtocolRules.ForeignChildIds(current, edited).Count > 0 || removeIds.Any(x => current.Attachments.All(a => a.DispatchProtocolAttachmentId != x)))
				return BadRequest(Refused(new List<string> { "ErrorForeignRows" }));

			var errors = ProtocolRules.Validate(edited);
			errors.AddRange(await ValidateReferencesAsync(edited, current));
			if (errors.Count > 0)
				return BadRequest(Refused(errors));

			var wasDisabled = current.IsDisabled;
			var before = Snapshot(current);
			var saved = await _protocolsService.UpdateProtocolAsync(edited, removeIds, UserId, cancellationToken);
			Audit(AuditLogTypes.ProtocolUpdated, before, saved);
			if (wasDisabled != saved.IsDisabled)
				Audit(saved.IsDisabled ? AuditLogTypes.ProtocolDisabled : AuditLogTypes.ProtocolEnabled, before, saved);

			// The text is the draft's (an older client sending the same text it read changes nothing in force).
			if (_library != null && input.ProtocolText != null && !string.Equals(StringHelpers.SanitizeHtmlForStorage(input.ProtocolText), current.ProtocolText, StringComparison.Ordinal))
			{
				var reader = await ReaderAsync();
				var draft = await _library.GetOpenDraftAsync(DepartmentId, current.DispatchProtocolId);
				if (draft == null || draft.Status == (int)ProtocolVersionStatuses.Draft)
					await _library.SaveDraftAsync(reader, current.DispatchProtocolId, new ProtocolDraftInput
					{
						ContentHtml = input.ProtocolText,
						ChangeSummary = draft?.ChangeSummary,
						RequiresAcknowledgment = draft?.RequiresAcknowledgment ?? ProtocolKindRules.AcknowledgmentByDefault(current.Kind),
						Locale = draft?.Locale,
						ExpectedRowVersion = draft?.RowVersion ?? 0
					}, cancellationToken);
			}

			return Ok(Saved(saved));
		}

		/// <summary>Disables (or enables) a protocol. A disabled protocol is never suggested, listed for the apps or attached.</summary>
		[HttpPost("SetDisabled")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<ActionResult<SaveProtocolResult>> SetDisabled([FromBody] SetProtocolDisabledInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ProtocolId <= 0)
				return BadRequest();

			if (_authorizationService == null || !await _authorizationService.CanUserModifyProtocolAsync(UserId, input.ProtocolId))
				return Unauthorized();

			var before = await _protocolsService.GetProtocolHeaderByIdAsync(input.ProtocolId);
			var beforeSnapshot = Snapshot(before);
			var wasDisabled = before?.IsDisabled ?? false;

			var saved = await _protocolsService.SetProtocolDisabledAsync(input.ProtocolId, input.Disabled, UserId, cancellationToken);
			if (saved == null)
				return NotFound();

			if (wasDisabled != saved.IsDisabled)
				Audit(saved.IsDisabled ? AuditLogTypes.ProtocolDisabled : AuditLogTypes.ProtocolEnabled, beforeSnapshot, saved);

			return Ok(Saved(await _protocolsService.GetProtocolByIdAsync(saved.DispatchProtocolId) ?? saved));
		}

		#region Versions and lifecycle (P1)

		/// <summary>The protocol's versions the caller may read (no content): members see published history, authors and approvers every version.</summary>
		[HttpGet("GetVersions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolVersionsResult>> GetVersions(int protocolId)
		{
			if (_library == null || _protocolAccess == null)
				return NotFound();

			var reader = await ReaderAsync();
			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != DepartmentId || !await _protocolAccess.CanReadAsync(reader, protocol))
				return NotFound();

			var audience = await _library.GetAudienceAsync(DepartmentId, protocolId);
			var result = new GetProtocolVersionsResult();
			foreach (var version in await _library.GetVersionsAsync(DepartmentId, protocolId))
				if (ProtocolAccessRules.CanReadVersion(reader, protocol, audience, version))
					result.Data.Add(Version(version, false));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The call is the caller's to view, is in the department and cites exactly this version.</summary>
		private async Task<bool> IsCitedByViewableCallAsync(int? callId, ProtocolVersion version)
		{
			if (!callId.HasValue || callId.Value <= 0 || version == null || _callsService == null || _authorizationService == null)
				return false;
			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId.Value))
				return false;
			var call = await _callsService.GetCallByIdAsync(callId.Value);
			if (call == null || call.DepartmentId != DepartmentId)
				return false;
			return ProtocolAccessRules.IsCitedByCall(version, await _callsService.GetCallProtocolsByCallIdAsync(call.CallId));
		}

		/// <summary>
		/// One version with its sanitized content and approval steps, when the caller may read it. With <paramref name="callId"/>, a version the
		/// call cites (a legacy version from before versions existed, or an older one) is readable by anyone who may view the call, as the call
		/// page shows it (P1, R-P2); without it, the library's own rules apply.
		/// </summary>
		[HttpGet("GetVersion")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolVersionResult>> GetVersion(int protocolVersionId, int? callId = null)
		{
			if (_library == null || _protocolAccess == null)
				return NotFound();

			var version = await _library.GetVersionAsync(DepartmentId, protocolVersionId);
			var protocol = version == null ? null : await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			var reader = await ReaderAsync();
			if (protocol == null || protocol.DepartmentId != DepartmentId || !reader.IsCurrentMember)
				return NotFound();
			if (!ProtocolAccessRules.CanReadVersion(reader, protocol, await _library.GetAudienceAsync(DepartmentId, protocol.DispatchProtocolId), version) &&
				!await IsCitedByViewableCallAsync(callId, version))
				return NotFound();

			var data = Version(version, true);
			if (reader.IsManager)
				data.Approvals = (await _library.GetApprovalsAsync(DepartmentId, version.ProtocolVersionId)).Select(Approval).ToList();

			var result = new GetProtocolVersionResult { Data = data, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Saves the open draft's content (creating a draft from the version in force when there is none). A save against an
		/// older copy than the stored one is refused with Error "StaleCopy" and who saved it when.
		/// </summary>
		[HttpPost("SaveDraft")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<ActionResult<ProtocolLifecycleActionResult>> SaveDraft([FromBody] SaveProtocolDraftInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ProtocolId <= 0 || _library == null)
				return BadRequest();

			var outcome = await _library.SaveDraftAsync(await ReaderAsync(), input.ProtocolId, new ProtocolDraftInput
			{
				ContentHtml = input.ContentHtml,
				ContentJson = string.IsNullOrWhiteSpace(input.ContentJson) ? null : input.ContentJson,
				ChangeSummary = input.ChangeSummary,
				RequiresAcknowledgment = input.RequiresAcknowledgment,
				Locale = input.Locale,
				ExpectedRowVersion = input.ExpectedRowVersion
			}, cancellationToken);

			return Lifecycle(outcome);
		}

		/// <summary>Submits a draft for review (its approval steps open). Comment: the change summary.</summary>
		[HttpPost("SubmitVersion")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<ActionResult<ProtocolLifecycleActionResult>> SubmitVersion([FromBody] ProtocolVersionActionInput input, CancellationToken cancellationToken)
		{
			if (input == null || _library == null)
				return BadRequest();
			return Lifecycle(await _library.SubmitAsync(await ReaderAsync(), input.ProtocolVersionId, input.Comment, cancellationToken));
		}

		/// <summary>
		/// Approves the caller's step with an L2 signature bound to the version's content hash. The API session must have
		/// completed a step-up within the step-up window (MFA step-up endpoints); without it the answer is Error
		/// "SignatureRequired". Passkey-bound signing is web-only in this release.
		/// </summary>
		[HttpPost("ApproveVersion")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<ActionResult<ProtocolLifecycleActionResult>> ApproveVersion([FromBody] ProtocolVersionActionInput input, CancellationToken cancellationToken)
		{
			if (input == null || _library == null)
				return BadRequest();
			return Lifecycle(await _library.ApproveAsync(await ReaderAsync(), input.ProtocolVersionId, input.Comment, await ProofAsync(cancellationToken), null, cancellationToken));
		}

		/// <summary>Rejects the version back to draft; the comment is required.</summary>
		[HttpPost("RejectVersion")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<ActionResult<ProtocolLifecycleActionResult>> RejectVersion([FromBody] ProtocolVersionActionInput input, CancellationToken cancellationToken)
		{
			if (input == null || _library == null)
				return BadRequest();
			return Lifecycle(await _library.RejectAsync(await ReaderAsync(), input.ProtocolVersionId, input.Comment, cancellationToken));
		}

		/// <summary>Publishes an approved version now, or schedules it for EffectiveOnUtc. The change summary is required.</summary>
		[HttpPost("PublishVersion")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<ActionResult<ProtocolLifecycleActionResult>> PublishVersion([FromBody] PublishProtocolVersionInput input, CancellationToken cancellationToken)
		{
			if (input == null || _library == null)
				return BadRequest();
			return Lifecycle(await _library.PublishAsync(await ReaderAsync(), input.ProtocolVersionId, input.Major, input.ChangeSummary,
				Utc(input.EffectiveOnUtc), Utc(input.ExpiresOnUtc), cancellationToken));
		}

		/// <summary>"Reviewed, no change": an L2-signed stamp on the version in force that resets the next review date.</summary>
		[HttpPost("RecordReview")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<ProtocolLifecycleActionResult>> RecordReview([FromBody] RecordProtocolReviewInput input, CancellationToken cancellationToken)
		{
			if (input == null || _library == null)
				return BadRequest();
			return Lifecycle(await _library.RecordReviewAsync(await ReaderAsync(), input.ProtocolId, await ProofAsync(cancellationToken), null, cancellationToken));
		}

		/// <summary>The versions waiting for approval; CanDecide marks the steps the caller may decide.</summary>
		[HttpGet("GetReviewQueue")]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolReviewQueueResult>> GetReviewQueue()
		{
			var result = new GetProtocolReviewQueueResult();
			if (_library != null)
			{
				foreach (var item in await _library.GetReviewQueueAsync(await ReaderAsync()))
				{
					result.Data.Add(new ProtocolReviewQueueItemData
					{
						ProtocolId = item.Protocol.DispatchProtocolId.ToString(),
						Code = item.Protocol.Code,
						Name = item.Protocol.Name,
						ProtocolVersionId = item.Version.ProtocolVersionId.ToString(),
						SubmittedOn = item.Version.SubmittedOn,
						SubmittedByUserId = item.Version.SubmittedByUserId,
						StepOrder = item.Step?.StepOrder ?? 0,
						StepsTotal = item.StepsTotal,
						CanDecide = item.CanDecide,
						OpenComments = item.OpenComments
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The signatures on a version (approvals and review stamps), each checked against the content as stored now.</summary>
		[HttpGet("GetVersionSignatures")]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolSignaturesResult>> GetVersionSignatures(int protocolVersionId)
		{
			if (_library == null || _signatures == null || _protocolAccess == null)
				return NotFound();

			var version = await _library.GetVersionAsync(DepartmentId, protocolVersionId);
			var protocol = version == null ? null : await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (protocol == null || !ProtocolAccessRules.CanReadVersion(await ReaderAsync(), protocol, await _library.GetAudienceAsync(DepartmentId, protocol.DispatchProtocolId), version))
				return NotFound();

			var result = new GetProtocolSignaturesResult();
			foreach (var kind in new[] { SignatureSubjectKinds.ProtocolVersion, SignatureSubjectKinds.ProtocolReview })
			{
				foreach (var check in await _signatures.GetVerifiedForSubjectAsync(DepartmentId, kind, protocolVersionId.ToString()))
				{
					result.Data.Add(new ProtocolSignatureResultData
					{
						SignatureRecordId = check.Record.SignatureRecordId,
						SignerUserId = check.Record.SignerUserId,
						SignerName = check.Record.SignerNameSnapshot,
						Intent = check.Record.Intent,
						Level = check.Record.Level,
						Method = check.Record.Method,
						StatementText = check.Record.StatementText,
						ArtifactSha256 = check.Record.ArtifactSha256,
						SignedOn = check.Record.SignedOn,
						Verification = (int)check.State
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The API session's step-up proof, from server-side evidence only (never the request body).</summary>
		private Task<SignatureProof> ProofAsync(CancellationToken cancellationToken) =>
			ApiSignatureProof.BuildAsync(HttpContext, UserId, DepartmentId, _evidence, _mfaPolicy, cancellationToken);

		private ActionResult<ProtocolLifecycleActionResult> Lifecycle(Model.Services.ProtocolLifecycleResult outcome)
		{
			var result = new ProtocolLifecycleActionResult
			{
				Data = outcome.Version == null ? null : Version(outcome.Version, false),
				Error = outcome.Succeeded ? null : outcome.Error.ToString(),
				ConflictUserId = outcome.ConflictUserId,
				ConflictOn = outcome.ConflictOn,
				SignatureRecordId = outcome.Signature?.SignatureRecordId,
				PageSize = outcome.Version == null ? 0 : 1,
				Status = outcome.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);

			switch (outcome.Error)
			{
				case ProtocolLifecycleErrors.None:
					return Ok(result);
				case ProtocolLifecycleErrors.NotFound:
					return NotFound(result);
				case ProtocolLifecycleErrors.NotAuthorized:
					return StatusCode(StatusCodes.Status403Forbidden, result);
				case ProtocolLifecycleErrors.StaleCopy:
				case ProtocolLifecycleErrors.AlreadyDecided:
				case ProtocolLifecycleErrors.DraftExists:
					return Conflict(result);
				case ProtocolLifecycleErrors.SignatureRequired:
				case ProtocolLifecycleErrors.PasskeyRequired:
					return StatusCode(StatusCodes.Status403Forbidden, result);
				default:
					return BadRequest(result);
			}
		}

		private static ProtocolVersionResultData Version(ProtocolVersion version, bool withContent) => new ProtocolVersionResultData
		{
			ProtocolVersionId = version.ProtocolVersionId.ToString(),
			ProtocolId = version.DispatchProtocolId.ToString(),
			Label = version.Label,
			Status = version.Status,
			EffectiveOn = version.EffectiveOn,
			ExpiresOn = version.ExpiresOn,
			ContentHash = version.ContentHash,
			// P3: a block version keeps its block anchors and tables (the structured allow-list); P1 html is sanitized as before.
			ContentHtml = withContent ? ProtocolContentDocument.SanitizeForOutput(version.ContentHtml) : null,
			ContentJson = withContent ? version.ContentJson : null,
			ChangeSummary = version.ChangeSummary,
			RequiresAcknowledgment = version.RequiresAcknowledgment,
			Locale = version.Locale,
			CreatedOn = version.CreatedOn,
			SubmittedOn = version.SubmittedOn,
			ApprovedOn = version.ApprovedOn,
			PublishedOn = version.PublishedOn,
			RowVersion = version.RowVersion
		};

		private static ProtocolApprovalResultData Approval(ProtocolVersionApproval a) => new ProtocolApprovalResultData
		{
			Round = a.Round,
			StepOrder = a.StepOrder,
			ApproverUserId = a.ApproverUserId,
			ApproverRoleId = a.ApproverRoleId,
			Decision = a.Decision,
			DecidedByUserId = a.DecidedByUserId,
			DecidedOn = a.DecidedOn,
			Comment = a.Comment,
			SignatureRecordId = a.SignatureRecordId
		};

		/// <summary>The versions in force of the listed protocols (headers), one query.</summary>
		private async Task<Dictionary<int, ProtocolVersion>> CurrentVersionsAsync(IEnumerable<DispatchProtocol> protocols)
		{
			var map = new Dictionary<int, ProtocolVersion>();
			var list = (protocols ?? Enumerable.Empty<DispatchProtocol>()).Where(p => p?.CurrentVersionId != null).ToList();
			if (_library == null || list.Count == 0)
				return map;

			var wanted = list.Select(p => p.CurrentVersionId.Value).ToHashSet();
			foreach (var version in (await _library.GetDepartmentVersionHeadersAsync(DepartmentId)).Where(v => wanted.Contains(v.ProtocolVersionId)))
				map[version.DispatchProtocolId] = version;
			return map;
		}

		#endregion Versions and lifecycle (P1)

		private async Task<DispatchProtocol> BuildAsync(ProtocolInput input, DispatchProtocol current, bool isNew)
		{
			var types = _callsService == null ? new List<CallType>() : await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			return new DispatchProtocol
			{
				Name = input.Name?.Trim(),
				Code = ProtocolRules.NormalizeCode(input.Code),
				Description = input.Description,
				ProtocolText = input.ProtocolText,
				MinimumWeight = input.MinimumWeight,
				IsDisabled = input.IsDisabled,
				Kind = current?.Kind ?? (int)ProtocolKinds.Dispatch,
				Triggers = (input.Triggers ?? new List<ProtocolTriggerInput>()).Where(x => x != null).Select(t =>
				{
					var needsType = t.Type == (int)ProtocolTriggerTypes.CallType || t.Type == (int)ProtocolTriggerTypes.CallPriortyAndType;
					var type = needsType && t.CallTypeId.HasValue ? types.FirstOrDefault(x => x.CallTypeId == t.CallTypeId.Value) : null;
					var existing = current?.Triggers?.FirstOrDefault(x => x.DispatchProtocolTriggerId == t.ProtocolTriggerId);

					return new DispatchProtocolTrigger
					{
						DispatchProtocolTriggerId = isNew ? 0 : t.ProtocolTriggerId,
						Type = t.Type,
						StartsOn = Utc(t.StartsOn),
						EndsOn = Utc(t.EndsOn),
						Priority = t.Type == (int)ProtocolTriggerTypes.CallType ? null : t.Priority,
						CallTypeId = needsType ? t.CallTypeId : null,
						// The type's current name, or the one the trigger kept when its type was deleted since.
						CallType = needsType ? type?.Type ?? (existing?.CallTypeId == t.CallTypeId ? existing?.CallType : null) : null
					};
				}).ToList(),
				Questions = (input.Questions ?? new List<ProtocolQuestionInput>()).Where(x => x != null).Select(q => new DispatchProtocolQuestion
				{
					DispatchProtocolQuestionId = isNew ? 0 : q.ProtocolQuestionId,
					Question = q.Question?.Trim(),
					Answers = (q.Answers ?? new List<ProtocolAnswerInput>()).Where(x => x != null).Select(a => new DispatchProtocolQuestionAnswer
					{
						DispatchProtocolQuestionAnswerId = isNew ? 0 : a.ProtocolQuestionAnswerId,
						Answer = a.Answer?.Trim(),
						Weight = a.Weight
					}).ToList()
				}).ToList()
			};
		}

		/// <summary>The call types and priorities the triggers name must be the department's (or what the trigger already had).</summary>
		private async Task<List<string>> ValidateReferencesAsync(DispatchProtocol protocol, DispatchProtocol current)
		{
			var errors = new List<string>();
			if (_callsService == null)
				return errors;

			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			foreach (var trigger in protocol.Triggers)
			{
				var existing = current?.Triggers?.FirstOrDefault(x => x.DispatchProtocolTriggerId == trigger.DispatchProtocolTriggerId && trigger.DispatchProtocolTriggerId > 0);

				if (trigger.Priority.HasValue && !priorities.Any(x => x.DepartmentCallPriorityId == trigger.Priority.Value && !x.IsDeleted) && existing?.Priority != trigger.Priority)
					errors.Add("ErrorTriggerPriority");

				if (trigger.CallTypeId.HasValue && types.All(x => x.CallTypeId != trigger.CallTypeId.Value) && existing?.CallTypeId != trigger.CallTypeId)
					errors.Add("ErrorTriggerCallType");
			}

			return errors.Distinct().ToList();
		}

		private static DateTime? Utc(DateTime? value) =>
			value.HasValue ? DateTime.SpecifyKind(value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value, DateTimeKind.Utc) : null;

		private static SaveProtocolResult Refused(List<string> errors)
		{
			var result = new SaveProtocolResult { Errors = errors, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private static SaveProtocolResult Saved(DispatchProtocol protocol)
		{
			var result = new SaveProtocolResult { Data = ConvertProtocolData(protocol), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private static string Snapshot(DispatchProtocol protocol) => protocol == null ? null : Newtonsoft.Json.JsonConvert.SerializeObject(new
		{
			protocol.DispatchProtocolId,
			protocol.Name,
			protocol.Code,
			protocol.IsDisabled,
			protocol.MinimumWeight,
			Triggers = protocol.Triggers?.Count,
			Questions = protocol.Questions?.Count
		});

		private void Audit(AuditLogTypes type, string before, DispatchProtocol after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = type,
				Before = before,
				After = Snapshot(after),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request?.Headers["User-Agent"]} {Request?.Headers["Accept-Language"]}"
			});
		}

		private static List<ProtocolTriggerQuestionResult> ConvertQuestions(DispatchProtocol dp)
		{
			return (dp.Questions ?? Enumerable.Empty<DispatchProtocolQuestion>()).OrderBy(x => x.DispatchProtocolQuestionId).Select(q => new ProtocolTriggerQuestionResult
			{
				ProtocolTriggerQuestionId = q.DispatchProtocolQuestionId.ToString(),
				Question = q.Question,
				Answers = (q.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>()).OrderBy(x => x.DispatchProtocolQuestionAnswerId).Select(a => new ProtocolQuestionAnswerResult
				{
					ProtocolQuestionAnswerId = a.DispatchProtocolQuestionAnswerId.ToString(),
					Answer = a.Answer,
					Weight = a.Weight
				}).ToList()
			}).ToList();
		}

		public static ProtocolResultData ConvertProtocolData(DispatchProtocol dp) => ConvertProtocolData(dp, null);

		public static ProtocolResultData ConvertProtocolData(DispatchProtocol dp, ProtocolVersion current)
		{
			var protocol = new ProtocolResultData();
			protocol.ProtocolId = dp.DispatchProtocolId.ToString();
			protocol.DepartmentId = dp.DepartmentId;
			protocol.Name = dp.Name;
			protocol.Code = dp.Code;
			protocol.IsDisabled = dp.IsDisabled;
			protocol.Description = dp.Description;
			// The text in force (the published version's); a protocol never published has none.
			protocol.ProtocolText = dp.ProtocolText;
			protocol.CreatedOn = dp.CreatedOn;
			protocol.CreatedByUserId = dp.CreatedByUserId;
			protocol.UpdatedOn = dp.UpdatedOn;
			protocol.MinimumWeight = dp.MinimumWeight;
			protocol.UpdatedByUserId = dp.UpdatedByUserId;
			protocol.State = (int)dp.State;
			protocol.Kind = dp.Kind;
			protocol.Status = dp.Status;
			protocol.CurrentVersionId = dp.CurrentVersionId?.ToString();
			protocol.VersionLabel = current?.Label;
			protocol.EffectiveOn = current?.EffectiveOn;
			protocol.RequiresAcknowledgment = current?.RequiresAcknowledgment ?? false;
			protocol.Number = dp.Number;
			protocol.Tags = dp.Tags;
			protocol.CategoryId = dp.CategoryId;
			protocol.OwnerUserId = dp.OwnerUserId;
			protocol.NextReviewOn = dp.NextReviewOn;
			protocol.ExcludeFromAi = dp.ExcludeFromAi;
			protocol.Triggers = new List<ProtocolTriggerResult>();
			protocol.Attachments = new List<ProtocolTriggerAttachmentResult>();
			protocol.Questions = ConvertQuestions(dp);

			foreach (var t in dp.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>())
			{
				var trigger = new ProtocolTriggerResult();
				trigger.ProtocolTriggerId = t.DispatchProtocolTriggerId.ToString();
				trigger.Type = t.Type;
				trigger.StartsOn = t.StartsOn;
				trigger.EndsOn = t.EndsOn;
				trigger.Priority = t.Priority;
				trigger.CallType = t.CallType;
				trigger.CallTypeId = t.CallTypeId;
				trigger.Geofence = t.Geofence;

				protocol.Triggers.Add(trigger);
			}

			foreach (var a in dp.Attachments ?? Enumerable.Empty<DispatchProtocolAttachment>())
			{
				var attachment = new ProtocolTriggerAttachmentResult();
				attachment.ProtocolTriggerAttachmentId = a.DispatchProtocolAttachmentId.ToString();
				attachment.FileName = a.FileName;
				attachment.FileType = a.FileType;
				attachment.Size = a.Size;

				protocol.Attachments.Add(attachment);
			}

			return protocol;
		}
	}
}
