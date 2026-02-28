using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models;
using Resgrid.WebCore.Models;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class WorkflowsController : SecureBaseController
	{
		private readonly IWorkflowService _workflowService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IPermissionsService _permissionsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IAuditService _auditService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IWorkflowTemplateContextBuilder _contextBuilder;

		public WorkflowsController(IWorkflowService workflowService, IDepartmentsService departmentsService,
			IPermissionsService permissionsService, IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService, IAuditService auditService,
			IEventAggregator eventAggregator, ISubscriptionsService subscriptionsService,
			IWorkflowTemplateContextBuilder contextBuilder)
		{
			_workflowService         = workflowService;
			_departmentsService      = departmentsService;
			_permissionsService      = permissionsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService   = personnelRolesService;
			_auditService            = auditService;
			_eventAggregator         = eventAggregator;
			_subscriptionsService    = subscriptionsService;
			_contextBuilder          = contextBuilder;
		}

		// ── Index ─────────────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.Workflow_View)]
		public async Task<IActionResult> Index(CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			var workflows = await _workflowService.GetWorkflowsByDepartmentIdAsync(DepartmentId, ct);
			return View(workflows);
		}

		// ── New / Create ──────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.Workflow_Create)]
		public async Task<IActionResult> New(CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			var usedEventTypes = await _workflowService.GetUsedEventTypesForDepartmentAsync(DepartmentId, ct);
			ViewBag.UsedEventTypes = usedEventTypes;

			return View(new Workflow { MaxRetryCount = 3, RetryBackoffBaseSeconds = 5, IsEnabled = true });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Workflow_Create)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> New(Workflow model, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			// DepartmentId, WorkflowId, and CreatedByUserId are server-assigned values
			// that are not present in the form. Populate them before re-validating so the
			// [Required] attributes on those fields don't cause ModelState to fail.
			model.DepartmentId    = DepartmentId;
			model.CreatedByUserId = UserId;
			model.WorkflowId      = Guid.NewGuid().ToString();

			ModelState.ClearValidationState(nameof(Workflow.WorkflowId));
			ModelState.ClearValidationState(nameof(Workflow.DepartmentId));
			ModelState.ClearValidationState(nameof(Workflow.CreatedByUserId));
			TryValidateModel(model);

			if (!ModelState.IsValid)
			{
				var usedEventTypes = await _workflowService.GetUsedEventTypesForDepartmentAsync(DepartmentId, ct);
				ViewBag.UsedEventTypes = usedEventTypes;
				return View(model);
			}

			// Enforce one workflow per event type per department
			if (await _workflowService.WorkflowExistsForEventTypeAsync(DepartmentId, model.TriggerEventType, ct))
			{
				ModelState.AddModelError(nameof(Workflow.TriggerEventType),
					"A workflow already exists for this trigger event type. Only one workflow per event type is allowed.");
				var usedEventTypes = await _workflowService.GetUsedEventTypesForDepartmentAsync(DepartmentId, ct);
				ViewBag.UsedEventTypes = usedEventTypes;
				return View(model);
			}

			// Enforce plan-based workflow count cap
			var plan      = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
			var isFreePlan = plan?.IsFree ?? false;
			if (!await _workflowService.CanAddWorkflowAsync(DepartmentId, isFreePlan, ct))
			{
				ModelState.AddModelError(string.Empty, "Workflow limit reached for your plan. Please upgrade to add more workflows.");
				var usedEventTypes2 = await _workflowService.GetUsedEventTypesForDepartmentAsync(DepartmentId, ct);
				ViewBag.UsedEventTypes = usedEventTypes2;
				return View(model);
			}

			// Pass a copy without WorkflowId so SaveWorkflowAsync takes the insert path.
			model.WorkflowId = string.Empty;
			var saved = await _workflowService.SaveWorkflowAsync(model, ct);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowAdded,
				After        = JsonSerializer.Serialize(new { saved.WorkflowId, saved.Name, saved.TriggerEventType }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Index");
		}

		// ── Edit ──────────────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.Workflow_Update)]
		public async Task<IActionResult> Edit(string workflowId, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			var workflow = await _workflowService.GetWorkflowByIdAsync(workflowId, ct);
			if (workflow == null || workflow.DepartmentId != DepartmentId)
				return NotFound();

			workflow.Steps = await _workflowService.GetStepsByWorkflowIdAsync(workflowId, ct);

			var credentials = await _workflowService.GetCredentialsByDepartmentIdAsync(DepartmentId, ct);
			ViewBag.CredentialsJson = JsonSerializer.Serialize(
				credentials.Select(c => new { id = c.WorkflowCredentialId, name = c.Name, credentialType = c.CredentialType })
			);

			ViewBag.TriggerEventTypeName = ((WorkflowTriggerEventType)workflow.TriggerEventType).ToString();

			return View(workflow);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Workflow_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(Workflow model, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			if (!ModelState.IsValid)
			{
				ViewBag.TriggerEventTypeName = ((WorkflowTriggerEventType)model.TriggerEventType).ToString();
				return View(model);
			}

			var existing = await _workflowService.GetWorkflowByIdAsync(model.WorkflowId, ct);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return NotFound();

			existing.Name                    = model.Name;
			existing.Description             = model.Description;
			// TriggerEventType is intentionally not updated — it is locked after creation.
			existing.IsEnabled               = model.IsEnabled;
			existing.MaxRetryCount           = model.MaxRetryCount;
			existing.RetryBackoffBaseSeconds = model.RetryBackoffBaseSeconds;

			await _workflowService.SaveWorkflowAsync(existing, ct);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowEdited,
				After        = JsonSerializer.Serialize(new { existing.WorkflowId, existing.Name, existing.TriggerEventType, existing.IsEnabled }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Index");
		}

		// ── Delete ────────────────────────────────────────────────────────────────

		[HttpPost]
		[Authorize(Policy = ResgridResources.Workflow_Delete)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string workflowId, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			var existing = await _workflowService.GetWorkflowByIdAsync(workflowId, ct);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return NotFound();

			await _workflowService.DeleteWorkflowAsync(workflowId, ct);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowDeleted,
				Before       = JsonSerializer.Serialize(new { existing.WorkflowId, existing.Name, existing.TriggerEventType }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Index");
		}

		// ── Steps (AJAX) ──────────────────────────────────────────────────────────

		[HttpPost]
		[Authorize(Policy = ResgridResources.Workflow_Create)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveStep([FromBody] SaveStepRequest request, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return Forbid();

			if (request == null || string.IsNullOrWhiteSpace(request.WorkflowId))
				return BadRequest(new { error = "WorkflowId is required." });

			var workflow = await _workflowService.GetWorkflowByIdAsync(request.WorkflowId, ct);
			if (workflow == null || workflow.DepartmentId != DepartmentId)
				return NotFound(new { error = "Workflow not found." });

			bool isNew = string.IsNullOrWhiteSpace(request.WorkflowStepId);

			// Enforce plan-based step count cap for new steps
			if (isNew)
			{
				var plan       = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
				var isFreePlan = plan?.IsFree ?? false;
				if (!await _workflowService.CanAddStepAsync(request.WorkflowId, isFreePlan, ct))
					return BadRequest(new { error = "Step limit reached for your plan. Please upgrade to add more steps." });
			}

			var step = new WorkflowStep
			{
				WorkflowStepId       = request.WorkflowStepId,
				WorkflowId           = request.WorkflowId,
				ActionType           = request.ActionType,
				StepOrder            = request.StepOrder,
				OutputTemplate       = request.OutputTemplate,
				ActionConfig         = request.ActionConfig,
				WorkflowCredentialId = request.WorkflowCredentialId,
				IsEnabled            = request.IsEnabled,
				ConditionExpression  = request.ConditionExpression
			};

			if (isNew)
				step.CreatedByUserId = UserId;
			else
				step.UpdatedByUserId = UserId;

			step = await _workflowService.SaveWorkflowStepAsync(step, ct);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = isNew ? AuditLogTypes.WorkflowStepAdded : AuditLogTypes.WorkflowStepEdited,
				After        = JsonSerializer.Serialize(new { step.WorkflowStepId, step.WorkflowId, step.ActionType, step.StepOrder }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return Ok(new { workflowStepId = step.WorkflowStepId });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Workflow_Create)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ValidateCondition([FromBody] ValidateConditionWebRequest request, CancellationToken ct)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.ConditionExpression))
				return BadRequest(new { isValid = false, parseErrors = new[] { "ConditionExpression is required." } });

			var parsed = Scriban.Template.Parse(request.ConditionExpression);
			if (parsed.HasErrors)
			{
				return Ok(new
				{
					isValid     = false,
					parseErrors = parsed.Messages.Select(m => m.ToString()).ToList()
				});
			}

			if (!string.IsNullOrWhiteSpace(request.SamplePayloadJson))
			{
				try
				{
					var eventType    = (WorkflowTriggerEventType)request.TriggerEventType;
					var scriptObject = await _contextBuilder.BuildContextAsync(DepartmentId, eventType, request.SamplePayloadJson, ct);
					var context      = new Scriban.TemplateContext { StrictVariables = false };
					context.PushGlobal((Scriban.Runtime.ScriptObject)scriptObject);
					var evaluated = (await parsed.RenderAsync(context))?.Trim() ?? string.Empty;
					return Ok(new { isValid = true, evaluatedResult = evaluated });
				}
				catch (Exception ex)
				{
					return Ok(new { isValid = false, parseErrors = new[] { $"Evaluation error: {ex.Message}" } });
				}
			}

			return Ok(new { isValid = true });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<IActionResult> RunDetail(string runId, CancellationToken ct)
		{
			if (!await CanUserViewWorkflowRunsAsync())
				return RedirectToAction("Dashboard", "Home");

			var run = await _workflowService.GetWorkflowRunByIdAsync(runId, ct);
			if (run == null || run.DepartmentId != DepartmentId)
				return NotFound();

			var logs  = await _workflowService.GetLogsForRunAsync(runId, ct);
			ViewBag.Logs = logs;
			return View(run);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Workflow_Delete)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteStep(string id, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return Forbid();

			await _workflowService.DeleteWorkflowStepAsync(id, ct);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowStepDeleted,
				Before       = JsonSerializer.Serialize(new { WorkflowStepId = id }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return Ok(new { success = true });
		}

		// ── Runs ──────────────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<IActionResult> Runs(string workflowId, int page = 1, CancellationToken ct = default)
		{
			if (!await CanUserViewWorkflowRunsAsync())
				return RedirectToAction("Dashboard", "Home");

			var runs = await _workflowService.GetRunsByWorkflowIdAsync(workflowId, page, 50, ct);
			ViewBag.WorkflowId = workflowId;
			ViewBag.Page       = page;
			return View(runs);
		}

		// ── Health ────────────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<IActionResult> Health(string workflowId, CancellationToken ct)
		{
			if (!await CanUserViewWorkflowRunsAsync())
				return RedirectToAction("Dashboard", "Home");

			var health = await _workflowService.GetWorkflowHealthAsync(workflowId, ct);
			ViewBag.WorkflowId = workflowId;
			return View(health);
		}

		// ── Pending ───────────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.WorkflowRun_View)]
		public async Task<IActionResult> Pending(CancellationToken ct)
		{
			if (!await CanUserViewWorkflowRunsAsync())
				return RedirectToAction("Dashboard", "Home");

			var runs = await _workflowService.GetPendingAndRunningRunsByDepartmentIdAsync(DepartmentId, ct);
			return View(runs);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.WorkflowRun_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelRun(string workflowRunId, CancellationToken ct)
		{
			if (!await CanUserViewWorkflowRunsAsync())
				return RedirectToAction("Dashboard", "Home");

			await _workflowService.CancelWorkflowRunAsync(workflowRunId, ct);
			return RedirectToAction("Pending");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.WorkflowRun_Delete)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ClearPending(CancellationToken ct)
		{
			if (!await CanUserViewWorkflowRunsAsync())
				return RedirectToAction("Dashboard", "Home");

			await _workflowService.ClearPendingRunsAsync(DepartmentId, ct);
			return RedirectToAction("Pending");
		}

		// ── Credentials ───────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.WorkflowCredential_View)]
		public async Task<IActionResult> Credentials(CancellationToken ct)
		{
			if (!await CanUserManageWorkflowCredentialsAsync())
				return RedirectToAction("Dashboard", "Home");

			var creds = await _workflowService.GetCredentialsByDepartmentIdAsync(DepartmentId, ct);
			return View(creds);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Create)]
		public async Task<IActionResult> CredentialNew()
		{
			if (!await CanUserManageWorkflowCredentialsAsync())
				return RedirectToAction("Dashboard", "Home");

			return View(new WorkflowCredentialViewModel());
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Create)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CredentialNew(WorkflowCredentialViewModel model, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowCredentialsAsync())
				return RedirectToAction("Dashboard", "Home");

			if (!ModelState.IsValid)
				return View(model);

			var plaintextJson = BuildCredentialJson(model);
			if (plaintextJson is null)
			{
				ModelState.AddModelError(string.Empty, "Please fill in the required fields for the selected credential type.");
				return View(model);
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var credential = new WorkflowCredential
			{
				DepartmentId    = DepartmentId,
				CreatedByUserId = UserId,
				Name            = model.Name,
				CredentialType  = (int)model.CredentialType,
				EncryptedData   = plaintextJson
			};

			var saved = await _workflowService.SaveCredentialAsync(credential, department?.Code ?? string.Empty, ct);

			// NOTE: Credential data is intentionally excluded from the audit log to prevent sensitive info from being stored.
			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowCredentialAdded,
				After        = JsonSerializer.Serialize(new { saved.WorkflowCredentialId, saved.Name, saved.CredentialType }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Credentials");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Update)]
		public async Task<IActionResult> CredentialEdit(string credentialId, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowCredentialsAsync())
				return RedirectToAction("Dashboard", "Home");

			var cred = await _workflowService.GetCredentialByIdAsync(credentialId, ct);
			if (cred == null || cred.DepartmentId != DepartmentId)
				return NotFound();

			var vm = new WorkflowCredentialViewModel
			{
				WorkflowCredentialId = cred.WorkflowCredentialId,
				Name                 = cred.Name,
				CredentialType       = (WorkflowCredentialType)cred.CredentialType
			};

			return View(vm);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CredentialEdit(WorkflowCredentialViewModel model, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowCredentialsAsync())
				return RedirectToAction("Dashboard", "Home");

			var existing = await _workflowService.GetCredentialByIdAsync(model.WorkflowCredentialId, ct);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return NotFound();

			if (!ModelState.IsValid)
				return View(model);

			var plaintextJson = BuildCredentialJson(model);
			if (plaintextJson is null)
			{
				ModelState.AddModelError(string.Empty, "Please fill in the required fields for the selected credential type.");
				return View(model);
			}

			existing.Name           = model.Name;
			existing.CredentialType = (int)model.CredentialType;
			existing.EncryptedData  = plaintextJson;
			existing.UpdatedByUserId = UserId;

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			await _workflowService.SaveCredentialAsync(existing, department?.Code ?? string.Empty, ct);

			// NOTE: Credential data is intentionally excluded from the audit log to prevent sensitive info from being stored.
			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowCredentialEdited,
				After        = JsonSerializer.Serialize(new { existing.WorkflowCredentialId, existing.Name, existing.CredentialType }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Credentials");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Delete)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteCredential(string credentialId, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowCredentialsAsync())
				return RedirectToAction("Dashboard", "Home");

			var existing = await _workflowService.GetCredentialByIdAsync(credentialId, ct);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return NotFound();

			await _workflowService.DeleteCredentialAsync(credentialId, ct);

			// NOTE: Credential data is intentionally excluded from the audit log to prevent sensitive info from being stored.
			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowCredentialDeleted,
				Before       = JsonSerializer.Serialize(new { existing.WorkflowCredentialId, existing.Name, existing.CredentialType }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Credentials");
		}

		// ── Permission Helpers ────────────────────────────────────────────────────

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

		// ── Helpers ───────────────────────────────────────────────────────────────

		private static string BuildCredentialJson(WorkflowCredentialViewModel vm)
		{
			object data = vm.CredentialType switch
			{
				WorkflowCredentialType.Smtp => AllBlank(vm.SmtpHost, vm.SmtpUsername, vm.SmtpPassword) ? null
					: (object)new { host = vm.SmtpHost, port = vm.SmtpPort ?? 587, username = vm.SmtpUsername, password = vm.SmtpPassword, useSsl = vm.SmtpUseSsl, fromAddress = vm.SmtpFromAddress },

				WorkflowCredentialType.Twilio => AllBlank(vm.TwilioAccountSid, vm.TwilioAuthToken) ? null
					: new { accountSid = vm.TwilioAccountSid, authToken = vm.TwilioAuthToken, fromNumber = vm.TwilioFromNumber },

				WorkflowCredentialType.Ftp => AllBlank(vm.FtpHost, vm.FtpUsername, vm.FtpPassword) ? null
					: new { host = vm.FtpHost, port = vm.FtpPort ?? 21, username = vm.FtpUsername, password = vm.FtpPassword, passive = vm.FtpPassive },

				WorkflowCredentialType.Sftp => AllBlank(vm.SftpHost, vm.SftpUsername) ? null
					: new { host = vm.SftpHost, port = vm.SftpPort ?? 22, username = vm.SftpUsername, password = vm.SftpPassword, privateKey = vm.SftpPrivateKey },

				WorkflowCredentialType.AwsS3 => AllBlank(vm.AwsAccessKeyId, vm.AwsSecretAccessKey) ? null
					: new { accessKeyId = vm.AwsAccessKeyId, secretAccessKey = vm.AwsSecretAccessKey, region = vm.AwsRegion, bucketName = vm.AwsBucketName },

				WorkflowCredentialType.HttpBearer => AllBlank(vm.HttpBearerToken) ? null
					: new { token = vm.HttpBearerToken },

				WorkflowCredentialType.HttpBasic => AllBlank(vm.HttpBasicUsername, vm.HttpBasicPassword) ? null
					: new { username = vm.HttpBasicUsername, password = vm.HttpBasicPassword },

				WorkflowCredentialType.HttpApiKey => AllBlank(vm.ApiKeyHeaderName, vm.ApiKeyValue) ? null
					: new { headerName = vm.ApiKeyHeaderName, apiKey = vm.ApiKeyValue },

				WorkflowCredentialType.MicrosoftTeams => AllBlank(vm.TeamsWebhookUrl) ? null
					: new { webhookUrl = vm.TeamsWebhookUrl },

				WorkflowCredentialType.Slack => AllBlank(vm.SlackWebhookUrl, vm.SlackBotToken) ? null
					: new { webhookUrl = vm.SlackWebhookUrl, botToken = vm.SlackBotToken },

				WorkflowCredentialType.Discord => AllBlank(vm.DiscordWebhookUrl, vm.DiscordBotToken) ? null
					: new { webhookUrl = vm.DiscordWebhookUrl, botToken = vm.DiscordBotToken },

				WorkflowCredentialType.AzureBlobStorage => AllBlank(vm.AzureConnectionString, vm.AzureContainerName) ? null
					: new { connectionString = vm.AzureConnectionString, containerName = vm.AzureContainerName },

				WorkflowCredentialType.Box => AllBlank(vm.BoxClientId, vm.BoxClientSecret) ? null
					: new { clientId = vm.BoxClientId, clientSecret = vm.BoxClientSecret, enterpriseId = vm.BoxEnterpriseId, privateKey = vm.BoxPrivateKey, privateKeyPassword = vm.BoxPrivateKeyPassword, publicKeyId = vm.BoxPublicKeyId },

				WorkflowCredentialType.Dropbox => AllBlank(vm.DropboxRefreshToken, vm.DropboxAppKey) ? null
					: new { refreshToken = vm.DropboxRefreshToken, appKey = vm.DropboxAppKey, appSecret = vm.DropboxAppSecret },

				_ => null
			};

			return data is null ? null : JsonSerializer.Serialize(data);
		}

		private static bool AllBlank(params string[] values) =>
			Array.TrueForAll(values, v => string.IsNullOrWhiteSpace(v));
	}
}
