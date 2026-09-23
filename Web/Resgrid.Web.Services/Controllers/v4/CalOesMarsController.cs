using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.CostRecovery.CalOesMars;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Cal OES MARS cost recovery (Workforce &amp; Business Operations plan, C5). Gated by the CostRecovery.CalOesMars
	/// entitlement. Rostered personnel get / save / validate their own incident-bound F-42 and expense drafts and see
	/// them in the queue; MutualAidReimbursement_View exposes the department queue and readiness metadata,
	/// _Update the expected-reimbursement run, _Submit the manual external observations. Agency identifiers, annual
	/// rate inputs, the attested handoff and invoice decisions stay MVC-only in P0. Nothing here writes to MARS.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class CalOesMarsController : V4AuthenticatedApiControllerbase
	{
		private readonly ICalOesMarsService _mars;
		private readonly IDeploymentService _deployments;
		private readonly IBusinessOperationsAccessService _access;
		private readonly ICalOesMarsExternalGateway _gateway;

		public CalOesMarsController(ICalOesMarsService mars, IDeploymentService deployments, IBusinessOperationsAccessService access, ICalOesMarsExternalGateway gateway)
		{
			_mars = mars;
			_deployments = deployments;
			_access = access;
			_gateway = gateway;
		}

		private Task<bool> EnabledAsync() => _access.CanUseCostRecoveryAsync(DepartmentId);
		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanManage => IsAdmin || ClaimsAuthorizationHelper.CanManageMutualAidReimbursement();
		private static bool CanView => CanManage || ClaimsAuthorizationHelper.CanViewMutualAidReimbursement();
		private static bool CanSubmit => IsAdmin || ClaimsAuthorizationHelper.CanSubmitMutualAidReimbursement();
		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("calmars_", StringComparison.Ordinal) || ex.Message.StartsWith("deployments_", StringComparison.Ordinal);

		private ActionResult<T> Failed<T>(string reason, int status = StatusCodes.Status400BadRequest) where T : StandardApiResponseV4Base, new()
		{
			var failed = new T { PageSize = 0, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(failed);
			Response.Headers["X-Resgrid-Reason"] = reason;
			return StatusCode(status, failed);
		}

		private async Task<bool> CanTouchAsync(string workItemId) => CanManage || await _mars.IsRosteredForWorkItemAsync(workItemId, DepartmentId, UserId);
		private async Task<bool> CanSeeAsync(string workItemId) => CanView || await _mars.IsRosteredForWorkItemAsync(workItemId, DepartmentId, UserId);

		[HttpGet("GetAccess")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsAccessResult>> GetAccess()
		{
			var result = new CalOesMarsAccessResult
			{
				Data = new CalOesMarsAccessData
				{
					Enabled = await EnabledAsync(), CanView = CanView, CanManage = CanManage, CanSubmit = CanSubmit, CanReconcile = IsAdmin || ClaimsAuthorizationHelper.CanReconcileMutualAidReimbursement(),
					AuthorityProfileCode = CalOesMarsAuthorityProfile.Current.Code, PortalUrl = _gateway.GetPortalUrl(null)
				},
				PageSize = 1, Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetReadiness")]
		[Authorize(Policy = ResgridResources.MutualAidReimbursement_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsReadinessResult>> GetReadiness(DateTime? asOf = null)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsReadinessResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			var r = await _mars.GetAgencyReadinessAsync(DepartmentId, asOf);
			// Readiness reports presence of the agency identifiers, never their values (plan C5).
			var result = new CalOesMarsReadinessResult
			{
				Data = new CalOesMarsReadinessData
				{
					AsOf = r.AsOf, AuthorityProfileCode = r.AuthorityProfileCode, AuthorityProfileCurrent = r.AuthorityProfileCurrent, IsReady = r.IsReady, HasAgencyProfile = r.Agency != null, MacsDesignator = r.Agency?.MacsDesignator,
					HasFein = !string.IsNullOrWhiteSpace(r.Agency?.FeinReference), HasUei = !string.IsNullOrWhiteSpace(r.Agency?.UeiReference), HasFiscalSupplier = !string.IsNullOrWhiteSpace(r.Agency?.FiscalSupplierReference), AgencyVerifiedOn = r.Agency?.VerifiedOn,
					ResourceProfiles = r.ResourceProfiles, ResourceMismatches = r.ResourceMismatches, CurrentRateProfiles = r.CurrentRateProfiles.Count, CurrentAgreements = r.CurrentAgreements.Count,
					OpenWorkItems = r.OpenWorkItems, ReturnedWorkItems = r.ReturnedWorkItems, InvoicesAwaitingLocalApproval = r.InvoicesAwaitingLocalApproval,
					Items = r.Items.Select(i => new CalOesMarsReadinessItemData { Key = i.Key, Severity = i.Severity, MessageKey = i.MessageKey, Detail = i.Detail, Area = i.Area }).ToList()
				},
				PageSize = 1, Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetQueue")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsQueueResult>> GetQueue(int? recordType = null)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsQueueResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			var items = await _mars.GetActionQueueAsync(DepartmentId, UserId, CanView);
			if (recordType.HasValue) items = items.Where(i => i.WorkItem.RecordType == recordType.Value).ToList();
			var result = new CalOesMarsQueueResult { Data = items.Select(MapQueue).ToList(), PageSize = items.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetWorkItem")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsWorkItemResult>> GetWorkItem(string id)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsWorkItemResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			if (!await CanSeeAsync(id)) return Unauthorized();
			var item = await _mars.GetWorkItemAsync(id, DepartmentId);
			if (item == null) return NotFound();
			var result = new CalOesMarsWorkItemResult { Data = Map(item), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("BuildF42")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsWorkItemResult>> BuildF42([FromBody] BuildF42Input input)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsWorkItemResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.DeploymentId)) return Failed<CalOesMarsWorkItemResult>("calmars_deployment_required");
			if (!CanManage && !await _deployments.CanFieldMemberSeeAsync(input.DeploymentId, DepartmentId, UserId)) return Unauthorized();
			try
			{
				var item = await _mars.BuildF42DraftAsync(input.DeploymentId, DepartmentId, input.RmsExternalOrderFillId, UserId, Ip, Agent);
				var result = new CalOesMarsWorkItemResult { Data = Map(item), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<CalOesMarsWorkItemResult>(ex.Message); }
		}

		[HttpPost("BuildExpenseClaim")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsWorkItemResult>> BuildExpenseClaim([FromBody] BuildExpenseClaimInput input)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsWorkItemResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.DeploymentId)) return Failed<CalOesMarsWorkItemResult>("calmars_deployment_required");
			if (!CanManage && !await _deployments.CanFieldMemberSeeAsync(input.DeploymentId, DepartmentId, UserId)) return Unauthorized();
			try
			{
				var item = await _mars.BuildExpenseClaimDraftAsync(input.DeploymentId, DepartmentId, input.F42WorkItemId, UserId, Ip, Agent);
				var result = new CalOesMarsWorkItemResult { Data = Map(item), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<CalOesMarsWorkItemResult>(ex.Message); }
		}

		[HttpPost("SaveF42")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsWorkItemResult>> SaveF42([FromBody] SaveF42Input input)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsWorkItemResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.WorkItemId) || input.Snapshot == null) return Failed<CalOesMarsWorkItemResult>("calmars_work_item_not_found");
			if (!await CanTouchAsync(input.WorkItemId)) return Unauthorized();
			try
			{
				var item = await _mars.SaveF42SnapshotAsync(input.WorkItemId, DepartmentId, input.Snapshot, UserId, Ip, Agent);
				var result = new CalOesMarsWorkItemResult { Data = Map(item), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<CalOesMarsWorkItemResult>(ex.Message); }
		}

		[HttpPost("SaveExpenseClaim")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsWorkItemResult>> SaveExpenseClaim([FromBody] SaveExpenseClaimInput input)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsWorkItemResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.WorkItemId) || input.Snapshot == null) return Failed<CalOesMarsWorkItemResult>("calmars_work_item_not_found");
			if (!await CanTouchAsync(input.WorkItemId)) return Unauthorized();
			try
			{
				var item = await _mars.SaveExpenseSnapshotAsync(input.WorkItemId, DepartmentId, input.Snapshot, UserId, Ip, Agent);
				var result = new CalOesMarsWorkItemResult { Data = Map(item), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<CalOesMarsWorkItemResult>(ex.Message); }
		}

		[HttpPost("Validate")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsValidationApiResult>> Validate(string id)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsValidationApiResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			if (!await CanTouchAsync(id)) return Unauthorized();
			try
			{
				var validation = await _mars.ValidateForPortalAsync(id, DepartmentId, UserId, Ip, Agent);
				var result = new CalOesMarsValidationApiResult { Data = validation, PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<CalOesMarsValidationApiResult>(ex.Message); }
		}

		[HttpPost("CalculateExpectedReimbursement")]
		[Authorize(Policy = ResgridResources.MutualAidReimbursement_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsReimbursementApiResult>> CalculateExpectedReimbursement(string id)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsReimbursementApiResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			try
			{
				var calc = await _mars.CalculateExpectedReimbursementAsync(id, DepartmentId, UserId, Ip, Agent);
				var result = new CalOesMarsReimbursementApiResult
				{
					Data = new CalOesMarsReimbursementData { ExpectedTotal = calc.ExpectedTotal, UncertainTotal = calc.UncertainTotal, Lines = calc.Lines.Select(MapLine).ToList(), Exceptions = calc.Exceptions },
					PageSize = 1, Status = ResponseHelper.Success
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<CalOesMarsReimbursementApiResult>(ex.Message); }
		}

		[HttpPost("RecordExternalSubmission")]
		[Authorize(Policy = ResgridResources.MutualAidReimbursement_Submit)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsWorkItemResult>> RecordExternalSubmission([FromBody] CalOesMarsObservationInput input)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsWorkItemResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.WorkItemId)) return Failed<CalOesMarsWorkItemResult>("calmars_work_item_not_found");
			try
			{
				var item = await _mars.RecordExternalSubmissionAsync(input.WorkItemId, DepartmentId, ToObservation(input), UserId, Ip, Agent);
				var result = new CalOesMarsWorkItemResult { Data = Map(item), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<CalOesMarsWorkItemResult>(ex.Message); }
		}

		[HttpPost("RecordExternalStatus")]
		[Authorize(Policy = ResgridResources.MutualAidReimbursement_Submit)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CalOesMarsWorkItemResult>> RecordExternalStatus([FromBody] CalOesMarsObservationInput input)
		{
			if (!await EnabledAsync()) return Failed<CalOesMarsWorkItemResult>("cost_recovery_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.WorkItemId)) return Failed<CalOesMarsWorkItemResult>("calmars_work_item_not_found");
			try
			{
				var item = await _mars.RecordExternalStatusAsync(input.WorkItemId, DepartmentId, ToObservation(input), UserId, Ip, Agent);
				var result = new CalOesMarsWorkItemResult { Data = Map(item), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<CalOesMarsWorkItemResult>(ex.Message); }
		}

		#region Mapping

		private static CalOesMarsExternalObservation ToObservation(CalOesMarsObservationInput input) => new CalOesMarsExternalObservation { ExternalId = input.ExternalId, ExternalStatus = input.ExternalStatus, ObservedOn = input.ObservedOn, Comment = input.Comment, ArtifactChecksum = input.ArtifactChecksum };

		private static CalOesMarsQueueItemData MapQueue(CalOesMarsQueueItem q) => new CalOesMarsQueueItemData
		{
			Id = q.WorkItem.CalOesMarsWorkItemId, DeploymentId = q.WorkItem.DeploymentId, DeploymentName = q.DeploymentName, RecordType = q.WorkItem.RecordType, RecordTypeName = ((CalOesMarsRecordTypes)q.WorkItem.RecordType).ToString(),
			LocalState = q.WorkItem.LocalState, LocalStateName = ((CalOesMarsLocalStates)q.WorkItem.LocalState).ToString(), IncidentNumber = q.IncidentNumber, RequestNumber = q.RequestNumber, MarsRecordId = q.WorkItem.MarsRecordId,
			ObservedExternalStatus = q.WorkItem.ObservedExternalStatus, ObservedOn = q.WorkItem.ObservedOn, ErrorCount = q.ErrorCount, WarningCount = q.WarningCount, AgeDays = q.AgeDays, IsMine = q.IsMine,
			ExpectedTotal = CanView ? q.WorkItem.ExpectedTotal : null, AddedOn = q.WorkItem.AddedOn, UpdatedOn = q.WorkItem.EditedOn ?? q.WorkItem.AddedOn
		};

		private static CalOesMarsWorkItemData Map(CalOesMarsWorkItem w) => new CalOesMarsWorkItemData
		{
			Id = w.CalOesMarsWorkItemId, DeploymentId = w.DeploymentId, DeploymentName = w.DeploymentName, RmsExternalOrderId = w.RmsExternalOrderId, RmsExternalOrderFillId = w.RmsExternalOrderFillId,
			RecordType = w.RecordType, RecordTypeName = ((CalOesMarsRecordTypes)w.RecordType).ToString(), LocalState = w.LocalState, LocalStateName = ((CalOesMarsLocalStates)w.LocalState).ToString(),
			MarsRecordId = w.MarsRecordId, ObservedExternalStatus = w.ObservedExternalStatus, ObservedOn = w.ObservedOn, CorrectionComment = w.CorrectionComment, AuthorityProfileCode = w.AuthorityProfileCode,
			IsLocallyEditable = w.IsLocallyEditable, IsExternal = w.IsExternal, SupersedesWorkItemId = w.SupersedesWorkItemId,
			F42 = w.RecordType == (int)CalOesMarsRecordTypes.F42 ? Resgrid.Services.CostRecovery.CalOesMarsService.Deserialize<CalOesMarsF42Snapshot>(w.SnapshotJson) : null,
			ExpenseClaim = w.RecordType == (int)CalOesMarsRecordTypes.ExpenseClaim ? Resgrid.Services.CostRecovery.CalOesMarsService.Deserialize<CalOesMarsExpenseClaimSnapshot>(w.SnapshotJson) : null,
			Validation = Resgrid.Services.CostRecovery.CalOesMarsService.Deserialize<CalOesMarsValidationResult>(w.ValidationSummaryJson),
			ExpectedTotal = CanView ? w.ExpectedTotal : null, Lines = CanView ? w.Lines.Select(MapLine).ToList() : new List<CalOesMarsLineData>(),
			RowVersion = w.RowVersion, AddedOn = w.AddedOn, UpdatedOn = w.EditedOn ?? w.AddedOn
		};

		private static CalOesMarsLineData MapLine(CalOesMarsReimbursementLine l) => new CalOesMarsLineData
		{
			Id = l.CalOesMarsReimbursementLineId, LineKind = l.LineKind, LineKindName = ((CalOesMarsLineKinds)l.LineKind).ToString(), SubjectId = l.SubjectId, SubjectName = l.SubjectName, LineDate = l.LineDate,
			Quantity = l.Quantity, Unit = l.Unit, Rate = l.Rate, ExpectedAmount = l.ExpectedAmount, ApprovedAmount = l.ApprovedAmount, PaidAmount = l.PaidAmount, EligibilityState = l.EligibilityState, EligibilityReason = l.EligibilityReason
		};

		#endregion
	}
}
