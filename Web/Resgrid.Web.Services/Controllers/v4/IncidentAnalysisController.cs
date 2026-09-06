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
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The NERIS incident-analysis filing (RMS-3): the fire/hazmat investigation the contract posts separately from
	/// the incident, once the destination already holds it.
	/// <para>
	/// It is a second submittable artifact for the same incident rather than a section of it, so it has its own
	/// endpoints, its own ETag and its own lifecycle — an analysis that will not validate must never block the
	/// incident report. Visibility is inherited from the incident: an analysis is never visible to somebody who
	/// cannot see the report it belongs to.
	/// </para>
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IncidentAnalysisController : V4AuthenticatedApiControllerbase, IActionFilter
	{
		private readonly IIncidentAnalysisService _analysis;
		private readonly IIncidentReportsService _incidentReports;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IRecordsApiIdempotencyService _idempotency;

		private SystemPrincipalRecordGrant _systemGrant;
		private bool _systemGrantResolved;

		public IncidentAnalysisController(IIncidentAnalysisService analysis, IIncidentReportsService incidentReports, IRecordsCutoverService cutoverService,
			IRecordsAuthorizationService recordsAuthorizationService, IFeatureToggleService featureToggleService, IRecordsApiIdempotencyService idempotency)
		{
			_analysis = analysis;
			_incidentReports = incidentReports;
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

		#region Reads

		/// <summary>The analysis by its own id.</summary>
		[HttpGet("GetIncidentAnalysis")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<IncidentAnalysisResult>> GetIncidentAnalysis(string id)
		{
			if (!await FlagOnAsync())
				return NotFound();

			var aggregate = await _analysis.GetAsync(DepartmentId, id, true);
			if (aggregate?.Analysis == null || !await CanViewReportAsync(aggregate.Analysis.IncidentReportId))
				return NotFound();

			await RecordReadAsync(aggregate.Analysis.IncidentReportId);
			return Ok(await WrapAsync(aggregate));
		}

		/// <summary>The analysis for an incident report, if one has been started.</summary>
		[HttpGet("GetForReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<IncidentAnalysisResult>> GetForReport(string reportId)
		{
			if (!await FlagOnAsync())
				return NotFound();
			if (string.IsNullOrWhiteSpace(reportId) || !await CanViewReportAsync(reportId))
				return NotFound();

			var aggregate = await _analysis.GetForReportAsync(DepartmentId, reportId, true);
			if (aggregate?.Analysis == null)
				return NotFound();

			return Ok(await WrapAsync(aggregate));
		}

		#endregion

		#region Authoring

		/// <summary>Starts (or returns) the analysis for a report; one per report, so a second start is a 200.</summary>
		[HttpPost("Start")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<IncidentAnalysisResult>> Start(StartIncidentAnalysisInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentReportId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var origin = RecordsApiHelper.ResolveOrigin(input.OriginClient);
			var gate = await FieldClientGateAsync(origin);
			if (gate != null)
				return gate;

			if (!await CanViewReportAsync(input.IncidentReportId))
				return NotFound();

			try
			{
				var existing = await _analysis.GetForReportAsync(DepartmentId, input.IncidentReportId);
				var aggregate = await _analysis.StartForReportAsync(DepartmentId, UserId, input.IncidentReportId, origin, cancellationToken);
				var result = await WrapAsync(aggregate, existing?.Analysis != null ? ResponseHelper.Success : ResponseHelper.Created);
				return existing?.Analysis != null ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_validation");
			}
		}

		/// <summary>ETag-guarded draft save; 409 with the current analysis on a stale row version.</summary>
		[HttpPost("SaveDraft")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<IncidentAnalysisResult>> SaveDraft(SaveIncidentAnalysisDraftInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.AnalysisId))
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

			var current = await LoadAuthorizedAsync(input.AnalysisId);
			if (current == null)
				return NotFound();
			if (!CanEdit(current))
				return Forbid();

			try
			{
				var saved = await _analysis.SaveDraftAsync(DepartmentId, UserId, input.AnalysisId, rowVersion.Value,
					IncidentAnalysisApiMapper.ToDraftInput(input, origin), await CanViewRestrictedAsync(), cancellationToken);
				return Ok(await WrapAsync(saved, ResponseHelper.Updated));
			}
			catch (RecordConcurrencyException ex)
			{
				return await ConflictAsync(input.AnalysisId, ex.ExpectedRowVersion);
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

		/// <summary>Local validation against the pinned contract; the incident's own issue list is never touched.</summary>
		[HttpPost("Validate")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<IncidentValidationResult>> Validate(IncidentAnalysisCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.AnalysisId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;
			if (await LoadAuthorizedAsync(input.AnalysisId) == null)
				return NotFound();

			try
			{
				var issues = await _analysis.ValidateAsync(DepartmentId, input.AnalysisId, cancellationToken);
				var result = new IncidentValidationResult
				{
					Data = issues.Select(IncidentReportsApiMapper.ToIssue).ToList(),
					HasBlockingIssues = issues.Any(i => i.Severity == (int)RmsValidationSeverity.Error),
					Status = ResponseHelper.Success
				};
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

		/// <summary>
		/// Writes the immutable revision and queues the filing. Finalizing before the incident is at the
		/// destination is allowed: the submission waits for the incident's NERIS id rather than failing.
		/// </summary>
		[HttpPost("Finalize")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Finalize)]
		public Task<ActionResult<IncidentAnalysisResult>> Finalize(IncidentAnalysisCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, true, rowVersion => _analysis.FinalizeAsync(DepartmentId, UserId, input.AnalysisId, rowVersion, cancellationToken));
		}

		/// <summary>Queues (or re-queues) the current revision; used when the incident was filed after the analysis was finalized.</summary>
		[HttpPost("Submit")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public Task<ActionResult<IncidentAnalysisResult>> Submit(IncidentAnalysisCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _analysis.QueueSubmissionAsync(DepartmentId, UserId, input.AnalysisId, cancellationToken));
		}

		[HttpPost("Void")]
		[Consumes(MediaTypeNames.Application.Json)]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public Task<ActionResult<IncidentAnalysisResult>> Void(IncidentAnalysisCommandInput input, CancellationToken cancellationToken)
		{
			return CommandAsync(input, false, _ => _analysis.VoidAsync(DepartmentId, UserId, input.AnalysisId, input.ReasonCode, input.ReasonText, cancellationToken));
		}

		private async Task<ActionResult<IncidentAnalysisResult>> CommandAsync(IncidentAnalysisCommandInput input, bool requiresRowVersion, Func<long, Task<IncidentAnalysisAggregate>> action,
			[CallerMemberName] string command = null)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.AnalysisId))
				return BadRequest();
			var usable = await UsableAsync();
			if (usable != null)
				return usable;

			var origin = RecordsApiHelper.ResolveOrigin(input.OriginClient);
			var gate = await FieldClientGateAsync(origin);
			if (gate != null)
				return gate;

			var current = await LoadAuthorizedAsync(input.AnalysisId);
			if (current == null)
				return NotFound();

			var key = RecordsApiHelper.ResolveIdempotencyKey(input.IdempotencyKey, Request);
			if (key != null)
			{
				var replayed = await _idempotency.TryGetRecordIdAsync(DepartmentId, UserId, key, command);
				if (string.Equals(replayed, input.AnalysisId, StringComparison.Ordinal))
					return Ok(await WrapAsync(await _analysis.GetAsync(DepartmentId, input.AnalysisId, true)));
			}

			long rowVersion = current.Analysis.RowVersion;
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
					await _idempotency.RememberAsync(DepartmentId, UserId, key, command, input.AnalysisId);
				return Ok(await WrapAsync(aggregate, ResponseHelper.Updated));
			}
			catch (RecordConcurrencyException ex)
			{
				return await ConflictAsync(input.AnalysisId, ex.ExpectedRowVersion);
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

		/// <summary>An analysis is only as visible as the incident it belongs to.</summary>
		private async Task<bool> CanViewReportAsync(string reportId)
		{
			if (string.IsNullOrWhiteSpace(reportId))
				return false;

			var grant = SystemGrant;
			if (grant != null)
				return await _recordsAuthorizationService.CanSystemPrincipalViewRecordAsync(grant, reportId);

			return await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, reportId, DepartmentId);
		}

		private async Task<IncidentAnalysisAggregate> LoadAuthorizedAsync(string analysisId, bool includeHistory = false)
		{
			if (string.IsNullOrWhiteSpace(analysisId))
				return null;

			var aggregate = await _analysis.GetAsync(DepartmentId, analysisId, includeHistory);
			if (aggregate?.Analysis == null)
				return null;

			if (!await CanViewReportAsync(aggregate.Analysis.IncidentReportId))
			{
				await RecordDeniedAsync(aggregate.Analysis.IncidentReportId);
				return null;
			}

			return aggregate;
		}

		private bool CanEdit(IncidentAnalysisAggregate aggregate)
		{
			if (!ClaimsAuthorizationHelper.CanCreateRecord())
				return false;

			var analysis = aggregate.Analysis;
			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
				|| string.Equals(analysis.OwnerUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(analysis.AuthorUserId, UserId, StringComparison.OrdinalIgnoreCase);
		}

		private Task RecordReadAsync(string reportId)
		{
			return _incidentReports.RecordAccessAsync(DepartmentId, UserId, reportId, null, RmsAccessAuditAction.Read, AccessPurpose(), IpAddressHelper.GetRequestIP(Request, true));
		}

		private Task RecordDeniedAsync(string reportId)
		{
			return _incidentReports.RecordAccessAsync(DepartmentId, UserId, reportId, null, RmsAccessAuditAction.Denied, AccessPurpose(), IpAddressHelper.GetRequestIP(Request, true));
		}

		private string AccessPurpose(string purpose = null)
		{
			var grant = SystemGrant;
			if (grant == null)
				return purpose;

			return string.IsNullOrWhiteSpace(purpose) ? grant.Purpose : $"{grant.Purpose}: {purpose}";
		}

		private async Task<ActionResult<IncidentAnalysisResult>> ConflictAsync(string analysisId, long expectedRowVersion)
		{
			var current = await _analysis.GetAsync(DepartmentId, analysisId, true);
			if (current?.Analysis == null)
				return NotFound();

			var result = await WrapAsync(current, RecordsController.ConflictStatus);
			result.Data.ETag = RecordsApiContract.ToETag(current.Analysis.RowVersion);
			// The expected row version is what the caller sent; the payload already carries the current one.
			Response.Headers[RecordsApiContract.ETagHeader] = RecordsApiContract.ToETag(current.Analysis.RowVersion);
			return Conflict(result);
		}

		private async Task<IncidentAnalysisResult> WrapAsync(IncidentAnalysisAggregate aggregate, string status = ResponseHelper.Success)
		{
			var result = new IncidentAnalysisResult
			{
				Data = IncidentAnalysisApiMapper.ToAnalysis(aggregate, await CanViewRestrictedAsync()),
				PageSize = 1,
				Status = status
			};
			ResponseHelper.PopulateV4ResponseData(result);
			RecordsApiHelper.SetETag(Response, aggregate.Analysis.RowVersion);
			return result;
		}

		#endregion

        private async Task<bool> CanViewRestrictedAsync() => ClaimsAuthorizationHelper.CanViewRestrictedRecords()
            && await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);
	}
}
