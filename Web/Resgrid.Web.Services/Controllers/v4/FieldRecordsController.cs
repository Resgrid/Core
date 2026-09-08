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
	/// Field Records for the Responder, Unit, Incident Command and Dispatch apps (RMS plan RMS-1D): minimum-version
	/// preflight, the FieldRecordCatalogV1 manifest, server-calculated prefill with provenance, the bounded sync
	/// bundle, and work assignments. Every filter is derived server-side from the authenticated principal, the
	/// department, the app flag and the verified context; a forged origin, context or capability value can only
	/// narrow the response. Authoring itself stays on the Records controller.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class FieldRecordsController : V4AuthenticatedApiControllerbase
	{
		private readonly IFieldRecordsService _field;
		private readonly IRecordWorkAssignmentsService _assignments;
		private readonly IRecordsFieldRolloutService _rollout;

		public FieldRecordsController(IFieldRecordsService field, IRecordWorkAssignmentsService assignments, IRecordsFieldRolloutService rollout)
		{
			_field = field;
			_assignments = assignments;
			_rollout = rollout;
		}

		#region Preflight and catalog

		/// <summary>Whether this app, at this version, may show Records at all in this department.</summary>
		[HttpGet("Preflight")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordPreflightResult>> Preflight(int? originClient = null, string appVersion = null, string clientCapability = null)
		{
			var origin = Origin(originClient);
			var preflight = await _field.PreflightAsync(DepartmentId, UserId, origin, AppVersion(appVersion), clientCapability);
			var result = new FieldRecordPreflightResult
			{
				Data = new FieldRecordPreflightData
				{
					ContractVersion = preflight.ContractVersion, SyncContractVersion = preflight.SyncContractVersion, OriginClient = preflight.Origin.ToString(), Ok = preflight.Ok,
					Reasons = preflight.Reasons, ModuleEnabled = preflight.ModuleEnabled, RecordsUsable = preflight.RecordsUsable, AppEnabled = preflight.AppEnabled,
					MinimumAppVersion = preflight.MinimumAppVersion, AppVersion = preflight.AppVersion, ClientCapability = preflight.ClientCapability,
					ProtectionState = preflight.ProtectionState, ServerTimestampMs = preflight.ServerTimestampMs
				},
				Status = ResponseHelper.Success, PageSize = 1
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The definitions this app may start right now, plus a coded reason for each one withheld.</summary>
		[HttpPost("Catalog")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordCatalogResult>> Catalog([FromBody] FieldRecordCatalogInput input)
		{
			var catalog = await _field.GetCatalogAsync(DepartmentId, UserId, ToRequest(input));
			var result = new FieldRecordCatalogResult { Data = ToData(catalog), Status = ResponseHelper.Success, PageSize = catalog.Definitions.Count };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Server-calculated prefill for one catalog entry; refused when that entry is not in this client's catalog.</summary>
		[HttpPost("Prefill")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<FieldRecordPrefillResult>> Prefill([FromBody] FieldRecordPrefillInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.DefinitionKey)) return BadRequest();
			FieldRecordPrefill prefill;
			try
			{
				prefill = await _field.PrefillAsync(DepartmentId, UserId, ToRequest(input), input.DefinitionKey, input.Version);
			}
			catch (UnauthorizedAccessException)
			{
				return Forbid();
			}

			var result = new FieldRecordPrefillResult
			{
				Data = new FieldRecordPrefillData
				{
					ContractVersion = prefill.ContractVersion, DefinitionKey = prefill.DefinitionKey, Version = prefill.Version, PrefillVersion = prefill.PrefillVersion,
					CallId = prefill.CallId, UnitId = prefill.UnitId, StationGroupId = prefill.StationGroupId, Provenance = prefill.Provenance,
					SuggestedParticipantUserIds = prefill.SuggestedParticipantUserIds, SuggestedUnitIds = prefill.SuggestedUnitIds, CalculatedOn = prefill.CalculatedOn,
					Values = prefill.Values.Select(v => new FieldRecordPrefillValueData { SectionKey = v.SectionKey, FieldKey = v.FieldKey, Value = v.Value, ReferenceType = v.ReferenceType, ReferenceId = v.ReferenceId }).ToList()
				},
				Status = ResponseHelper.Success, PageSize = 1
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		#endregion

		#region Sync

		/// <summary>
		/// The bounded working set: catalog, authorized change delta with tombstones, the caller's own drafts and
		/// returned Records, and their open assignments. A scope change answers ResetRequired instead of a page.
		/// </summary>
		[HttpPost("Sync")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordSyncResult>> Sync([FromBody] FieldRecordSyncInput input, CancellationToken cancellationToken)
		{
			input ??= new FieldRecordSyncInput();
			if (input.Since < 0 || input.Since > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()) return BadRequest();

			var request = new FieldRecordSyncRequest
			{
				Origin = Origin(input.OriginClient), AppVersion = AppVersion(input.AppVersion), ClientCapability = input.ClientCapability,
				Context = input.Context?.ToContext() ?? new FieldRecordContext(), Since = input.Since, SinceId = input.SinceId, ScopeStamp = input.ScopeStamp,
				Take = input.Take, IncludeCatalog = input.IncludeCatalog
			};
			var bundle = await _field.SyncAsync(DepartmentId, UserId, request, cancellationToken);
			var result = new FieldRecordSyncResult
			{
				Data = new FieldRecordSyncData
				{
					ContractVersion = bundle.ContractVersion, Ok = bundle.Ok, Reasons = bundle.Reasons, ScopeStamp = bundle.ScopeStamp, ResetRequired = bundle.ResetRequired,
					Since = bundle.Since, ServerTimestampMs = bundle.ServerTimestampMs, ServerCursorId = bundle.ServerCursorId, HasMore = bundle.HasMore,
					Catalog = bundle.Catalog == null ? null : ToData(bundle.Catalog), Tombstones = bundle.Tombstones,
					Records = bundle.Changes.Select(RecordsApiMapper.ToSummary).ToList(),
					Drafts = bundle.Drafts.Select(RecordsApiMapper.ToSummary).ToList(),
					Assignments = bundle.Assignments.Select(ToAssignment).ToList()
				},
				Status = ResponseHelper.Success, PageSize = bundle.Changes.Count
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		#endregion

		#region Work assignments

		/// <summary>The caller's open work queue, narrowed by assignment and re-authorized per Record.</summary>
		[HttpPost("Assignments")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordAssignmentsResult>> Assignments([FromBody] FieldRecordCatalogInput input)
		{
			var rows = await _assignments.GetQueueAsync(DepartmentId, UserId, input?.Context?.ToContext(), 0);
			var result = new FieldRecordAssignmentsResult { Data = rows.Select(ToAssignment).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Assignments on one Record; empty when the caller cannot read that Record.</summary>
		[HttpGet("RecordAssignments")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordAssignmentsResult>> RecordAssignments(string recordId)
		{
			if (string.IsNullOrWhiteSpace(recordId)) return BadRequest();
			var rows = await _assignments.GetForRecordAsync(DepartmentId, UserId, recordId);
			var result = new FieldRecordAssignmentsResult { Data = rows.Select(ToAssignment).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpPost("Assign")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<FieldRecordAssignmentResult>> Assign([FromBody] FieldRecordAssignInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RecordId)) return BadRequest();
			return await CommandAsync(() => _assignments.AssignAsync(DepartmentId, UserId, new RecordWorkAssignmentInput
			{
				RecordId = input.RecordId, AssigneeKind = (RmsWorkAssigneeKind)input.AssigneeKind, AssigneeUserId = input.AssigneeUserId, AssigneeUnitId = input.AssigneeUnitId,
				AssigneeGroupId = input.AssigneeGroupId, AssigneeRole = input.AssigneeRole, Purpose = input.Purpose, Note = input.Note, DueOn = input.DueOn,
				SourceContext = input.Context?.ToContext(), OriginClient = Origin(input.OriginClient)
			}, cancellationToken));
		}

		[HttpPost("AcknowledgeAssignment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordAssignmentResult>> AcknowledgeAssignment([FromBody] FieldRecordAssignmentCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.AssignmentId)) return BadRequest();
			return await CommandAsync(() => _assignments.AcknowledgeAsync(DepartmentId, UserId, input.AssignmentId, input.RowVersion, input.Context?.ToContext(), Origin(input.OriginClient), cancellationToken));
		}

		[HttpPost("CompleteAssignment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordAssignmentResult>> CompleteAssignment([FromBody] FieldRecordAssignmentCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.AssignmentId)) return BadRequest();
			return await CommandAsync(() => _assignments.CompleteAsync(DepartmentId, UserId, input.AssignmentId, input.RowVersion, input.Context?.ToContext(), Origin(input.OriginClient), cancellationToken));
		}

		[HttpPost("CancelAssignment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<FieldRecordAssignmentResult>> CancelAssignment([FromBody] FieldRecordAssignmentCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.AssignmentId)) return BadRequest();
			return await CommandAsync(() => _assignments.CancelAsync(DepartmentId, UserId, input.AssignmentId, input.RowVersion, input.Reason, Origin(input.OriginClient), cancellationToken));
		}

		#endregion

		#region Rollout telemetry

		/// <summary>
		/// Safe rollout datapoints from this app (RMS plan RMS-1D): coded outcomes, counts and durations against the
		/// authenticated department and member. Nothing here carries record content, and a report that names an
		/// unknown event or a non-field origin is dropped rather than stored.
		/// </summary>
		[HttpPost("Telemetry")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordTelemetryResult>> Telemetry([FromBody] FieldRecordTelemetryInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.Events == null || input.Events.Count == 0)
				return BadRequest();

			var accepted = await _rollout.RecordBatchAsync(DepartmentId, UserId, new RecordFieldRolloutBatch
			{
				OriginClient = Origin(input.OriginClient),
				AppVersion = AppVersion(input.AppVersion),
				ClientCapability = input.ClientCapability,
				Events = input.Events.Select(e => new RecordFieldRolloutInput
				{
					EventType = e.EventType, Outcome = e.Outcome, DefinitionKey = e.DefinitionKey, DefinitionVersion = e.DefinitionVersion,
					RecordId = e.RecordId, DurationMs = e.DurationMs, ItemCount = e.ItemCount, OccurredOn = e.OccurredOn
				}).ToList()
			}, cancellationToken);

			var result = new FieldRecordTelemetryResult { Data = new FieldRecordTelemetryData { Accepted = accepted }, Status = ResponseHelper.Success, PageSize = accepted };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The per-app rollout dashboard for this department (RMS plan RMS-1D). Department administration only.</summary>
		[HttpGet("Rollout")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<FieldRecordRolloutResult>> Rollout(int windowDays = 30, CancellationToken cancellationToken = default)
		{
			try
			{
				var rollout = await _rollout.GetAsync(DepartmentId, UserId, windowDays, cancellationToken);
				var result = new FieldRecordRolloutResult { Data = rollout, Status = ResponseHelper.Success, PageSize = rollout.Apps.Count };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (UnauthorizedAccessException)
			{
				return Forbid();
			}
		}

		#endregion

		#region Helpers

		private async Task<ActionResult<FieldRecordAssignmentResult>> CommandAsync(Func<Task<RmsRecordWorkAssignment>> action)
		{
			try
			{
				var row = await action();
				var result = new FieldRecordAssignmentResult { Data = ToAssignment(row), Status = ResponseHelper.Success, PageSize = 1 };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException ex) { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "assignment_conflict"); }
			catch (RecordTransitionException ex) { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition"); }
			catch (InvalidOperationException ex) { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "assignment_state"); }
			catch (ArgumentException ex) { return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "assignment_validation"); }
		}

		private FieldRecordCatalogRequest ToRequest(FieldRecordCatalogInput input) => new FieldRecordCatalogRequest
		{
			Origin = Origin(input?.OriginClient),
			AppVersion = AppVersion(input?.AppVersion),
			ClientCapability = input?.ClientCapability,
			Context = input?.Context?.ToContext() ?? new FieldRecordContext()
		};

		private static FieldRecordCatalogData ToData(FieldRecordCatalog catalog) => new FieldRecordCatalogData
		{
			ContractVersion = catalog.ContractVersion, OriginClient = catalog.Origin.ToString(), Ok = catalog.Ok, Reasons = catalog.Reasons, ContextKind = catalog.ContextKind,
			ContextVerified = catalog.ContextVerified, ProtectionState = catalog.ProtectionState, ScopeStamp = catalog.ScopeStamp, Definitions = catalog.Definitions,
			Exclusions = catalog.Exclusions, ServerTimestampMs = catalog.ServerTimestampMs
		};

		private static FieldRecordAssignmentData ToAssignment(RmsRecordWorkAssignment a) => new FieldRecordAssignmentData
		{
			AssignmentId = a.RmsRecordWorkAssignmentId, RecordId = a.RecordId, AssigneeKind = ((RmsWorkAssigneeKind)a.AssigneeKind).ToString(), AssigneeUserId = a.AssigneeUserId,
			AssigneeUnitId = a.AssigneeUnitId, AssigneeGroupId = a.AssigneeGroupId, AssigneeRole = a.AssigneeRole, Purpose = a.Purpose, Note = a.Note, DueOn = a.DueOn,
			State = ((RmsWorkAssignmentState)a.State).ToString(), AcknowledgedOn = a.AcknowledgedOn, CompletedOn = a.CompletedOn, OriginClient = ((RmsOriginClient)a.OriginClient).ToString(),
			CreatedOn = a.CreatedOn, CreatedByUserId = a.CreatedByUserId, RowVersion = a.RowVersion
		};

		/// <summary>The claimed origin, normalized: a non-field value is System, which every field gate refuses.</summary>
		private static RmsOriginClient Origin(int? value)
		{
			var origin = RecordsApiHelper.ResolveOrigin(value);
			return FieldRecordCatalogV1.IsFieldOrigin(origin) ? origin : RmsOriginClient.System;
		}

		/// <summary>The body value when present, otherwise the standard app header.</summary>
		private string AppVersion(string bodyValue)
		{
			if (!string.IsNullOrWhiteSpace(bodyValue)) return bodyValue.Trim();
			var header = Request?.Headers["X-Resgrid-App-Version"].ToString();
			return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
		}

		#endregion
	}
}
