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
	public class IncidentReportsController : V4AuthenticatedApiControllerbase, IActionFilter
	{
		private readonly IIncidentReportsService _incidentReports;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly INerisProfileService _neris;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IRecordsApiIdempotencyService _idempotency;
		private readonly IIncidentAnalysisService _analysis;
		private readonly IRecordsSubmissionService _submissionWorker;

		private SystemPrincipalRecordGrant _systemGrant;
		private bool _systemGrantResolved;

		public IncidentReportsController(IIncidentReportsService incidentReports, IRecordsCutoverService cutoverService, IRecordsAuthorizationService recordsAuthorizationService,
			INerisProfileService neris, IFeatureToggleService featureToggleService, IRecordsApiIdempotencyService idempotency, IIncidentAnalysisService analysis, IRecordsSubmissionService submissionWorker)
		{
			_incidentReports = incidentReports;
			_cutoverService = cutoverService;
			_recordsAuthorizationService = recordsAuthorizationService;
			_neris = neris;
			_featureToggleService = featureToggleService;
			_idempotency = idempotency;
			_analysis = analysis;
			_submissionWorker = submissionWorker;
		}

		[HttpPost("Submissions/{submissionId}/Reconcile")]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public async Task<IActionResult> Reconcile(string submissionId, [FromBody] RmsSubmissionReconciliationInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			if (!(await _cutoverService.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			try
			{
				if (input.ConfirmedNotCreated)
				{
					if (!string.IsNullOrWhiteSpace(input.ExternalId)) return BadRequest("A recorded filing cannot also be declared absent.");
					await _submissionWorker.ConfirmNotCreatedAsync(DepartmentId, UserId, submissionId, input.RowVersion, input.VerificationReference, input.Reason, cancellationToken);
				}
				else await _submissionWorker.ReconcileAsync(DepartmentId, UserId, submissionId, input.RowVersion, input.ExternalId, input.Reason, cancellationToken);
				return NoContent();
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException ex) { return Problem(statusCode: 409, title: ex.Message); }
			catch (InvalidOperationException ex) { return Problem(statusCode: 409, title: ex.Message); }
			catch (ArgumentException ex) { return Problem(statusCode: 400, title: ex.Message); }
		}

		[HttpGet("Submissions/{submissionId}/Exchanges")]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> ExchangeHistory(string submissionId, CancellationToken cancellationToken)
		{
			if (!(await _cutoverService.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			try { return Ok(await _submissionWorker.GetHistoryAsync(DepartmentId, UserId, submissionId, cancellationToken)); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (InvalidOperationException) { return NotFound(); }
		}

		/// <summary>
		/// Same system-principal gate as <see cref="RecordsController"/>: a service account with no configured
		/// Record grant for this department is refused before the action runs, and a granted one is confined to
		/// reads because no mutating Record policy is ever issued to it (registry section 4.4).
		/// </summary>
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

			return await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, recordId, DepartmentId);
		}

		/// <summary>Audit purpose: the grant's stated purpose for a system principal, so a machine read is never anonymous.</summary>
		private string AccessPurpose(string purpose = null)
		{
			var grant = SystemGrant;
			if (grant == null)
				return purpose;

			return string.IsNullOrWhiteSpace(purpose) ? grant.Purpose : $"{grant.Purpose}: {purpose}";
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
				// Visibility belongs in the query, not in a filter over the page. Filtering afterwards made the total
				// the size of whatever survived this page, so a member with scoped visibility was told there was one
				// page and could never reach older reports.
				var visible = await VisibleGroupIdsAsync();
				var query = new RmsIncidentReportQuery
				{
					Year = year,
					States = state.HasValue ? new List<int> { state.Value } : null,
					CallId = callId,
					OwnerUserId = string.IsNullOrWhiteSpace(owner) ? null : owner,
					StationGroupId = group,
					VisibleGroupIds = visible,
					ViewerUserId = SystemGrant == null ? UserId : null,
					Skip = Math.Max(0, skip),
					Take = Math.Max(1, Math.Min(RecordsController.MaxPageSize, take))
				};
				foreach (var report in await _incidentReports.QueryAsync(DepartmentId, query))
					result.Data.Add(IncidentReportsApiMapper.ToSummary(report));

				result.Total = await _incidentReports.CountAsync(DepartmentId, query);
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

			await _incidentReports.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, AccessPurpose(), IpAddressHelper.GetRequestIP(Request, true));
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
			if (!ClaimsAuthorizationHelper.CanViewCalls()) return Forbid();
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
			catch (UnauthorizedAccessException) { return Forbid(); }
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
				var saved = await _incidentReports.SaveDraftAsync(DepartmentId, UserId, input.ReportId, rowVersion.Value, IncidentReportsApiMapper.ToDraftInput(input, origin), await CanViewRestrictedAsync(), cancellationToken);
				return Ok(await WrapAsync(saved, ResponseHelper.Updated));
			}
			catch (RecordConcurrencyException ex)
			{
				return await ConflictAsync(input.ReportId, ex.ExpectedRowVersion);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
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

		/// <summary>
		/// <paramref name="command"/> scopes the idempotency key to the calling action. Without it a client that
		/// reused one key for SubmitForReview and then Finalize matched on the report alone and was told the second
		/// command succeeded without it ever running.
		/// </summary>
		private async Task<ActionResult<IncidentReportResult>> CommandAsync(IncidentReportCommandInput input, bool requiresRowVersion, Func<long, Task<IncidentReportAggregate>> action,
			[CallerMemberName] string command = null)
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
				var replayed = await _idempotency.TryGetRecordIdAsync(DepartmentId, UserId, key, command);
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
					await _idempotency.RememberAsync(DepartmentId, UserId, key, command, input.ReportId);
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

			if (!await CanViewRecordAsync(id))
			{
				await _incidentReports.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Denied, AccessPurpose(), IpAddressHelper.GetRequestIP(Request, true));
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
			var current = await LoadAuthorizedAsync(reportId, true);
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
					Current = IncidentReportsApiMapper.ToReport(current, await _neris.IsSubmissionEnabledAsync(DepartmentId), await CanViewRestrictedAsync())
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
			// The progressive section rules and the analysis link come from the server, so a client renders exactly
			// what validation will enforce and never keeps its own copy of either.
			var sections = await _incidentReports.GetSectionRequirementsAsync(DepartmentId, aggregate.Report.RmsIncidentReportId);
			var analysis = await _analysis.GetForReportAsync(DepartmentId, aggregate.Report.RmsIncidentReportId);

			var data = IncidentReportsApiMapper.ToReport(aggregate, await _neris.IsSubmissionEnabledAsync(DepartmentId),
				await CanViewRestrictedAsync(), sections, analysis?.Analysis?.RmsIncidentAnalysisId);

			var result = new IncidentReportResult { Data = data, PageSize = 1, Status = status };
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, aggregate.Report.RowVersion);
			return result;
		}

		#endregion

        private async Task<bool> CanViewRestrictedAsync() => ClaimsAuthorizationHelper.CanViewRestrictedRecords()
            && await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);
	}
}
