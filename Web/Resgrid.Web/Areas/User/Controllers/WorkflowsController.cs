using System;
using System.Collections.Generic;
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
using Resgrid.Services;

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
		private readonly IRecordsCutoverService _recordsCutoverService;
		private readonly IRecordsExportService _recordsExportService;
		private readonly IProtectedWorkflowService _protectedWorkflows;
		private readonly Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.Workflows.Workflows> _localizer;

		public WorkflowsController(IWorkflowService workflowService, IDepartmentsService departmentsService,
			IPermissionsService permissionsService, IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService, IAuditService auditService,
			IEventAggregator eventAggregator, ISubscriptionsService subscriptionsService,
			IWorkflowTemplateContextBuilder contextBuilder, IRecordsCutoverService recordsCutoverService, IRecordsExportService recordsExportService,
			IProtectedWorkflowService protectedWorkflows,
			Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.Workflows.Workflows> localizer)
		{
			_localizer               = localizer;
			_protectedWorkflows      = protectedWorkflows;
			_recordsExportService    = recordsExportService;
			_workflowService         = workflowService;
			_departmentsService      = departmentsService;
			_permissionsService      = permissionsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService   = personnelRolesService;
			_auditService            = auditService;
			_eventAggregator         = eventAggregator;
			_subscriptionsService    = subscriptionsService;
			_contextBuilder          = contextBuilder;
			_recordsCutoverService   = recordsCutoverService;
		}

		// ── Index ─────────────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.Workflow_View)]
		public async Task<IActionResult> Index(CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			var workflows = await _workflowService.GetWorkflowsByDepartmentIdAsync(DepartmentId, ct);

			// Protected Workflows status badges: each workflow's current (latest) release.
			ViewBag.ProtectedReleases = (await _protectedWorkflows.GetReleasesForDepartmentAsync(DepartmentId))
				.GroupBy(r => r.WorkflowId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedOn).First(), StringComparer.OrdinalIgnoreCase);
			ViewBag.CanAdministerProtectedWorkflows = await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId);

			return View(workflows);
		}

		// ── New / Create ──────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.Workflow_Create)]
		public async Task<IActionResult> New(CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			ViewBag.RecordsTriggersAvailable = await RecordsTriggersAvailableAsync();
			ViewBag.GalleryTemplates = await GalleryTemplatesAsync();

			return View(new Workflow { MaxRetryCount = 3, RetryBackoffBaseSeconds = 5, IsEnabled = true });
		}

		/// <summary>
		/// Creates a workflow from a gallery template: disabled, with placeholder destinations and no credential, so nothing
		/// runs until an administrator finishes it. The EHR samples also get a Draft Protected Workflows release (they read
		/// protected values, so they only ever send once a release is approved).
		/// </summary>
		[HttpPost]
		[Authorize(Policy = ResgridResources.Workflow_Create)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CreateFromTemplate(string templateKey, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowsAsync())
				return RedirectToAction("Dashboard", "Home");

			var template = WorkflowTemplateGallery.Find(templateKey);
			if (template == null || !(await GalleryTemplatesAsync()).Any(t => t.Key == template.Key))
				return NotFound();

			var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
			if (!await _workflowService.CanAddWorkflowAsync(DepartmentId, plan?.IsFree ?? false, ct))
			{
				TempData["GalleryError"] = "WorkflowLimitReached";
				return RedirectToAction("New");
			}

			var saved = await _workflowService.SaveWorkflowAsync(new Workflow
			{
				DepartmentId = DepartmentId,
				Name = _localizer[template.NameKey].Value,
				Description = _localizer[template.DescriptionKey].Value,
				TriggerEventType = (int)template.Trigger,
				IsEnabled = false,
				MaxRetryCount = 3,
				RetryBackoffBaseSeconds = 30,
				CreatedByUserId = UserId
			}, ct);

			var order = 1;
			foreach (var step in template.Steps)
			{
				await _workflowService.SaveWorkflowStepAsync(new WorkflowStep
				{
					WorkflowId = saved.WorkflowId,
					ActionType = (int)step.ActionType,
					ActionConfig = step.ActionConfig,
					OutputTemplate = step.OutputTemplate,
					ConditionExpression = step.ConditionExpression,
					StepOrder = order++,
					IsEnabled = true,
					CreatedByUserId = UserId
				}, ct);
			}

			var protectedDraftCreated = false;
			if (template.RequiresProtectedWorkflows && await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
			{
				var draft = await _protectedWorkflows.SaveDraftAsync(DepartmentId, saved.WorkflowId, new ProtectedReleaseDraft { FieldIds = template.ReleaseFieldIds },
					new ProtectedWorkflowActor { UserId = UserId }, ct);
				protectedDraftCreated = draft?.Success == true;
			}

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowAdded,
				After        = JsonSerializer.Serialize(new { saved.WorkflowId, saved.Name, saved.TriggerEventType, Template = template.Key }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			// Only claim a draft protected release when one was actually saved (the caller may not administer Protected
			// Workflows, or the draft may have been refused).
			TempData["GalleryCreated"] = !template.RequiresProtectedWorkflows
				? "GalleryCreated"
				: protectedDraftCreated ? "GalleryCreatedProtected" : "GalleryCreatedWithoutProtectedDraft";
			return RedirectToAction("Edit", new { workflowId = saved.WorkflowId });
		}

		/// <summary>The gallery for this department: the EHR samples only when Protected Workflows are enabled.</summary>
		private async Task<IReadOnlyList<WorkflowGalleryTemplate>> GalleryTemplatesAsync()
		{
			var settings = await _protectedWorkflows.GetDepartmentSettingsAsync(DepartmentId);
			return WorkflowTemplateGallery.Available(settings.Enabled && settings.AdpActive);
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
				ViewBag.RecordsTriggersAvailable = await RecordsTriggersAvailableAsync();
				ViewBag.GalleryTemplates = await GalleryTemplatesAsync();
				return View(model);
			}

			// Any number of workflows may share a trigger: each one gets its own run for every event (only the plan's
			// workflow count is capped).

			// Enforce plan-based workflow count cap
			var plan      = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
			var isFreePlan = plan?.IsFree ?? false;
			if (!await _workflowService.CanAddWorkflowAsync(DepartmentId, isFreePlan, ct))
			{
				ModelState.AddModelError(string.Empty, _localizer["WorkflowLimitReached"].Value);
				ViewBag.RecordsTriggersAvailable = await RecordsTriggersAvailableAsync();
				ViewBag.GalleryTemplates = await GalleryTemplatesAsync();
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
			ViewBag.RecordsTriggersAvailable = await RecordsTriggersAvailableAsync();
			await AddExportTemplatesAsync((WorkflowTriggerEventType)workflow.TriggerEventType);

			// EHR delivery options (content type, success rule, capture, idempotency) belong to protected steps.
			var protectedRelease = await _protectedWorkflows.GetCurrentReleaseAsync(workflowId);
			ViewBag.IsProtectedWorkflow = protectedRelease != null && protectedRelease.ReleaseState != ProtectedReleaseState.Revoked;

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
				ViewBag.RecordsTriggersAvailable = await RecordsTriggersAvailableAsync();
				await AddExportTemplatesAsync((WorkflowTriggerEventType)model.TriggerEventType);
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

			// Protected Workflows: protected.* never in a condition, URL or header, and only in the output template of a
			// workflow that has (or is being given) a protected release — otherwise it would silently render empty.
			var protectedTemplateError = await _protectedWorkflows.ValidateStepTemplatesAsync(step, ct);
			if (protectedTemplateError != null)
				return BadRequest(Resgrid.Localization.Areas.User.ProtectedWorkflows.ProtectedWorkflowsResources.Get(
					"ValidationError_" + protectedTemplateError, System.Globalization.CultureInfo.CurrentUICulture.Name));

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
			ApplyPublishedKeys(vm, cred, includeMethod: true);

			return View(vm);
		}

		/// <summary>The public signing key of a private_key_jwt credential, as a JWK file for an EHR that takes an uploaded key.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.WorkflowCredential_View)]
		public async Task<IActionResult> CredentialJwk(string credentialId, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowCredentialsAsync())
				return RedirectToAction("Dashboard", "Home");

			var cred = await _workflowService.GetCredentialByIdAsync(credentialId, ct);
			if (cred == null || cred.DepartmentId != DepartmentId || cred.CredentialType != (int)WorkflowCredentialType.OAuth2ClientCredentials)
				return NotFound();

			var current = WorkflowJwtKeys.ReadPublicKeys(cred.PublicJwks).Where(k => !k.RetiredOn.HasValue).OrderByDescending(k => k.CreatedOn).FirstOrDefault();
			if (current?.Jwk == null)
				return NotFound();

			return File(System.Text.Encoding.UTF8.GetBytes(current.Jwk.ToString(Newtonsoft.Json.Formatting.Indented)), "application/json",
				$"resgrid-workflow-credential-{current.Kid}.jwk.json");
		}

		/// <summary>Rotates a private_key_jwt credential's signing key; the old key stays in the JWKS for the overlap window.</summary>
		[HttpPost]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RotateCredentialKey(string credentialId, CancellationToken ct)
		{
			if (!await CanUserManageWorkflowCredentialsAsync())
				return RedirectToAction("Dashboard", "Home");

			var existing = await _workflowService.GetCredentialByIdAsync(credentialId, ct);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var rotated = await _workflowService.RotateCredentialSigningKeyAsync(credentialId, DepartmentId, department?.Code ?? string.Empty, UserId, ct);
			if (rotated == null)
				return BadRequest();

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.WorkflowCredentialEdited,
				After        = JsonSerializer.Serialize(new { rotated.WorkflowCredentialId, rotated.Name, rotated.CredentialType, KeyRotated = true }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			TempData["CredentialKeyRotated"] = true;
			return RedirectToAction("CredentialEdit", new { credentialId });
		}

		/// <param name="includeMethod">True on the first render of the edit page: the stored method and algorithm are shown.</param>
		private static void ApplyPublishedKeys(WorkflowCredentialViewModel vm, WorkflowCredential cred, bool includeMethod)
		{
			if (cred.CredentialType != (int)WorkflowCredentialType.OAuth2ClientCredentials || string.IsNullOrWhiteSpace(cred.PublicJwks))
				return;

			var keys = WorkflowJwtKeys.ReadPublicKeys(cred.PublicJwks);
			var current = keys.Where(k => !k.RetiredOn.HasValue).OrderByDescending(k => k.CreatedOn).FirstOrDefault();
			if (includeMethod)
			{
				vm.OAuth2AuthMethod = WorkflowJwtKeys.PrivateKeyJwt;
				vm.OAuth2SigningAlg = current?.Alg ?? WorkflowJwtKeys.Rs384;
			}
			vm.CurrentKeyId = current?.Kid;
			vm.CurrentKeyCreatedOn = current?.CreatedOn;
			vm.PublishedKeyCount = keys.Count(k => WorkflowJwtKeys.IsPublished(k.RetiredOn, DateTime.UtcNow, Config.DataProtectionConfig.WorkflowJwksOverlapDays));
			vm.JwksUrl = $"{(Config.SystemBehaviorConfig.ResgridApiBaseUrl ?? string.Empty).TrimEnd('/')}/api/v4/workflow-credentials/{cred.WorkflowCredentialId}/jwks.json";
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

			ApplyPublishedKeys(model, existing, includeMethod: false);
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

		/// <summary>
		/// Records (RMS) triggers 100-115 are only offered once Records is usable for the department (plan section
		/// 5.6). A workflow already bound to one keeps working; the outbox simply never fires before activation.
		/// </summary>
		/// <summary>
		/// Department report exports a step on a Records trigger may attach (RMS plan section 5.6). Only the
		/// designer for a Records workflow gets the list; other triggers never carry a record to export.
		/// </summary>
		private async Task AddExportTemplatesAsync(WorkflowTriggerEventType trigger)
		{
			ViewBag.RecordsExportAvailable = false;
			ViewBag.RecordsExportTemplatesJson = "[]";
			if (!WorkflowTriggerEventTypes.IsRecordsTrigger(trigger) || !await RecordsTriggersAvailableAsync())
				return;

			var templates = await _recordsExportService.GetTemplatesAsync(DepartmentId);
			ViewBag.RecordsExportAvailable = true;
			ViewBag.RecordsExportTemplatesJson = JsonSerializer.Serialize(templates.Where(t => t.IsEnabled)
				.Select(t => new { id = t.RmsExportTemplateId, name = t.Name, key = t.TemplateKey, format = ((RmsExportFormat)t.Format).ToString(), scope = ((RmsExportScope)t.Scope).ToString() }));
		}

		private async Task<bool> RecordsTriggersAvailableAsync()
		{
			var state = await _recordsCutoverService.GetModuleStateAsync(DepartmentId);
			return state != null && state.RecordsUsable;
		}

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

				WorkflowCredentialType.OAuth2ClientCredentials => AllBlank(vm.OAuth2TokenUrl, vm.OAuth2ClientId) ? null
					: vm.IsPrivateKeyJwt
						// The signing keys are generated and kept by WorkflowService; nothing secret is posted.
						? new { tokenUrl = vm.OAuth2TokenUrl?.Trim(), clientId = vm.OAuth2ClientId?.Trim(), clientSecret = (string)null, scope = vm.OAuth2Scope?.Trim(), audience = vm.OAuth2Audience?.Trim(), authMethod = WorkflowJwtKeys.PrivateKeyJwt, signingAlg = WorkflowJwtKeys.NormalizeAlgorithm(vm.OAuth2SigningAlg) }
						: (object)new { tokenUrl = vm.OAuth2TokenUrl?.Trim(), clientId = vm.OAuth2ClientId?.Trim(), clientSecret = vm.OAuth2ClientSecret, scope = vm.OAuth2Scope?.Trim(), audience = vm.OAuth2Audience?.Trim(), authMethod = WorkflowJwtKeys.ClientSecret, signingAlg = (string)null },

				_ => null
			};

			return data is null ? null : JsonSerializer.Serialize(data);
		}

		private static bool AllBlank(params string[] values) =>
			Array.TrueForAll(values, v => string.IsNullOrWhiteSpace(v));
	}
}
