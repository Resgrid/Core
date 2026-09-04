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
using Resgrid.Model.Providers;
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
	/// NERIS incident reports over the v4 Records contract (RMS-2 aggregate, RMS-1B client contract): one authoritative
	/// report per Call, source-aware prefill with provenance, ETag-guarded draft saves, local/destination validation,
	/// attestation, and sanitized submission history. Same gates as Records: flag first, per-record visibility on
	/// every read, Field Records flags for field clients.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IncidentReportsController : V4AuthenticatedApiControllerbase
	{
		private readonly IIncidentReportsService _incidentReports;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly INerisProfileService _neris;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IRecordsApiIdempotencyService _idempotency;

		public IncidentReportsController(IIncidentReportsService incidentReports, IRecordsCutoverService cutoverService, IRecordsAuthorizationService recordsAuthorizationService,
			INerisProfileService neris, IFeatureToggleService featureToggleService, IRecordsApiIdempotencyService idempotency)
		{
			_incidentReports = incidentReports;
			_cutoverService = cutoverService;
			_recordsAuthorizationService = recordsAuthorizationService;
			_neris = neris;
			_featureToggleService = featureToggleService;
			_idempotency = idempotency;
		}

		#region Reads

		[HttpGet("GetIncidentReports")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<IncidentReportsResult>> GetIncidentReports(int? year, int? state, int? callId, string owner, int? group, int skip = 0, int take = 50)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var result = new IncidentReportsResult();
			if (moduleState.RecordsUsable)
			{
				var query = new RmsIncidentReportQuery
				{
					Year = year,
					States = state.HasValue ? new List<int> { state.Value } : null,
					CallId = callId,
					OwnerUserId = string.IsNullOrWhiteSpace(owner) ? null : owner,
					StationGroupId = group,
					Skip = Math.Max(0, skip),
					Take = Math.Max(1, Math.Min(RecordsController.MaxPageSize, take))
				};
				var visible = await _recordsAuthorizationService.GetVisibleGroupIdsAsync(UserId, DepartmentId);
				foreach (var report in await _incidentReports.QueryAsync(DepartmentId, query))
				{
					if (visible == null || await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, report.RmsIncidentReportId, DepartmentId))
						result.Data.Add(IncidentReportsApiMapper.ToSummary(report));
				}
				result.Total = visible == null ? await _incidentReports.CountAsync(DepartmentId, query) : query.Skip + result.Data.Count;
				result.Page = query.Skip / query.Take;
			}

			result.PageSize = result.Data.Count;
			result.Status = result.Data.Count > 0 ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>One report with all sections, provenance facts, validation issues and sanitized submission history. Sets a weak ETag.</summary>
		[HttpGet("GetIncidentReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<IncidentReportResult>> GetIncidentReport(string id)
		{
			if (!await FlagOnAsync())
				return NotFound();
			var aggregate = await LoadAuthorizedAsync(id, true);
			if (aggregate == null)
				return NotFound();

			await _incidentReports.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, null, IpAddressHelper.GetRequestIP(Request, true));
			return Ok(await WrapAsync(aggregate));
		}

		/// <summary>The authoritative report for a Call, if one exists.</summary>
		[HttpGet("GetForCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<IncidentReportResult>> GetForCall(int callId)
		{
			if (!await FlagOnAsync())
				return NotFound();
			var existing = await _incidentReports.GetForCallAsync(DepartmentId, callId);
			if (existing == null)
				return NotFound();

			var aggregate = await LoadAuthorizedAsync(existing.Report.RmsIncidentReportId, true);
			if (aggregate == null)
				return NotFound();
			return Ok(await WrapAsync(aggregate));
		}

		#endregion

		#region Authoring

		/// <summary>Starts the report for a Call with dispatch prefill (SingleAuthoritative): a second start returns the existing report with 200.</summary>
		[HttpPost("Start")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<IncidentReportResult>> Start(StartIncidentReportInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.CallId <= 0)
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var origin = RecordsApiHelper.ResolveOrigin(input.OriginClient);
			var gate = await FieldClientGateAsync(origin);
			if (gate != null)
				return gate;

			try
			{
				var existing = await _incidentReports.GetForCallAsync(DepartmentId, input.CallId);
				var aggregate = await _incidentReports.StartFromCallAsync(DepartmentId, UserId, input.CallId, origin, cancellationToken);
				var result = await WrapAsync(aggregate, existing != null ? ResponseHelper.Success : ResponseHelper.Created);
				return existing != null ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
			}
			catch (ArgumentException ex)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		/// <summary>ETag-guarded draft save; 409 with the current report on a stale row version.</summary>
		[HttpPost("SaveDraft")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<IncidentReportResult>> SaveDraft(SaveIncidentReportDraftInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ReportId))
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

			var current = await LoadAuthorizedAsync(input.ReportId);
			if (current == null)
				return NotFound();
			if (!CanEditReport(current.Report))
				return Forbid();

			try
			{
				var saved = await _incidentReports.SaveDraftAsync(DepartmentId, UserId, input.ReportId, rowVersion.Value, IncidentReportsApiMapper.ToDraftInput(input, origin), cancellationToken);
				return Ok(await WrapAsync(saved, ResponseHelper.Updated));
			}
			catch (RecordConcurrencyException ex)
			{
				return await ConflictAsync(input.ReportId, ex.ExpectedRowVersion);
			}
			catch (ArgumentException ex)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
			catch (RecordTransitionException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message, type: "record_transition");
			}
		}

		/// <summary>Runs local validation (and the destination's validate endpoint when IncludeDestination and submission is enabled); issues stay on the report.</summary>
		[HttpPost("Validate")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<IncidentValidationResult>> Validate(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ReportId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;
			if (await LoadAuthorizedAsync(input.ReportId) == null)
				return NotFound();

			try
			{
				var issues = await _incidentReports.ValidateAsync(DepartmentId, input.ReportId, input.IncludeDestination, cancellationToken);
				var result = new IncidentValidationResult { Data = issues.Select(IncidentReportsApiMapper.ToIssue).ToList(), HasBlockingIssues = issues.Any(i => i.Severity == (int)RmsValidationSeverity.Error), Status = ResponseHelper.Success };
				result.PageSize = result.Data.Count;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		#endregion

		#region Lifecycle

		[HttpPost("SubmitForReview")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public Task<ActionResult<IncidentReportResult>> SubmitForReview(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, true, rowVersion => _incidentReports.SubmitForReviewAsync(DepartmentId, UserId, input.ReportId, rowVersion, cancellationToken));
		}

		[HttpPost("ReturnForCorrection")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public Task<ActionResult<IncidentReportResult>> ReturnForCorrection(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _incidentReports.ReturnForCorrectionAsync(DepartmentId, UserId, input.ReportId, input.ReasonCode, input.ReasonText, cancellationToken));
		}

		/// <summary>Validate-then-sign: local errors return 422 with the issues; otherwise the revision, attestation and (when the profile allows) the submission are written.</summary>
		[HttpPost("Finalize")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Finalize)]
		public async Task<ActionResult<IncidentReportResult>> Finalize(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			if (input != null && !input.Attested)
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The attestation statement must be accepted to finalize.", type: "attestation_required");
			return await CommandAsync(input, true, rowVersion => _incidentReports.FinalizeAsync(DepartmentId, UserId, input.ReportId, rowVersion, IncidentReportsService.AttestationStatementVersion, IpAddressHelper.GetRequestIP(Request, true), input.ReasonCode, input.ReasonText, cancellationToken));
		}

		/// <summary>After a rejection: a new revision and a new idempotency key, back into the submission queue.</summary>
		[HttpPost("CorrectAndResubmit")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public async Task<ActionResult<IncidentReportResult>> CorrectAndResubmit(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			if (input != null && !input.Attested)
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The attestation statement must be accepted to resubmit.", type: "attestation_required");
			return await CommandAsync(input, true, rowVersion => _incidentReports.CorrectAndResubmitAsync(DepartmentId, UserId, input.ReportId, rowVersion, IncidentReportsService.AttestationStatementVersion, IpAddressHelper.GetRequestIP(Request, true), input.ReasonCode, input.ReasonText, cancellationToken));
		}

		/// <summary>Queues (or re-queues) the current revision for the destination.</summary>
		[HttpPost("Submit")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public Task<ActionResult<IncidentReportResult>> Submit(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _incidentReports.QueueSubmissionAsync(DepartmentId, UserId, input.ReportId, cancellationToken));
		}

		[HttpPost("OpenAmendment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Amend)]
		public Task<ActionResult<IncidentReportResult>> OpenAmendment(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _incidentReports.OpenAmendmentAsync(DepartmentId, UserId, input.ReportId, cancellationToken));
		}

		[HttpPost("AbandonAmendment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Amend)]
		public Task<ActionResult<IncidentReportResult>> AbandonAmendment(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _incidentReports.AbandonAmendmentAsync(DepartmentId, UserId, input.ReportId, cancellationToken));
		}

		[HttpPost("Void")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public Task<ActionResult<IncidentReportResult>> Void(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _incidentReports.VoidAsync(DepartmentId, UserId, input.ReportId, input.ReasonCode, input.ReasonText, cancellationToken));
		}

		[HttpPost("Cancel")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public Task<ActionResult<IncidentReportResult>> Cancel(IncidentReportCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _incidentReports.CancelAsync(DepartmentId, UserId, input.ReportId, cancellationToken));
		}

		private async Task<ActionResult<IncidentReportResult>> CommandAsync(IncidentReportCommandInput input, bool requiresRowVersion, Func<long, Task<IncidentReportAggregate>> action)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ReportId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var origin = RecordsApiHelper.ResolveOrigin(input.OriginClient);
			var gate = await FieldClientGateAsync(origin);
			if (gate != null)
				return gate;

			var current = await LoadAuthorizedAsync(input.ReportId);
			if (current == null)
				return NotFound();

			var key = RecordsApiHelper.ResolveIdempotencyKey(input.IdempotencyKey, Request);
			if (key != null)
			{
				var replayed = await _idempotency.TryGetRecordIdAsync(DepartmentId, UserId, key);
				if (string.Equals(replayed, input.ReportId, StringComparison.Ordinal))
					return Ok(await WrapAsync(await _incidentReports.GetAsync(DepartmentId, input.ReportId, true), ResponseHelper.Success));
			}

			long rowVersion = current.Report.RowVersion;
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
					await _idempotency.RememberAsync(DepartmentId, UserId, key, input.ReportId);
				return Ok(await WrapAsync(aggregate, ResponseHelper.Updated));
			}
			catch (RecordConcurrencyException ex)
			{
				return await ConflictAsync(input.ReportId, ex.ExpectedRowVersion);
			}
			catch (IncidentReportValidationException ex)
			{
				var result = new IncidentValidationResult { Data = ex.Issues.Select(IncidentReportsApiMapper.ToIssue).ToList(), HasBlockingIssues = true, Status = ResponseHelper.Failure };
				result.PageSize = result.Data.Count;
				ResponseHelper.PopulateV4ResponseData(result);
				return StatusCode(StatusCodes.Status422UnprocessableEntity, result);
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

		private async Task<ActionResult> FieldClientGateAsync(RmsOriginClient origin)
		{
			var flag = RecordsApiHelper.FieldFlagFor(origin);
			if (flag == null || await _featureToggleService.IsEnabledAsync(flag, DepartmentId))
				return null;
			return Problem(statusCode: StatusCodes.Status403Forbidden, title: $"Field Records are not enabled for the {origin} app in this department.", type: "field_records_disabled");
		}

		private async Task<IncidentReportAggregate> LoadAuthorizedAsync(string id, bool includeHistory = false)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			if (!await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, id, DepartmentId))
			{
				await _incidentReports.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Denied, null, IpAddressHelper.GetRequestIP(Request, true));
				return null;
			}

			return await _incidentReports.GetAsync(DepartmentId, id, includeHistory);
		}

		private bool CanEditReport(RmsIncidentReport report)
		{
			if (!ClaimsAuthorizationHelper.CanCreateRecord())
				return false;

			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
				|| string.Equals(report.OwnerUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(report.AuthorUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| (report.AmendsRevisionId != null && ClaimsAuthorizationHelper.CanAmendRecords());
		}

		private async Task<ActionResult<IncidentReportResult>> ConflictAsync(string reportId, long expectedRowVersion)
		{
			var current = await _incidentReports.GetAsync(DepartmentId, reportId, true);
			if (current == null)
				return NotFound();

			var result = new IncidentReportConflictResult
			{
				Data = new IncidentReportConflictData
				{
					ReportId = reportId,
					ExpectedRowVersion = expectedRowVersion,
					CurrentRowVersion = current.Report.RowVersion,
					CurrentState = current.Report.State,
					CurrentStateName = ((RmsRecordState)current.Report.State).ToString(),
					Current = IncidentReportsApiMapper.ToReport(current, await _neris.IsSubmissionEnabledAsync(DepartmentId))
				},
				PageSize = 1,
				Status = RecordsController.ConflictStatus
			};
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, current.Report.RowVersion);
			return Conflict(result);
		}

		private async Task<IncidentReportResult> WrapAsync(IncidentReportAggregate aggregate, string status = ResponseHelper.Success)
		{
			var result = new IncidentReportResult { Data = IncidentReportsApiMapper.ToReport(aggregate, await _neris.IsSubmissionEnabledAsync(DepartmentId)), PageSize = 1, Status = status };
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, aggregate.Report.RowVersion);
			return result;
		}

		#endregion
	}
}
