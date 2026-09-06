using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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
	public class RecordsController : V4AuthenticatedApiControllerbase, IActionFilter
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
		private readonly IRecordsDashboardService _dashboard;

		private SystemPrincipalRecordGrant _systemGrant;
		private bool _systemGrantResolved;

		public RecordsController(IRecordsService recordsService, IRecordsCutoverService cutoverService, IRecordsAuthorizationService recordsAuthorizationService,
			IFeatureToggleService featureToggleService, IDepartmentSettingsService departmentSettingsService, IDepartmentDataProtectionService dataProtectionService,
			IRecordsSearchService recordsSearch, IRecordAttachmentUploadService uploads, IRecordsApiIdempotencyService idempotency, IRecordsDashboardService dashboard)
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
			_dashboard = dashboard;
		}

		/// <summary>
		/// Every action on this controller refuses a system principal that has no configured Record grant for
		/// the department it resolved to, before the action runs (Identifier Allocation Registry section 4.4).
		/// A user principal is untouched. Mutating actions need no further guard: their policies are never
		/// issued to a system principal, so the claim check has already refused them.
		/// </summary>
		public void OnActionExecuting(ActionExecutingContext context)
		{
			var gate = SystemPrincipalGate();
			if (gate != null)
				context.Result = gate;
		}

		public void OnActionExecuted(ActionExecutedContext context)
		{
		}

		#region Dashboard

		/// <summary>
		/// The Records work queues an officer opens the module to look at (RMS-3): incomplete, awaiting review,
		/// rejected, accepted, overdue, plus the disclosure clock. Group-scope aware, so a member is never told that
		/// records exist which their own queue will not show them, and a count that cannot be produced degrades into
		/// a warning rather than failing the whole dashboard.
		/// </summary>
		[HttpGet("Dashboard")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordsDashboardResult>> Dashboard(CancellationToken cancellationToken)
		{
			if (!await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var dashboard = await _dashboard.GetAsync(DepartmentId, UserId, cancellationToken);
			var result = new RecordsDashboardResult { Data = RecordsDashboardApiMapper.ToDashboard(dashboard), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// NERIS crosswalk coverage: which of the department's own call types map to a NERIS incident type, which do
		/// not, and which map to a code the pinned contract no longer carries. A gap report, not a statistic — every
		/// unmapped type is a filing somebody classifies by hand on the night.
		/// </summary>
		[HttpGet("CrosswalkCoverage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<NerisCrosswalkCoverageResult>> CrosswalkCoverage(CancellationToken cancellationToken)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var coverage = await _dashboard.GetCrosswalkCoverageAsync(DepartmentId, cancellationToken);
			var result = new NerisCrosswalkCoverageResult { Data = RecordsDashboardApiMapper.ToCoverage(coverage), PageSize = coverage.Items.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		#endregion

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
					CanViewRestricted = await CanViewRestrictedAsync(),
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
				VisibleGroupIds = await VisibleGroupIdsAsync(),
				ViewerUserId = RecordsSystemPrincipal.IsSystemPrincipal(User) ? null : UserId,
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
			if (!RecordsSystemPrincipal.IsSystemPrincipal(User) && !await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
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

			var visible = await VisibleGroupIdsAsync();
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
						Text = text.Trim(), VisibleGroupIds = visible, ViewerUserId = RecordsSystemPrincipal.IsSystemPrincipal(User) ? null : UserId, States = states, DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey, Year = year, Skip = skip, Take = take
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
				var query = new RmsRecordQuery { Year = year, DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey, States = states, VisibleGroupIds = visible, ViewerUserId = RecordsSystemPrincipal.IsSystemPrincipal(User) ? null : UserId, Skip = skip, Take = take };
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
					if (!loaded.TryGetValue(id, out var projection) || !await CanViewRecordAsync(id))
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
		/// Persist Data.ServerTimestampMs, ServerCursorId and ScopeStamp together; loop while HasMore.
		/// ResetRequired requires clearing cached Records, revisions, attachments and evidence before a full pull.
		/// </summary>
		[HttpGet("Changes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<ActionResult<RecordsChangesResult>> Changes(long since = 0, int take = 200, string sinceId = null, string scopeStamp = null)
		{
			if (since < 0 || since > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()) return BadRequest();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			take = Math.Max(1, Math.Min(500, take));
			var result = new RecordsChangesResult { Data = new RecordsChangesData { Since = since } };
			var now = DateTime.UtcNow;

			if (moduleState.RecordsUsable)
			{
				result.Data.ScopeStamp = await ReadScopeStampAsync();
				if (result.Data.ScopeStamp == null) return Forbid();
				var resumeTime = RecordsApiHelper.FromUnixMs(since);
				string resumeId = null;
				var validCursor = string.IsNullOrEmpty(sinceId);
				if (!validCursor && RecordsApiHelper.TryReadChangesCursor(sinceId, since, out var exactTime, out resumeId))
				{
					resumeTime = exactTime; validCursor = true;
				}
				if (!validCursor || since > 0 && !string.Equals(scopeStamp, result.Data.ScopeStamp, StringComparison.Ordinal))
				{
					result.Data.ResetRequired = true;
					result.Data.ServerTimestampMs = 0;
					result.Status = ResponseHelper.Success;
					ResponseHelper.PopulateV4ResponseData(result);
					return Ok(result);
				}
				var rows = await _recordsService.GetChangesSinceAsync(DepartmentId, resumeTime, take + 1, resumeId);
				var hasMore = rows.Count > take;
				var page = rows.Take(take).ToList();
				foreach (var projection in page)
				{
					// Even an inaccessible live row can have been cached before a scope change. Only eviction metadata may leave here.
					var summary = RecordsApiMapper.ToSummary(projection);
					if (summary.IsTombstone || !await CanViewRecordAsync(projection.RmsRecordSearchProjectionId))
						summary = RecordsApiMapper.ToTombstone(projection);
					result.Data.Records.Add(summary);
				}
				result.Data.HasMore = hasMore;

				// Mid-stream the cursor is the last row delivered, timestamp and id together; at the end of the stream
				// it is the server clock with no tie-breaker, which is the full-catch-up case.
				if (hasMore && page.Count > 0)
				{
					var last = page[page.Count - 1];
					result.Data.ServerTimestampMs = RecordsApiHelper.ToUnixMs(last.ModifiedOn);
					result.Data.ServerCursorId = RecordsApiHelper.ChangesCursor(last.ModifiedOn, last.RmsRecordSearchProjectionId);
				}
				else
				{
					// Replay the final millisecond so a concurrent write in that clock bucket is not skipped.
					result.Data.ServerTimestampMs = RecordsApiHelper.ToUnixMs(now) - 1;
				}
				// A role, policy or membership change during hydration invalidates the entire page, including rows already authorized.
				for (var index = 0; index < result.Data.Records.Count; index++)
				{
					if (!result.Data.Records[index].IsTombstone && !await CanViewRecordAsync(page[index].RmsRecordSearchProjectionId))
						result.Data.Records[index] = RecordsApiMapper.ToTombstone(page[index]);
				}
				var finalScope = await ReadScopeStampAsync();
				if (finalScope == null) return Forbid();
				if (!string.Equals(result.Data.ScopeStamp, finalScope, StringComparison.Ordinal))
				{
					result.Data = new RecordsChangesData { Since = since, ScopeStamp = finalScope, ResetRequired = true, ServerTimestampMs = 0 };
				}
			}
			else
			{
				result.Data.ResetRequired = since > 0;
				result.Data.ServerTimestampMs = 0;
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

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, AccessPurpose(), IpAddressHelper.GetRequestIP(Request, true), RmsOriginClient.Api);
			return Ok(await WrapAsync(aggregate));
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

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, revisionId, RmsAccessAuditAction.Read, AccessPurpose("Revision " + revision.RevisionNumber), IpAddressHelper.GetRequestIP(Request, true), RmsOriginClient.Api);
			var result = new RecordRevisionSnapshotResult
			{
				Data = new RecordRevisionSnapshotData { Revision = RecordsApiMapper.ToRevision(revision), Snapshot = RecordsApiMapper.ToRecord(snapshot, await CanViewRestrictedAsync()) },
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

			var diffs = await _recordsService.DiffRevisionsAsync(DepartmentId, from, to, await CanViewRestrictedAsync());
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
			if (input.CallId.HasValue && !ClaimsAuthorizationHelper.CanViewCalls()) return Forbid();
			try
			{
				var aggregate = await _recordsService.CreateDraftAsync(DepartmentId, UserId, RecordsApiMapper.ToDraftInput(input, origin), cancellationToken);
				var replayed = !string.IsNullOrWhiteSpace(input.IdempotencyKey) && aggregate.Record.RowVersion > 1;
				var result = await WrapAsync(aggregate, replayed ? ResponseHelper.Success : ResponseHelper.Created);
				return replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
			}
			catch (RecordIdempotencyException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_idempotency_conflict");
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
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
		/// Creates and links a closed historical Call to an existing Run draft. Source authorization and the
		/// parent version prevent an unauthorized or duplicate Call from being created.
		/// </summary>
		[HttpPost("CreateRunCall")]
		[Authorize(Policy = ResgridResources.Record_Create)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<ActionResult<RecordResult>> CreateRunCall(CreateRunCallInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RecordId)) return BadRequest();
			var usable = await UsableAsync(); if (usable != null) return usable;
			var gate = await FieldClientGateAsync(RecordsApiHelper.ResolveOrigin(input.OriginClient)); if (gate != null) return gate;
			var version = RecordsApiHelper.ResolveRowVersion(input.RowVersion, Request);
			if (!version.HasValue) return Problem(statusCode: 428, title: "RowVersion or If-Match is required before creating the Call.");
			var occurred = RecordsApiHelper.Utc(input.OccurredOnUtc);
			if (!occurred.HasValue) return BadRequest("The Call occurrence time in UTC is required.");
			if (await LoadAuthorizedAsync(input.RecordId) == null) return NotFound();
			try
			{
				var saved = await _recordsService.CreateRunCallAsync(DepartmentId, UserId, input.RecordId, version.Value,
					new RecordNewCallInput { Name = input.Name, Address = input.Address, Nature = input.Nature, OccurredOnUtc = occurred.Value }, cancellationToken);
				var result = await WrapAsync(saved, ResponseHelper.Updated);
				if (!await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, input.RecordId, DepartmentId)) return Forbid();
				return Ok(result);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { return Problem(statusCode: 409, title: "The draft changed. Reload it and check its Call before retrying."); }
			catch (RecordTransitionException ex) { return Problem(statusCode: 409, title: ex.Message); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Problem(statusCode: 400, title: ex.Message); }
		}

		/// <summary>ETag-guarded draft save; conflicts require deliberate reconciliation.</summary>
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
			if (draft.CallId.HasValue && draft.CallId != current.Record.CallId && !ClaimsAuthorizationHelper.CanViewCalls()) return Forbid();
			draft.DefinitionKey = draft.DefinitionKey ?? current.Record.DefinitionKey;
			try
			{
				var saved = await _recordsService.SaveDraftAsync(DepartmentId, UserId, input.RecordId, rowVersion.Value, draft, cancellationToken);
				return Ok(await WrapAsync(saved, ResponseHelper.Updated));
			}
			catch (RecordConcurrencyException ex)
			{
				return await ConflictAsync(input.RecordId, ex.ExpectedRowVersion, draft);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
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
		private async Task<ActionResult<RecordResult>> CommandAsync(RecordCommandInput input, bool requiresRowVersion, Func<long, Task<RecordAggregate>> action,
			[CallerMemberName] string command = null)
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
			long rowVersion = current.Record.RowVersion;
			var supplied = RecordsApiHelper.ResolveRowVersion(input.RowVersion, Request);
			if (requiresRowVersion)
			{
				if (!supplied.HasValue)
					return Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "RowVersion or If-Match is required for this command.", type: "precondition_required");
				rowVersion = supplied.Value;
			}
			var requestChecksum = RecordSnapshotSerializer.Checksum(Newtonsoft.Json.JsonConvert.SerializeObject(new {
				input.RecordId, RowVersion = supplied, input.ReasonCode, input.ReasonText, input.Attested,
				AttestationStatementVersion = string.IsNullOrWhiteSpace(input.AttestationStatementVersion) ? "1" : input.AttestationStatementVersion,
				input.NewOwnerUserId, Origin = origin
			}));
			if (key != null)
			{
				var replayed = await _idempotency.TryGetCommandAsync(DepartmentId, UserId, key, command);
				if (replayed != null)
				{
					if (replayed.RecordId != input.RecordId || replayed.RequestChecksum != requestChecksum)
						return Problem(statusCode: StatusCodes.Status409Conflict, title: "This key belongs to a different or unverified command. Review the record before issuing another command.", type: "record_idempotency_conflict");
					if (replayed.IsPending)
						return Problem(statusCode: StatusCodes.Status409Conflict, title: "This command is running or its outcome needs review. Refresh the record; do not automatically issue the command under a new key.", type: "record_command_pending");
					var replay = await LoadAuthorizedAsync(input.RecordId, true);
					if (replay == null) return NotFound();
					var response = await WrapAsync(replay, ResponseHelper.Success);
					if (!await CanViewRecordAsync(input.RecordId)) return Forbid();
					return Ok(response);
				}
			}

			try
			{
				if (key != null && !await _idempotency.TryReserveCommandAsync(DepartmentId, UserId, key, command, input.RecordId, requestChecksum))
					return Problem(statusCode: StatusCodes.Status409Conflict, title: "Another request reserved this command. Refresh the record and retry the same key to check its outcome.", type: "record_command_pending");
				var aggregate = await action(rowVersion);
				if (key != null)
				{
					try { await _idempotency.RememberCommandAsync(DepartmentId, UserId, key, command, input.RecordId, requestChecksum); }
					catch (Exception ex)
					{
						Logging.LogException(ex, "A Records command completed but its durable receipt could not be acknowledged.");
						return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The command may have completed. Refresh the record and review its history; do not automatically repeat it under a new key.", type: "record_command_outcome_unknown");
					}
				}
				var response = await WrapAsync(aggregate, ResponseHelper.Updated);
				if (!await CanViewRecordAsync(input.RecordId)) return Forbid();
				return Ok(response);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
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
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<ActionResult<RecordAttachmentContentResult>> GetAttachment(string id, string attachmentId)
		{
			if (!await FlagOnAsync())
				return NotFound();
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			var attachment = await _recordsService.GetAttachmentAsync(DepartmentId, UserId, attachmentId);
			if (attachment == null || !string.Equals(attachment.RecordId, id, StringComparison.Ordinal) || attachment.Data == null || attachment.DeletedOn.HasValue)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, AccessPurpose("Attachment " + attachmentId), IpAddressHelper.GetRequestIP(Request, true), RmsOriginClient.Api);
			// Audit may await external work. Reload the file and intersect current grants before any bytes leave the process.
			attachment = await _recordsService.GetAttachmentAsync(DepartmentId, UserId, attachmentId);
			if (attachment == null || attachment.DepartmentId != DepartmentId || attachment.RecordId != id || attachment.RmsRecordAttachmentId != attachmentId
				|| attachment.DeletedOn.HasValue || attachment.Data == null || attachment.ScanState != (int)RmsAttachmentScanState.Clean
				|| RecordSnapshotSerializer.Checksum(attachment.Data) != attachment.Checksum) return NotFound();
			if (attachment.RequiresRestrictedAccess && !await CanViewRestrictedAsync()) return Forbid();
			if (!await CanViewRecordAsync(id)) return NotFound();
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
				var attachment = await _uploads.CompleteAsync(DepartmentId, UserId, input.UploadId, input.Description, cancellationToken, input.Classification);
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
				return Ok(await WrapAsync(await _recordsService.GetAsync(DepartmentId, input.RecordId, true), ResponseHelper.Deleted));
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

		#region System principals

		/// <summary>True when the caller is the relay key or a client_credentials service account, not a member.</summary>
		private bool IsSystemPrincipal => RecordsSystemPrincipal.IsSystemPrincipal(User);

		/// <summary>The configured grant this request runs under, or null for a user principal or an ungranted system one.</summary>
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
		/// A system principal with no grant for this department is refused before any read runs. Mutating
		/// endpoints need no equivalent: their policies are never issued to a system principal at all.
		/// </summary>
		private ActionResult SystemPrincipalGate()
		{
			if (!IsSystemPrincipal || SystemGrant != null)
				return null;

			return Problem(statusCode: StatusCodes.Status403Forbidden,
				title: "This system principal has no configured Record grant for this department.", type: "record_grant_missing");
		}

		/// <summary>Group filter for the caller: the grant's groups for a system principal, the member's own otherwise.</summary>
		private async Task<List<int>> VisibleGroupIdsAsync()
		{
			var grant = SystemGrant;
			if (grant != null)
				return grant.VisibleGroupIds();

			return await _recordsAuthorizationService.GetVisibleGroupIdsAsync(UserId, DepartmentId);
		}

		/// <summary>Per-record visibility for the caller, routed to the grant rule for a system principal.</summary>
		private async Task<bool> CanViewRecordAsync(string recordId)
		{
			var grant = SystemGrant;
			if (grant != null)
				return await _recordsAuthorizationService.CanSystemPrincipalViewRecordAsync(grant, recordId);

			return await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)
				&& await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, recordId, DepartmentId);
		}

		private async Task<string> ReadScopeStampAsync()
		{
			try
			{
				var grant = SystemGrant;
				string memberScope = null;
				if (IsSystemPrincipal && grant == null) return null;
				if (grant == null)
				{
					if (!await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) return null;
					memberScope = await _recordsAuthorizationService.GetReadScopeStampAsync(UserId, DepartmentId);
					if (string.IsNullOrWhiteSpace(memberScope)) return null;
				}
				var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId, true);
				var visible = await VisibleGroupIdsAsync();
				return RecordSnapshotSerializer.Checksum(Newtonsoft.Json.JsonConvert.SerializeObject(new
				{
					DepartmentId, UserId, MemberScope = memberScope, Groups = visible?.Distinct().OrderBy(g => g).ToArray(),
					Restricted = await CanViewRestrictedAsync(), SystemPurpose = grant?.Purpose,
					ProtectionState = policy?.State, ProtectionEpoch = policy?.PolicyEpoch, ProtectionCatalog = policy?.CatalogVersion
				}));
			}
			catch (Exception ex) { Resgrid.Framework.Logging.LogException(ex); return null; }
		}

		/// <summary>Audit purpose: the grant's stated purpose for a system principal, so a machine read is never anonymous.</summary>
		private string AccessPurpose(string purpose = null)
		{
			var grant = SystemGrant;
			if (grant == null)
				return purpose;

			return string.IsNullOrWhiteSpace(purpose) ? grant.Purpose : $"{grant.Purpose}: {purpose}";
		}

		#endregion

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

			if (!await CanViewRecordAsync(id))
			{
				await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Denied, AccessPurpose(), IpAddressHelper.GetRequestIP(Request, true), RmsOriginClient.Api);
				return null;
			}

			var aggregate = await _recordsService.GetAsync(DepartmentId, id, includeRevisions);
			if (aggregate?.Record == null || aggregate.Record.DepartmentId != DepartmentId || aggregate.Record.DeletedOn.HasValue || aggregate.Record.PurgedOn.HasValue) return null;
			if (aggregate != null && !await CanViewRestrictedAsync()) aggregate.Attachments = aggregate.Attachments.Where(a => !a.RequiresRestrictedAccess).ToList();
			if (!await CanViewRecordAsync(id)) return null;
			return aggregate;
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
			var result = new RecordConflictResult { Data = RecordsApiMapper.ToConflict(conflict, current, await CanViewRestrictedAsync()), PageSize = 1, Status = ConflictStatus };
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, current.Record.RowVersion);
			if (!await CanViewRecordAsync(recordId)) return Forbid();
			return Conflict(result);
		}

		private async Task<RecordResult> WrapAsync(RecordAggregate aggregate, string status = ResponseHelper.Success)
		{
			var result = new RecordResult { Data = RecordsApiMapper.ToRecord(aggregate, await CanViewRestrictedAsync()), PageSize = 1, Status = status };
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, aggregate.Record.RowVersion);
			return result;
		}

		#endregion

        private async Task<bool> CanViewRestrictedAsync() => ClaimsAuthorizationHelper.CanViewRestrictedRecords()
            && await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);
	}
}
