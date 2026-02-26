using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Workflows;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Workflow engine management — create, edit, monitor, and manage department workflows.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class WorkflowsController : V4AuthenticatedApiControllerbase
	{
		private readonly IWorkflowService _workflowService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IPermissionsService _permissionsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ISubscriptionsService _subscriptionsService;

		public WorkflowsController(IWorkflowService workflowService, IDepartmentsService departmentsService,
			IPermissionsService permissionsService, IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService, ISubscriptionsService subscriptionsService)
		{
			_workflowService    = workflowService;
			_departmentsService = departmentsService;
			_permissionsService = permissionsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_subscriptionsService = subscriptionsService;
		}

		// ── Workflows ─────────────────────────────────────────────────────────────────

		/// <summary>Gets all workflows for the current department.</summary>
		[HttpGet("GetAll")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Workflow_View)]
		public async Task<ActionResult<GetWorkflowsResult>> GetAll(CancellationToken ct)
		{
			var workflows = await _workflowService.GetWorkflowsByDepartmentIdAsync(DepartmentId, ct);
			var result = new GetWorkflowsResult
			{
				Data = workflows.Select(MapWorkflowSummary).ToList()
			};
			return Ok(result);
		}

		/// <summary>Gets a single workflow by ID (with steps).</summary>
		[HttpGet("GetById/{workflowId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Workflow_View)]
		public async Task<ActionResult<WorkflowDetailResult>> GetById(string workflowId, CancellationToken ct)
		{
			var workflow = await _workflowService.GetWorkflowByIdAsync(workflowId, ct);
			if (workflow == null || workflow.DepartmentId != DepartmentId) return NotFound();

			var steps = await _workflowService.GetStepsByWorkflowIdAsync(workflowId, ct);
			return Ok(new WorkflowDetailResult { Workflow = MapWorkflowDetail(workflow, steps) });
		}

		/// <summary>Creates or updates a workflow.</summary>
		[HttpPost("Save")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Workflow_Create)]
		public async Task<ActionResult<WorkflowDetailResult>> Save([FromBody] SaveWorkflowInput input, CancellationToken ct)
		{
			if (!ModelState.IsValid) return BadRequest(ModelState);

			if (!await CanUserManageWorkflowsAsync()) return Forbid();

			// Enforce per-plan workflow count cap for new workflows
			bool isNewWorkflow = string.IsNullOrWhiteSpace(input.WorkflowId);
			if (isNewWorkflow)
			{
				var plan      = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
				var isFreePlan = plan?.IsFree ?? false;
				if (!await _workflowService.CanAddWorkflowAsync(DepartmentId, isFreePlan, ct))
					return UnprocessableEntity(new { error = "Workflow limit reached for your plan. Please upgrade to add more workflows." });
			}

			var workflow = new Workflow
			{
				WorkflowId         = input.WorkflowId,
				DepartmentId       = DepartmentId,
				Name               = input.Name,
				Description        = input.Description,
				TriggerEventType   = input.TriggerEventType,
				IsEnabled          = input.IsEnabled,
				MaxRetryCount      = input.MaxRetryCount > 0 ? input.MaxRetryCount : 3,
				RetryBackoffBaseSeconds = input.RetryBackoffBaseSeconds > 0 ? input.RetryBackoffBaseSeconds : 5,
				CreatedByUserId    = UserId
			};

			workflow = await _workflowService.SaveWorkflowAsync(workflow, ct);
			var steps = await _workflowService.GetStepsByWorkflowIdAsync(workflow.WorkflowId, ct);
			return Ok(new WorkflowDetailResult { Workflow = MapWorkflowDetail(workflow, steps) });
		}

		/// <summary>Deletes a workflow.</summary>
		[HttpDelete("Delete/{workflowId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Workflow_Delete)]
		public async Task<ActionResult<DeleteWorkflowResult>> Delete(string workflowId, CancellationToken ct)
		{
			var workflow = await _workflowService.GetWorkflowByIdAsync(workflowId, ct);
			if (workflow == null || workflow.DepartmentId != DepartmentId) return NotFound();

			if (!await CanUserManageWorkflowsAsync()) return Forbid();

			await _workflowService.DeleteWorkflowAsync(workflowId, ct);
			return Ok(new DeleteWorkflowResult { Success = true });
		}

		// ── Steps ─────────────────────────────────────────────────────────────────────

		/// <summary>Saves (creates or updates) a workflow step.</summary>
		[HttpPost("SaveStep")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Workflow_Create)]
		public async Task<ActionResult<WorkflowStepResult>> SaveStep([FromBody] SaveWorkflowStepInput input, CancellationToken ct)
		{
			if (!ModelState.IsValid) return BadRequest(ModelState);

			var workflow = await _workflowService.GetWorkflowByIdAsync(input.WorkflowId, ct);
			if (workflow == null || workflow.DepartmentId != DepartmentId) return Forbid();

			if (!await CanUserManageWorkflowsAsync()) return Forbid();

			// Enforce per-plan step count cap for new steps
			bool isNewStep = string.IsNullOrWhiteSpace(input.WorkflowStepId);
			if (isNewStep)
			{
				var plan       = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
				var isFreePlan = plan?.IsFree ?? false;
				if (!await _workflowService.CanAddStepAsync(input.WorkflowId, isFreePlan, ct))
					return UnprocessableEntity(new { error = "Step limit reached for your plan. Please upgrade to add more steps." });
			}

			var step = new WorkflowStep
			{
				WorkflowStepId       = input.WorkflowStepId,
				WorkflowId           = input.WorkflowId,
				ActionType           = input.ActionType,
				StepOrder            = input.StepOrder,
				OutputTemplate       = input.OutputTemplate,
				ActionConfig         = input.ActionConfig,
				WorkflowCredentialId = input.WorkflowCredentialId,
				IsEnabled            = input.IsEnabled
			};

			step = await _workflowService.SaveWorkflowStepAsync(step, ct);
			return Ok(new WorkflowStepResult { Step = MapStep(step) });
		}

		/// <summary>Deletes a workflow step.</summary>
		[HttpDelete("DeleteStep/{stepId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Workflow_Delete)]
		public async Task<ActionResult<DeleteWorkflowResult>> DeleteStep(string stepId, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync()) return Forbid();

			await _workflowService.DeleteWorkflowStepAsync(stepId, ct);
			return Ok(new DeleteWorkflowResult { Success = true });
		}

		// ── Credentials ───────────────────────────────────────────────────────────────

		/// <summary>Gets all credentials for the department (names/types only — no encrypted data exposed).</summary>
		[HttpGet("GetCredentials")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowCredential_View)]
		public async Task<ActionResult<GetCredentialsResult>> GetCredentials(CancellationToken ct)
		{
			var creds = await _workflowService.GetCredentialsByDepartmentIdAsync(DepartmentId, ct);
			return Ok(new GetCredentialsResult
			{
				Data = creds.Select(c => new CredentialSummaryData
				{
					WorkflowCredentialId = c.WorkflowCredentialId,
					Name                 = c.Name,
					CredentialType       = c.CredentialType,
					CreatedOn            = c.CreatedOn
				}).ToList()
			});
		}

		/// <summary>Saves a credential (plaintext JSON is encrypted server-side).</summary>
		[HttpPost("SaveCredential")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Create)]
		public async Task<ActionResult<SaveCredentialResult>> SaveCredential([FromBody] SaveCredentialInput input, CancellationToken ct)
		{
			if (!ModelState.IsValid) return BadRequest(ModelState);

			if (!await CanUserManageWorkflowCredentialsAsync()) return Forbid();

			if (!string.IsNullOrWhiteSpace(input.WorkflowCredentialId))
			{
				var existing = await _workflowService.GetCredentialByIdAsync(input.WorkflowCredentialId, ct);
				if (existing == null || existing.DepartmentId != DepartmentId) return NotFound();
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var cred = new WorkflowCredential
			{
				WorkflowCredentialId = input.WorkflowCredentialId,
				DepartmentId         = DepartmentId,
				Name                 = input.Name,
				CredentialType       = input.CredentialType,
				EncryptedData        = input.PlaintextCredentialJson,
				CreatedByUserId      = UserId
			};

			cred = await _workflowService.SaveCredentialAsync(cred, department?.Code ?? string.Empty, ct);
			return Ok(new SaveCredentialResult { WorkflowCredentialId = cred.WorkflowCredentialId });
		}

		/// <summary>Deletes a credential.</summary>
		[HttpDelete("DeleteCredential/{credentialId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Delete)]
		public async Task<ActionResult<DeleteWorkflowResult>> DeleteCredential(string credentialId, CancellationToken ct)
		{
			var cred = await _workflowService.GetCredentialByIdAsync(credentialId, ct);
			if (cred == null || cred.DepartmentId != DepartmentId) return NotFound();

			if (!await CanUserManageWorkflowCredentialsAsync()) return Forbid();

			await _workflowService.DeleteCredentialAsync(credentialId, ct);
			return Ok(new DeleteWorkflowResult { Success = true });
		}

		// ── Monitoring & Audit ────────────────────────────────────────────────────────

		/// <summary>Gets paginated workflow runs for the department.</summary>
		[HttpGet("GetRuns")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<ActionResult<GetWorkflowRunsResult>> GetRuns([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
		{
			if (!await CanUserViewWorkflowRunsAsync()) return Forbid();

			var runs = await _workflowService.GetRunsByDepartmentIdAsync(DepartmentId, page, pageSize, ct);
			return Ok(new GetWorkflowRunsResult { Data = runs.Select(MapRun).ToList() });
		}

		/// <summary>Gets runs for a specific workflow (paginated).</summary>
		[HttpGet("GetRunsByWorkflow/{workflowId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<ActionResult<GetWorkflowRunsResult>> GetRunsByWorkflow(string workflowId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
		{
			var workflow = await _workflowService.GetWorkflowByIdAsync(workflowId, ct);
			if (workflow == null || workflow.DepartmentId != DepartmentId) return NotFound();

			if (!await CanUserViewWorkflowRunsAsync()) return Forbid();

			var runs = await _workflowService.GetRunsByWorkflowIdAsync(workflowId, page, pageSize, ct);
			return Ok(new GetWorkflowRunsResult { Data = runs.Select(MapRun).ToList() });
		}

		/// <summary>Gets all pending/running workflow runs for the department (live monitor view).</summary>
		[HttpGet("GetPendingRuns")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<ActionResult<GetWorkflowRunsResult>> GetPendingRuns(CancellationToken ct)
		{
			if (!await CanUserViewWorkflowRunsAsync()) return Forbid();

			var runs = await _workflowService.GetPendingAndRunningRunsByDepartmentIdAsync(DepartmentId, ct);
			return Ok(new GetWorkflowRunsResult { Data = runs.Select(MapRun).ToList() });
		}

		/// <summary>Gets the run logs for a specific workflow run.</summary>
		[HttpGet("GetRunLogs/{runId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<ActionResult<GetWorkflowRunLogsResult>> GetRunLogs(string runId, CancellationToken ct)
		{
			var run = await _workflowService.GetWorkflowRunByIdAsync(runId, ct);
			if (run == null || run.DepartmentId != DepartmentId) return NotFound();

			if (!await CanUserViewWorkflowRunsAsync()) return Forbid();

			var logs = await _workflowService.GetLogsForRunAsync(runId, ct);
			return Ok(new GetWorkflowRunLogsResult { Data = logs.Select(MapLog).ToList() });
		}

		/// <summary>Gets health summary for a specific workflow.</summary>
		[HttpGet("GetHealth/{workflowId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<ActionResult<WorkflowHealthResult>> GetHealth(string workflowId, CancellationToken ct)
		{
			var workflow = await _workflowService.GetWorkflowByIdAsync(workflowId, ct);
			if (workflow == null || workflow.DepartmentId != DepartmentId) return NotFound();

			if (!await CanUserViewWorkflowRunsAsync()) return Forbid();

			var health = await _workflowService.GetWorkflowHealthAsync(workflowId, ct);
			return Ok(new WorkflowHealthResult { Health = MapHealth(health) });
		}

		/// <summary>Cancels a specific workflow run.</summary>
		[HttpPost("CancelRun/{runId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowRun_Delete)]
		public async Task<ActionResult<DeleteWorkflowResult>> CancelRun(string runId, CancellationToken ct)
		{
			var run = await _workflowService.GetWorkflowRunByIdAsync(runId, ct);
			if (run == null || run.DepartmentId != DepartmentId) return NotFound();

			if (!await CanUserViewWorkflowRunsAsync()) return Forbid();

			var cancelled = await _workflowService.CancelWorkflowRunAsync(runId, ct);
			return Ok(new DeleteWorkflowResult { Success = cancelled });
		}

		/// <summary>Clears all pending/queued runs for the department.</summary>
		[HttpPost("ClearPending")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowRun_Delete)]
		public async Task<ActionResult<DeleteWorkflowResult>> ClearPending(CancellationToken ct)
		{
			if (!await CanUserViewWorkflowRunsAsync()) return Forbid();

			await _workflowService.ClearPendingRunsAsync(DepartmentId, ct);
			return Ok(new DeleteWorkflowResult { Success = true });
		}

		/// <summary>Gets all template variables available for a given trigger event type.</summary>
		[HttpGet("GetTemplateVariables/{triggerEventType:int}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Workflow_View)]
		public ActionResult<GetTemplateVariablesResult> GetTemplateVariables(int triggerEventType)
		{
			var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog((WorkflowTriggerEventType)triggerEventType);
			return Ok(new GetTemplateVariablesResult
			{
				Data = catalog.Select(v => new TemplateVariableData
				{
					Name        = v.Name,
					Description = v.Description,
					DataType    = v.DataType,
					IsCommon    = v.IsCommon
				}).ToList()
			});
		}

		// ── Private Permission Helpers ────────────────────────────────────────────────

		private async Task<bool> CanUserManageWorkflowsAsync()
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateWorkflow);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			bool isGroupAdmin = group != null && group.IsUserGroupAdmin(UserId);
			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(UserId), isGroupAdmin, roles);
		}

		private async Task<bool> CanUserManageWorkflowCredentialsAsync()
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ManageWorkflowCredentials);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			bool isGroupAdmin = group != null && group.IsUserGroupAdmin(UserId);
			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(UserId), isGroupAdmin, roles);
		}

		private async Task<bool> CanUserViewWorkflowRunsAsync()
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ViewWorkflowRuns);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			bool isGroupAdmin = group != null && group.IsUserGroupAdmin(UserId);
			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(UserId), isGroupAdmin, roles);
		}

		// ── Private Mappers ───────────────────────────────────────────────────────────

		private static WorkflowSummaryData MapWorkflowSummary(Workflow w) => new WorkflowSummaryData
		{
			WorkflowId       = w.WorkflowId,
			Name             = w.Name,
			Description      = w.Description,
			TriggerEventType = w.TriggerEventType,
			IsEnabled        = w.IsEnabled,
			CreatedOn        = w.CreatedOn,
			UpdatedOn        = w.UpdatedOn
		};

		private static WorkflowDetailData MapWorkflowDetail(Workflow w, List<WorkflowStep> steps) => new WorkflowDetailData
		{
			WorkflowId              = w.WorkflowId,
			Name                    = w.Name,
			Description             = w.Description,
			TriggerEventType        = w.TriggerEventType,
			IsEnabled               = w.IsEnabled,
			MaxRetryCount           = w.MaxRetryCount,
			RetryBackoffBaseSeconds = w.RetryBackoffBaseSeconds,
			CreatedOn               = w.CreatedOn,
			UpdatedOn               = w.UpdatedOn,
			Steps                   = steps.Select(MapStep).ToList()
		};

		private static WorkflowStepData MapStep(WorkflowStep s) => new WorkflowStepData
		{
			WorkflowStepId       = s.WorkflowStepId,
			WorkflowId           = s.WorkflowId,
			ActionType           = s.ActionType,
			StepOrder            = s.StepOrder,
			OutputTemplate       = s.OutputTemplate,
			ActionConfig         = s.ActionConfig,
			WorkflowCredentialId = s.WorkflowCredentialId,
			IsEnabled            = s.IsEnabled
		};

		private static WorkflowRunData MapRun(WorkflowRun r) => new WorkflowRunData
		{
			WorkflowRunId    = r.WorkflowRunId,
			WorkflowId       = r.WorkflowId,
			Status           = r.Status,
			StatusText       = ((WorkflowRunStatus)r.Status).ToString(),
			TriggerEventType = r.TriggerEventType,
			StartedOn        = r.StartedOn,
			CompletedOn      = r.CompletedOn,
			ErrorMessage     = r.ErrorMessage,
			AttemptNumber    = r.AttemptNumber
		};

		private static WorkflowRunLogData MapLog(WorkflowRunLog l) => new WorkflowRunLogData
		{
			WorkflowRunLogId = l.WorkflowRunLogId,
			WorkflowRunId    = l.WorkflowRunId,
			WorkflowStepId   = l.WorkflowStepId,
			Status           = l.Status,
			StatusText       = ((WorkflowRunStatus)l.Status).ToString(),
			RenderedOutput   = l.RenderedOutput,
			ActionResult     = l.ActionResult,
			ErrorMessage     = l.ErrorMessage,
			StartedOn        = l.StartedOn,
			CompletedOn      = l.CompletedOn,
			DurationMs       = l.DurationMs
		};

		private static WorkflowHealthData MapHealth(WorkflowHealthSummary h) => new WorkflowHealthData
		{
			WorkflowId           = h.WorkflowId,
			WorkflowName         = h.WorkflowName,
			TotalRuns24h         = h.TotalRuns24h,
			SuccessfulRuns24h    = h.SuccessfulRuns24h,
			FailedRuns24h        = h.FailedRuns24h,
			TotalRuns7d          = h.TotalRuns7d,
			SuccessfulRuns7d     = h.SuccessfulRuns7d,
			FailedRuns7d         = h.FailedRuns7d,
			TotalRuns30d         = h.TotalRuns30d,
			SuccessfulRuns30d    = h.SuccessfulRuns30d,
			FailedRuns30d        = h.FailedRuns30d,
			SuccessRatePercent30d = h.SuccessRatePercent30d,
			AverageDurationMs30d = h.AverageDurationMs30d,
			LastRunOn            = h.LastRunOn,
			LastRunStatus        = h.LastRunStatus?.ToString(),
			LastErrorMessage     = h.LastErrorMessage
		};
	}
}











