using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.Records;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Records (RMS) client contract, RMS-1B (plan sections 5.3, 5.4, 5.9.1): capability manifest with the locked
	/// definition catalog and the Protected Data block, authorization-filtered list/search, a delta cursor with
	/// tombstones, ETag-guarded draft saves that return the changed field paths on conflict, scoped idempotency on
	/// creates and commands, revision history and diffs, and resumable checksummed attachment uploads. Every action
	/// gates on the Records.System flag first; every read passes the per-record visibility rule.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RecordsController : V4AuthenticatedApiControllerbase
	{
		public const string ConflictStatus = "conflict";
		public const int MaxPageSize = 200;

		private readonly IRecordsService _recordsService;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IRecordsSearchService _recordsSearch;
		private readonly IRecordAttachmentUploadService _uploads;
		private readonly IRecordsApiIdempotencyService _idempotency;

		public RecordsController(IRecordsService recordsService, IRecordsCutoverService cutoverService, IRecordsAuthorizationService recordsAuthorizationService,
			IFeatureToggleService featureToggleService, IDepartmentSettingsService departmentSettingsService, IDepartmentDataProtectionService dataProtectionService,
			IRecordsSearchService recordsSearch, IRecordAttachmentUploadService uploads, IRecordsApiIdempotencyService idempotency)
		{
			_recordsService = recordsService;
			_cutoverService = cutoverService;
			_recordsAuthorizationService = recordsAuthorizationService;
			_featureToggleService = featureToggleService;
			_departmentSettingsService = departmentSettingsService;
			_dataProtectionService = dataProtectionService;
			_recordsSearch = recordsSearch;
			_uploads = uploads;
			_idempotency = idempotency;
		}

		#region Capabilities

		/// <summary>
		/// The Records capability manifest: module/cutover state, the caller's effective Record permissions, per-app
		/// Field Records flags, the published (locked) definition catalog with field applicability and minimum client
		/// capability, search availability, upload limits, and the Protected Data block (plan 5.9.1).
		/// </summary>
		[HttpGet("Capabilities")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordsCapabilitiesResult>> Capabilities()
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var data = new RecordsCapabilitiesData
			{
				ContractVersion = RecordsApiContract.Version,
				ModuleEnabled = moduleState.FlagEnabled,
				RecordsUsable = moduleState.RecordsUsable,
				Activated = moduleState.Activated,
				ActivatedOn = moduleState.ActivatedOn,
				CutoverState = moduleState.CutoverState?.ToString(),
				Permissions = new RecordsPermissionsData
				{
					CanView = ClaimsAuthorizationHelper.CanViewRecords(),
					CanCreate = ClaimsAuthorizationHelper.CanCreateRecord(),
					CanReview = ClaimsAuthorizationHelper.CanReviewRecords(),
					CanApprove = ClaimsAuthorizationHelper.CanApproveRecords(),
					CanFinalize = ClaimsAuthorizationHelper.CanFinalizeRecords(),
					CanSubmit = ClaimsAuthorizationHelper.CanSubmitRecords(),
					CanAmend = ClaimsAuthorizationHelper.CanAmendRecords(),
					CanVoid = ClaimsAuthorizationHelper.CanVoidRecords(),
					CanExport = ClaimsAuthorizationHelper.CanExportRecords(),
					CanShare = ClaimsAuthorizationHelper.CanShareRecords(),
					CanReassign = ClaimsAuthorizationHelper.CanReassignRecordDrafts(),
					CanViewRestricted = ClaimsAuthorizationHelper.CanViewRestrictedRecords(),
					CanViewLegacy = ClaimsAuthorizationHelper.CanViewLegacyRecords(),
					IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
				},
				FieldClients = new RecordsFieldClientsData
				{
					Responder = moduleState.RecordsUsable && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsFieldResponder, DepartmentId),
					Unit = moduleState.RecordsUsable && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsFieldUnit, DepartmentId),
					IncidentCommand = moduleState.RecordsUsable && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsFieldIncidentCommand, DepartmentId),
					Dispatch = moduleState.RecordsUsable && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsFieldDispatch, DepartmentId)
				},
				Definitions = RecordsApiMapper.ToDefinitions(),
				Search = new RecordsSearchCapabilityData { Available = _recordsSearch.IsAvailable, NarrativeAvailable = await NarrativeSearchAvailableAsync() },
				Protection = await ProtectionAsync(),
				UploadChunkSize = _uploads.ChunkSize,
				MaxAttachmentBytes = RecordAttachmentHygiene.MaxBytes,
				ServerTimestampMs = RecordsApiHelper.ToUnixMs(DateTime.UtcNow)
			};

			try
			{
				data.GroupVisibilityMode = (await _departmentSettingsService.GetRecordsGroupVisibilityModeAsync(DepartmentId)).ToString();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			var result = new RecordsCapabilitiesResult { Data = data, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Plan 5.9.1: the same shape before and after Protected Data enrollment; NotInstalled while the subsystem is absent.</summary>
		private async Task<RecordsProtectionData> ProtectionAsync()
		{
			var protection = new RecordsProtectionData();
			try
			{
				var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId);
				if (policy == null)
					return protection;
				protection.State = ((DepartmentDataProtectionState)policy.State).ToString();
				protection.CatalogVersion = policy.CatalogVersion;
				protection.StepUpWindowMinutes = policy.StepUpWindowMinutes;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Records capability protection block fell back to NotInstalled.");
			}
			return protection;
		}

		private async Task<bool> NarrativeSearchAvailableAsync()
		{
			try
			{
				if (!_recordsSearch.IsAvailable || await _dataProtectionService.IsProtectionEnforcedAsync(DepartmentId))
					return false;
				var config = await _departmentSettingsService.GetRecordsSearchConfigAsync(DepartmentId);
				return config != null && config.IndexNarrative;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		#endregion

		#region Lists / delta

		/// <summary>The authorization-filtered record queue (safe projection columns only).</summary>
		[HttpGet("GetRecords")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordsResult>> GetRecords(int? year, string definitionKey, int? state, int? callId, string owner, int? group, int skip = 0, int take = 50)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var result = new RecordsResult();
			if (!moduleState.RecordsUsable)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}

			var query = new RmsRecordQuery
			{
				Year = year,
				DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey,
				States = state.HasValue ? new List<int> { state.Value } : null,
				CallId = callId,
				OwnerUserId = string.IsNullOrWhiteSpace(owner) ? null : owner,
				StationGroupId = group,
				VisibleGroupIds = await _recordsAuthorizationService.GetVisibleGroupIdsAsync(UserId, DepartmentId),
				ViewerUserId = UserId,
				Skip = Math.Max(0, skip),
				Take = Math.Max(1, Math.Min(MaxPageSize, take))
			};

			result.Data = (await _recordsService.QueryAsync(DepartmentId, query)).Select(RecordsApiMapper.ToSummary).ToList();
			result.Total = await _recordsService.CountAsync(DepartmentId, query);
			result.Page = query.Skip / query.Take;
			result.PageSize = result.Data.Count;
			result.Status = result.Data.Count > 0 ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Free-text search over the records index (plan 5.10). Hits are re-checked against per-record visibility;
		/// when the host is off or unavailable the filtered queue is returned with SearchDegraded set and the text is not applied.
		/// </summary>
		[HttpGet("Search")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordsResult>> Search(string text, int? year, string definitionKey, int? state, int skip = 0, int take = 50)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();
			if (string.IsNullOrWhiteSpace(text))
				return BadRequest();

			var result = new RecordsResult();
			if (!moduleState.RecordsUsable)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}

			var visible = await _recordsAuthorizationService.GetVisibleGroupIdsAsync(UserId, DepartmentId);
			var states = state.HasValue ? new List<int> { state.Value } : null;
			take = Math.Max(1, Math.Min(MaxPageSize, take));
			skip = Math.Max(0, skip);

			RecordsSearchResult search = null;
			if (_recordsSearch.IsAvailable)
			{
				try
				{
					search = await _recordsSearch.SearchAsync(DepartmentId, new RecordsSearchRequest
					{
						Text = text.Trim(), VisibleGroupIds = visible, ViewerUserId = UserId, States = states, DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey, Year = year, Skip = skip, Take = take
					});
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Records API search failed; returning the filtered queue.");
				}
			}

			if (search == null || !search.Available)
			{
				// Never quietly reimplemented as LIKE (plan 5.10): the queue renders and the caller is told.
				result.SearchDegraded = true;
				var query = new RmsRecordQuery { Year = year, DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey, States = states, VisibleGroupIds = visible, ViewerUserId = UserId, Skip = skip, Take = take };
				result.Data = (await _recordsService.QueryAsync(DepartmentId, query)).Select(RecordsApiMapper.ToSummary).ToList();
				result.Total = await _recordsService.CountAsync(DepartmentId, query);
			}
			else
			{
				var recordSource = ((int)RmsSearchSourceType.Record).ToString();
				var ids = search.Hits.Where(h => h.SourceType == recordSource && !string.IsNullOrWhiteSpace(h.SourceId)).Select(h => h.SourceId).Distinct().ToList();
				var loaded = (await _recordsService.GetProjectionsByIdsAsync(DepartmentId, ids)).ToDictionary(p => p.RmsRecordSearchProjectionId, StringComparer.OrdinalIgnoreCase);
				var dropped = 0;
				foreach (var id in ids)
				{
					if (!loaded.TryGetValue(id, out var projection) || !await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, id, DepartmentId))
					{
						dropped++;
						continue;
					}
					result.Data.Add(RecordsApiMapper.ToSummary(projection));
				}
				// Totals never disclose a record the caller cannot open.
				result.Total = dropped == 0 ? search.Total : skip + result.Data.Count;
				result.Truncated = search.Truncated;
			}

			result.Page = skip / take;
			result.PageSize = result.Data.Count;
			result.Status = result.Data.Count > 0 ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Delta cursor (plan 5.3): records modified after <paramref name="since"/> (Unix epoch ms; 0 = full pull),
		/// oldest first, with tombstones for cancelled, voided and deleted records so clients remove them locally.
		/// Persist Data.ServerTimestampMs and pass it back as the next since; loop while HasMore.
		/// </summary>
		[HttpGet("Changes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordsChangesResult>> Changes(long since = 0, int take = 200)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			take = Math.Max(1, Math.Min(500, take));
			var result = new RecordsChangesResult { Data = new RecordsChangesData { Since = since } };
			var now = DateTime.UtcNow;

			if (moduleState.RecordsUsable)
			{
				var visible = await _recordsAuthorizationService.GetVisibleGroupIdsAsync(UserId, DepartmentId);
				var rows = await _recordsService.GetChangesSinceAsync(DepartmentId, RecordsApiHelper.FromUnixMs(since), take + 1);
				var hasMore = rows.Count > take;
				var page = rows.Take(take).ToList();
				foreach (var projection in page)
				{
					// Tombstones ride through regardless of scope (the client may hold the row); live rows pass the visibility rule.
					var summary = RecordsApiMapper.ToSummary(projection);
					if (!summary.IsTombstone && visible != null && !await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, projection.RmsRecordSearchProjectionId, DepartmentId))
						continue;
					result.Data.Records.Add(summary);
				}
				result.Data.HasMore = hasMore;
				result.Data.ServerTimestampMs = hasMore && page.Count > 0 ? RecordsApiHelper.ToUnixMs(page[page.Count - 1].ModifiedOn) : RecordsApiHelper.ToUnixMs(now);
			}
			else
			{
				result.Data.ServerTimestampMs = RecordsApiHelper.ToUnixMs(now);
			}

			result.PageSize = result.Data.Records.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		#endregion

		#region Reads

		/// <summary>One record with its working details, participants, units, attachment metadata and revision summaries. Sets a weak ETag.</summary>
		[HttpGet("GetRecord")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordResult>> GetRecord(string id)
		{
			if (!await FlagOnAsync())
				return NotFound();

			var aggregate = await LoadAuthorizedAsync(id, true);
			if (aggregate == null)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, null, IpAddressHelper.GetRequestIP(Request, true), RmsOriginClient.Api);
			return Ok(Wrap(aggregate));
		}

		[HttpGet("GetRevisions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordRevisionsResult>> GetRevisions(string id)
		{
			if (!await FlagOnAsync())
				return NotFound();
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			var result = new RecordRevisionsResult { Data = (await _recordsService.GetRevisionsAsync(DepartmentId, id)).OrderByDescending(r => r.RevisionNumber).Select(RecordsApiMapper.ToRevision).ToList() };
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>A revision rendered from its own snapshot; restricted fields withheld without RecordRestricted_View.</summary>
		[HttpGet("GetRevision")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordRevisionSnapshotResult>> GetRevision(string id, string revisionId)
		{
			if (!await FlagOnAsync())
				return NotFound();
			var aggregate = await LoadAuthorizedAsync(id, true);
			if (aggregate == null)
				return NotFound();

			var revision = aggregate.Revisions.FirstOrDefault(r => string.Equals(r.RmsRevisionId, revisionId, StringComparison.Ordinal));
			if (revision == null)
				return NotFound();

			var snapshot = await _recordsService.GetRevisionSnapshotAsync(DepartmentId, revisionId);
			if (snapshot == null)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, revisionId, RmsAccessAuditAction.Read, "Revision " + revision.RevisionNumber, IpAddressHelper.GetRequestIP(Request, true), RmsOriginClient.Api);
			var result = new RecordRevisionSnapshotResult
			{
				Data = new RecordRevisionSnapshotData { Revision = RecordsApiMapper.ToRevision(revision), Snapshot = RecordsApiMapper.ToRecord(snapshot, ClaimsAuthorizationHelper.CanViewRestrictedRecords()) },
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>On-demand field-level diff between two revisions of the same record (plan 4.8).</summary>
		[HttpGet("Diff")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDiffResult>> Diff(string id, string from, string to)
		{
			if (!await FlagOnAsync())
				return NotFound();
			var aggregate = await LoadAuthorizedAsync(id, true);
			if (aggregate == null)
				return NotFound();
			if (!aggregate.Revisions.Any(r => r.RmsRevisionId == from) || !aggregate.Revisions.Any(r => r.RmsRevisionId == to))
				return NotFound();

			var diffs = await _recordsService.DiffRevisionsAsync(DepartmentId, from, to, ClaimsAuthorizationHelper.CanViewRestrictedRecords());
			var result = new RecordDiffResult
			{
				Data = new RecordDiffData { RecordId = id, FromRevisionId = from, ToRevisionId = to, Diffs = diffs.Select(d => new RecordFieldDiffData { Section = d.Section, FieldKey = d.FieldKey, OldValue = d.OldValue, NewValue = d.NewValue, Withheld = d.Withheld }).ToList() },
				Status = ResponseHelper.Success
			};
			result.PageSize = result.Data.Diffs.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		#endregion

		#region Drafts

		/// <summary>
		/// Creates a draft. IdempotencyKey (body or Idempotency-Key header) makes the create replayable: the same key
		/// returns the existing record with 200 instead of a duplicate. Field clients need their Records.Field.* flag.
		/// </summary>
		[HttpPost("CreateDraft")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordResult>> CreateDraft(SaveRecordDraftInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid)
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var origin = RecordsApiHelper.ResolveOrigin(input.OriginClient);
			var gate = await FieldClientGateAsync(origin);
			if (gate != null)
				return gate;

			input.IdempotencyKey = RecordsApiHelper.ResolveIdempotencyKey(input.IdempotencyKey, Request);
			try
			{
				var aggregate = await _recordsService.CreateDraftAsync(DepartmentId, UserId, RecordsApiMapper.ToDraftInput(input, origin), cancellationToken);
				var replayed = !string.IsNullOrWhiteSpace(input.IdempotencyKey) && aggregate.Record.RowVersion > 1;
				var result = Wrap(aggregate, replayed ? ResponseHelper.Success : ResponseHelper.Created);
				return replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordsLegacyWriteBlockedException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
		}

		/// <summary>
		/// ETag-guarded draft save. RowVersion (or If-Match) must equal the current row version; otherwise 409 with
		/// the current record and the field paths the client's copy would have changed, for deliberate reconciliation.
		/// </summary>
		[HttpPost("SaveDraft")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordResult>> SaveDraft(SaveRecordDraftInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RecordId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var rowVersion = RecordsApiHelper.ResolveRowVersion(input.RowVersion, Request);
			if (!rowVersion.HasValue)
				return Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "RowVersion or If-Match is required for a draft save.", type: "precondition_required");

			var origin = RecordsApiHelper.ResolveOrigin(input.OriginClient);
			var gate = await FieldClientGateAsync(origin);
			if (gate != null)
				return gate;

			var current = await LoadAuthorizedAsync(input.RecordId);
			if (current == null)
				return NotFound();
			if (!CanEditRecord(current.Record))
				return Forbid();

			var draft = RecordsApiMapper.ToDraftInput(input, origin);
			draft.DefinitionKey = draft.DefinitionKey ?? current.Record.DefinitionKey;
			try
			{
				var saved = await _recordsService.SaveDraftAsync(DepartmentId, UserId, input.RecordId, rowVersion.Value, draft, cancellationToken);
				return Ok(Wrap(saved, ResponseHelper.Updated));
			}
			catch (RecordConcurrencyException ex)
			{
				return await ConflictAsync(input.RecordId, ex.ExpectedRowVersion, draft);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordsLegacyWriteBlockedException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
		}

		#endregion

		#region Lifecycle commands

		[HttpPost("SubmitForReview")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public Task<ActionResult<RecordResult>> SubmitForReview(RecordCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, true, rowVersion => _recordsService.SubmitForReviewAsync(DepartmentId, UserId, input.RecordId, rowVersion, cancellationToken));
		}

		[HttpPost("ReturnForCorrection")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public Task<ActionResult<RecordResult>> ReturnForCorrection(RecordCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _recordsService.ReturnForCorrectionAsync(DepartmentId, UserId, input.RecordId, input.ReasonCode, input.ReasonText, cancellationToken));
		}

		[HttpPost("Approve")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Approve)]
		public Task<ActionResult<RecordResult>> Approve(RecordCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _recordsService.ApproveAsync(DepartmentId, UserId, input.RecordId, cancellationToken));
		}

		/// <summary>Finalize: validates, writes the immutable revision and attestation, emits the lifecycle events. Attested must be true. Online-only by design.</summary>
		[HttpPost("Finalize")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Finalize)]
		public async Task<ActionResult<RecordResult>> Finalize(RecordCommandInput input, CancellationToken cancellationToken)
		{
			if (input != null && !input.Attested)
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The attestation statement must be accepted to finalize.", type: "attestation_required");
			return await CommandAsync(input, true, rowVersion => _recordsService.FinalizeAsync(DepartmentId, UserId, input.RecordId, rowVersion, string.IsNullOrWhiteSpace(input.AttestationStatementVersion) ? "1" : input.AttestationStatementVersion, input.ReasonCode, input.ReasonText, cancellationToken));
		}

		[HttpPost("OpenAmendment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Amend)]
		public Task<ActionResult<RecordResult>> OpenAmendment(RecordCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _recordsService.OpenAmendmentAsync(DepartmentId, UserId, input.RecordId, cancellationToken));
		}

		[HttpPost("AbandonAmendment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Amend)]
		public Task<ActionResult<RecordResult>> AbandonAmendment(RecordCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _recordsService.AbandonAmendmentAsync(DepartmentId, UserId, input.RecordId, cancellationToken));
		}

		[HttpPost("Void")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public Task<ActionResult<RecordResult>> Void(RecordCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _recordsService.VoidAsync(DepartmentId, UserId, input.RecordId, input.ReasonCode, input.ReasonText, cancellationToken));
		}

		[HttpPost("Cancel")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public Task<ActionResult<RecordResult>> Cancel(RecordCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _recordsService.CancelAsync(DepartmentId, UserId, input.RecordId, cancellationToken));
		}

		[HttpPost("Reassign")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Reassign)]
		public Task<ActionResult<RecordResult>> Reassign(RecordCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _recordsService.ReassignDraftAsync(DepartmentId, UserId, input.RecordId, input.NewOwnerUserId, input.ReasonText, cancellationToken));
		}

		/// <summary>
		/// Shared command path: flag/usable gate, field-client gate, per-record visibility, scoped idempotency replay,
		/// ETag check where the transition takes one, and the 409/400 mapping. Never last-writer-wins.
		/// </summary>
		private async Task<ActionResult<RecordResult>> CommandAsync(RecordCommandInput input, bool requiresRowVersion, Func<long, Task<RecordAggregate>> action)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RecordId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var origin = RecordsApiHelper.ResolveOrigin(input.OriginClient);
			var gate = await FieldClientGateAsync(origin);
			if (gate != null)
				return gate;

			var current = await LoadAuthorizedAsync(input.RecordId);
			if (current == null)
				return NotFound();

			var key = RecordsApiHelper.ResolveIdempotencyKey(input.IdempotencyKey, Request);
			if (key != null)
			{
				var replayed = await _idempotency.TryGetRecordIdAsync(DepartmentId, UserId, key);
				if (string.Equals(replayed, input.RecordId, StringComparison.Ordinal))
					return Ok(Wrap(await _recordsService.GetAsync(DepartmentId, input.RecordId, true), ResponseHelper.Success));
			}

			long rowVersion = current.Record.RowVersion;
			if (requiresRowVersion)
			{
				var supplied = RecordsApiHelper.ResolveRowVersion(input.RowVersion, Request);
				if (!supplied.HasValue)
					return Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "RowVersion or If-Match is required for this command.", type: "precondition_required");
				rowVersion = supplied.Value;
			}

			try
			{
				var aggregate = await action(rowVersion);
				if (key != null)
					await _idempotency.RememberAsync(DepartmentId, UserId, key, input.RecordId);
				return Ok(Wrap(aggregate, ResponseHelper.Updated));
			}
			catch (RecordConcurrencyException ex)
			{
				return await ConflictAsync(input.RecordId, ex.ExpectedRowVersion, null);
			}
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordsLegacyWriteBlockedException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		#endregion

		#region Attachments

		[HttpGet("GetAttachments")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordAttachmentsResult>> GetAttachments(string id)
		{
			if (!await FlagOnAsync())
				return NotFound();
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();

			var result = new RecordAttachmentsResult { Data = aggregate.Attachments.Where(a => a.DeletedOn == null).Select(RecordsApiMapper.ToAttachment).ToList() };
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Attachment content (base64), department- and record-bound, audited as a read.</summary>
		[HttpGet("GetAttachment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordAttachmentContentResult>> GetAttachment(string id, string attachmentId)
		{
			if (!await FlagOnAsync())
				return NotFound();
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			var attachment = await _recordsService.GetAttachmentAsync(DepartmentId, attachmentId);
			if (attachment == null || !string.Equals(attachment.RecordId, id, StringComparison.Ordinal) || attachment.Data == null || attachment.DeletedOn.HasValue)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, "Attachment " + attachmentId, IpAddressHelper.GetRequestIP(Request, true), RmsOriginClient.Api);
			var meta = RecordsApiMapper.ToAttachment(attachment);
			var result = new RecordAttachmentContentResult
			{
				Data = new RecordAttachmentContentData
				{
					AttachmentId = meta.AttachmentId, RecordId = meta.RecordId, FileName = meta.FileName, ContentType = meta.ContentType, ByteSize = meta.ByteSize, Checksum = meta.Checksum, Description = meta.Description,
					UploadedByUserId = meta.UploadedByUserId, UploadedOn = meta.UploadedOn, ScanState = meta.ScanState, ScanStateName = meta.ScanStateName, Data = Convert.ToBase64String(attachment.Data)
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Opens a resumable upload session (plan 5.3): declare size and SHA-256; send chunks with UploadChunk; finish with CompleteUpload.</summary>
		[HttpPost("BeginUpload")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordUploadResult>> BeginUpload(BeginRecordUploadInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RecordId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var current = await LoadAuthorizedAsync(input.RecordId);
			if (current == null)
				return NotFound();
			if (!CanEditRecord(current.Record))
				return Forbid();

			try
			{
				var session = await _uploads.BeginAsync(DepartmentId, UserId, input.RecordId, input.FileName, input.ContentType, input.ByteSize, input.Sha256);
				return StatusCode(StatusCodes.Status201Created, WrapUpload(session, ResponseHelper.Created));
			}
			catch (RecordUploadSessionException ex)
			{
				return UploadProblem(ex);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		/// <summary>One chunk at the session's next offset (base64, at most ChunkSize bytes). Returns ReceivedBytes to resume from.</summary>
		[HttpPost("UploadChunk")]
		[Consumes(MediaTypeNames.Application.Json)]
		[RequestSizeLimit(4 * 1024 * 1024)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordUploadResult>> UploadChunk(RecordUploadChunkInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.UploadId) || string.IsNullOrWhiteSpace(input.Data))
				return BadRequest();
			if (!await FlagOnAsync())
				return NotFound();

			byte[] bytes;
			try { bytes = Convert.FromBase64String(input.Data); }
			catch (FormatException) { return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Chunk data must be base64.", type: "record_validation"); }

			try
			{
				var session = await _uploads.AppendAsync(DepartmentId, UserId, input.UploadId, input.Offset, bytes);
				return Ok(WrapUpload(session, ResponseHelper.Updated));
			}
			catch (RecordUploadSessionException ex)
			{
				return UploadProblem(ex);
			}
		}

		/// <summary>Session status, for resuming after a dropped connection.</summary>
		[HttpGet("GetUpload")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordUploadResult>> GetUpload(string uploadId)
		{
			if (!await FlagOnAsync())
				return NotFound();
			var session = await _uploads.GetAsync(DepartmentId, UserId, uploadId);
			if (session == null)
				return NotFound();
			return Ok(WrapUpload(session, ResponseHelper.Success));
		}

		/// <summary>Verifies size and SHA-256, then stores the attachment through hygiene and the scanner. 422 on checksum mismatch or rejection.</summary>
		[HttpPost("CompleteUpload")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordAttachmentResult>> CompleteUpload(CompleteRecordUploadInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.UploadId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			try
			{
				var attachment = await _uploads.CompleteAsync(DepartmentId, UserId, input.UploadId, input.Description, cancellationToken);
				var result = new RecordAttachmentResult { Data = RecordsApiMapper.ToAttachment(attachment), PageSize = 1, Status = ResponseHelper.Created };
				ResponseHelper.PopulateV4ResponseData(result);
				return StatusCode(StatusCodes.Status201Created, result);
			}
			catch (RecordUploadSessionException ex)
			{
				return UploadProblem(ex);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		[HttpPost("AbortUpload")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordUploadResult>> AbortUpload(RecordUploadIdInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.UploadId))
				return BadRequest();
			if (!await FlagOnAsync())
				return NotFound();
			if (!await _uploads.AbortAsync(DepartmentId, UserId, input.UploadId))
				return NotFound();
			var session = await _uploads.GetAsync(DepartmentId, UserId, input.UploadId);
			return Ok(WrapUpload(session, ResponseHelper.Deleted));
		}

		[HttpPost("RemoveAttachment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordResult>> RemoveAttachment(RecordAttachmentIdInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RecordId) || string.IsNullOrWhiteSpace(input.AttachmentId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var current = await LoadAuthorizedAsync(input.RecordId);
			if (current == null)
				return NotFound();
			if (!CanEditRecord(current.Record))
				return Forbid();

			try
			{
				if (!await _recordsService.RemoveAttachmentAsync(DepartmentId, UserId, input.RecordId, input.AttachmentId, cancellationToken))
					return NotFound();
				return Ok(Wrap(await _recordsService.GetAsync(DepartmentId, input.RecordId, true), ResponseHelper.Deleted));
			}
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
		}

		private ActionResult UploadProblem(RecordUploadSessionException ex)
		{
			int status;
			switch (ex.Code)
			{
				case "not_found": status = StatusCodes.Status404NotFound; break;
				case "too_large": status = StatusCodes.Status413PayloadTooLarge; break;
				case "checksum_mismatch":
				case "rejected": status = StatusCodes.Status422UnprocessableEntity; break;
				default: status = StatusCodes.Status409Conflict; break;
			}
			return Problem(statusCode: status, title: ex.Message, type: "upload_" + ex.Code);
		}

		private RecordUploadResult WrapUpload(RecordAttachmentUploadSession session, string status)
		{
			var result = new RecordUploadResult { Data = session == null ? null : RecordsApiMapper.ToUpload(session), PageSize = session == null ? 0 : 1, Status = status };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion

		#region Helpers

		private async Task<bool> FlagOnAsync()
		{
			return (await _cutoverService.GetModuleStateAsync(DepartmentId)).FlagEnabled;
		}

		/// <summary>Writes need the module usable: flag on and the department activated.</summary>
		private async Task<ActionResult> UsableAsync()
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();
			if (!moduleState.RecordsUsable)
				return Problem(statusCode: StatusCodes.Status409Conflict, title: "Records is not activated for this department.", type: "records_not_activated");
			return null;
		}

		/// <summary>A field client below its flag fails closed for authoring (plan 4.1); non-field origins pass.</summary>
		private async Task<ActionResult> FieldClientGateAsync(RmsOriginClient origin)
		{
			var flag = RecordsApiHelper.FieldFlagFor(origin);
			if (flag == null)
				return null;
			if (await _featureToggleService.IsEnabledAsync(flag, DepartmentId))
				return null;
			return Problem(statusCode: StatusCodes.Status403Forbidden, title: $"Field Records are not enabled for the {origin} app in this department.", type: "field_records_disabled");
		}

		/// <summary>Loads a record only when the caller passes the per-record visibility rule; a refusal is audited as Denied.</summary>
		private async Task<RecordAggregate> LoadAuthorizedAsync(string id, bool includeRevisions = false)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			if (!await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, id, DepartmentId))
			{
				await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Denied, null, IpAddressHelper.GetRequestIP(Request, true), RmsOriginClient.Api);
				return null;
			}

			return await _recordsService.GetAsync(DepartmentId, id, includeRevisions);
		}

		private bool CanEditRecord(RmsOperationalRecord record)
		{
			if (!ClaimsAuthorizationHelper.CanCreateRecord())
				return false;

			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
				|| string.Equals(record.OwnerUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(record.AuthorUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| (record.AmendsRevisionId != null && ClaimsAuthorizationHelper.CanAmendRecords());
		}

		private async Task<ActionResult<RecordResult>> ConflictAsync(string recordId, long expectedRowVersion, RecordDraftInput attempted)
		{
			var current = await _recordsService.GetAsync(DepartmentId, recordId, true);
			if (current == null)
				return NotFound();

			var conflict = RecordDraftConflictResolver.Describe(attempted, current, expectedRowVersion);
			var result = new RecordConflictResult { Data = RecordsApiMapper.ToConflict(conflict, current, ClaimsAuthorizationHelper.CanViewRestrictedRecords()), PageSize = 1, Status = ConflictStatus };
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, current.Record.RowVersion);
			return Conflict(result);
		}

		private RecordResult Wrap(RecordAggregate aggregate, string status = ResponseHelper.Success)
		{
			var result = new RecordResult { Data = RecordsApiMapper.ToRecord(aggregate, ClaimsAuthorizationHelper.CanViewRestrictedRecords()), PageSize = 1, Status = status };
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, aggregate.Record.RowVersion);
			return result;
		}

		#endregion
	}
}
