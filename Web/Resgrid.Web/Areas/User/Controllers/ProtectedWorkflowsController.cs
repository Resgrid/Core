using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Models.ProtectedWorkflows;
using Resgrid.Web.Attributes;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// ADP Protected Workflows administration: the department toggle, the editor's "Protected release" panel, the list
	/// of releases and the disclosure log. Every rule is enforced by <see cref="IProtectedWorkflowService"/> (ADP egress
	/// permission, interactive step-up for request/approve/renew/toggle, two-person rule); this controller only supplies
	/// the actor, including the session's step-up time. A command refused with step_up_required is retried after the
	/// page sends the user through <see cref="StepUp"/>.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class ProtectedWorkflowsController : SecureBaseController
	{
		private readonly IProtectedWorkflowService _protectedWorkflows;
		private readonly IWorkflowService _workflowService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;

		public ProtectedWorkflowsController(IProtectedWorkflowService protectedWorkflows, IWorkflowService workflowService,
			IDepartmentsService departmentsService, IUserProfileService userProfileService)
		{
			_protectedWorkflows = protectedWorkflows;
			_workflowService = workflowService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
		}

		// ── Pages ─────────────────────────────────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> Index(CancellationToken cancellationToken)
		{
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Unauthorized();

			var releases = await _protectedWorkflows.GetReleasesForDepartmentAsync(DepartmentId);
			var workflows = (await _workflowService.GetWorkflowsByDepartmentIdAsync(DepartmentId, cancellationToken))
				.ToDictionary(w => w.WorkflowId, StringComparer.OrdinalIgnoreCase);
			var recent = await _protectedWorkflows.GetDisclosuresAsync(DepartmentId, new ProtectedWorkflowDisclosureFilter
			{
				RecordType = ProtectedWorkflowRecordTypes.Disclosure,
				FromUtc = DateTime.UtcNow.AddDays(-30),
				MaxRows = 50000
			});
			var sends = recent.Where(d => !d.IsTest && d.Outcome == ProtectedWorkflowDisclosureOutcomes.Sent && d.WorkflowProtectedReleaseId != null)
				.GroupBy(d => d.WorkflowProtectedReleaseId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

			var model = new ProtectedWorkflowsIndexView
			{
				Settings = await _protectedWorkflows.GetDepartmentSettingsAsync(DepartmentId),
				Chain = await _protectedWorkflows.VerifyChainAsync(DepartmentId)
			};

			foreach (var release in releases.OrderBy(r => r.ReleaseState == ProtectedReleaseState.Revoked).ThenByDescending(r => r.CreatedOn))
			{
				workflows.TryGetValue(release.WorkflowId, out var workflow);
				model.Releases.Add(new ProtectedWorkflowReleaseRow
				{
					Release = release,
					WorkflowName = workflow?.Name ?? release.WorkflowId,
					WorkflowExists = workflow != null,
					ApprovedByName = await DisplayNameAsync(release.ApprovedByUserId),
					SendsLast30Days = sends.TryGetValue(release.WorkflowProtectedReleaseId, out var count) ? count : 0
				});
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Disclosures(string workflowId, string callId, DateTime? from, DateTime? to, CancellationToken cancellationToken)
		{
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Unauthorized();

			var workflows = await _workflowService.GetWorkflowsByDepartmentIdAsync(DepartmentId, cancellationToken);
			var records = await _protectedWorkflows.GetDisclosuresAsync(DepartmentId, Filter(workflowId, callId, from, to, 2000));

			var model = new ProtectedWorkflowDisclosuresView
			{
				WorkflowId = workflowId,
				CallId = callId,
				From = from,
				To = to,
				Workflows = workflows.OrderBy(w => w.Name).ToList(),
				Records = records,
				WorkflowNames = workflows.ToDictionary(w => w.WorkflowId, w => w.Name, StringComparer.OrdinalIgnoreCase),
				Chain = await _protectedWorkflows.VerifyChainAsync(DepartmentId)
			};

			foreach (var actor in records.Select(r => r.ActorUserId).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase))
				model.ActorNames[actor] = await DisplayNameAsync(actor);

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> DisclosuresCsv(string workflowId, string callId, DateTime? from, DateTime? to)
		{
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Unauthorized();

			var csv = await _protectedWorkflows.ExportDisclosuresCsvAsync(DepartmentId, Filter(workflowId, callId, from, to, 50000));
			return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv",
				$"protected-workflow-disclosures-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
		}

		/// <summary>The editor panel (partial). Empty when the department has not turned the feature on and the workflow has no release.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Workflow_Update)]
		public async Task<IActionResult> Panel(string workflowId, CancellationToken cancellationToken)
		{
			var view = await _protectedWorkflows.GetReleaseViewAsync(DepartmentId, workflowId, UserId, cancellationToken);
			if (view == null)
				return NotFound();

			if (!view.TriggerSupported && view.Release == null)
				return Content(string.Empty);
			if (!view.Department.Enabled && (view.Release == null || view.Release.ReleaseState == ProtectedReleaseState.Revoked))
				return Content(string.Empty);

			string credentialName = null;
			if (!string.IsNullOrWhiteSpace(view.Release?.WorkflowCredentialId ?? view.Validation?.WorkflowCredentialId))
			{
				var credential = await _workflowService.GetCredentialByIdAsync(view.Release?.WorkflowCredentialId ?? view.Validation.WorkflowCredentialId, cancellationToken);
				credentialName = credential?.DepartmentId == DepartmentId ? credential.Name : null;
			}

			return PartialView("_ReleasePanel", new ProtectedReleasePanelView
			{
				Release = view,
				CurrentUserId = UserId,
				CredentialName = credentialName,
				RequestedByName = await DisplayNameAsync(view.Release?.RequestedByUserId),
				ApprovedByName = await DisplayNameAsync(view.Release?.ApprovedByUserId)
			});
		}

		/// <summary>Sends the user through the step-up verification and back to the page they came from.</summary>
		[HttpGet]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public IActionResult StepUp(string returnUrl)
		{
			// The attribute's window can be longer than the one Protected Workflows accept; re-verify rather than bounce the
			// user back to a command the service will refuse again.
			if (!ProtectedWorkflowService.IsStepUpFresh(RequiresRecentTwoFactorAttribute.GetStepUpVerifiedAtUtc(HttpContext, UserId), DateTime.UtcNow))
				return RedirectToAction("Verify2FA", "TwoFactor", new { area = "User", returnUrl = Url.Action("StepUp", new { returnUrl }) });

			if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
				return LocalRedirect(returnUrl);
			return RedirectToAction("Index");
		}

		// ── Commands (JSON) ───────────────────────────────────────────────────────────────────────────

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveDepartmentSettings([FromBody] ProtectedWorkflowSettingsInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			return Result(await _protectedWorkflows.SetDepartmentSettingsAsync(DepartmentId, input.Enabled, input.RequireSecondApprover,
				input.AcknowledgedVersion, Actor(), cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveDraft([FromBody] ProtectedReleaseDraftInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			return Result(await _protectedWorkflows.SaveDraftAsync(DepartmentId, input.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = input.FieldIds ?? new List<string>(),
				RecipientType = input.RecipientType,
				RecipientName = input.RecipientName,
				Purpose = input.Purpose
			}, Actor(), cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RequestApproval([FromBody] ProtectedReleaseCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			return Result(await _protectedWorkflows.RequestApprovalAsync(DepartmentId, input.WorkflowId, input.Attested,
				input.AcknowledgedVersion, input.ReviewedFingerprint, Actor(), input.Sensitive(), cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Approve([FromBody] ProtectedReleaseCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			return Result(await _protectedWorkflows.ApproveAsync(DepartmentId, input.ReleaseId, input.Attested,
				input.AcknowledgedVersion, input.ReviewedFingerprint, Actor(), input.Sensitive(), cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Renew([FromBody] ProtectedReleaseCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			return Result(await _protectedWorkflows.RenewAsync(DepartmentId, input.ReleaseId, input.Attested,
				input.AcknowledgedVersion, input.ReviewedFingerprint, Actor(), input.Sensitive(), cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Suspend([FromBody] ProtectedReleaseCommandInput input, CancellationToken cancellationToken) =>
			input == null ? BadRequest() : Result(await _protectedWorkflows.SuspendAsync(DepartmentId, input.ReleaseId, Actor(), cancellationToken));

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Revoke([FromBody] ProtectedReleaseCommandInput input, CancellationToken cancellationToken) =>
			input == null ? BadRequest() : Result(await _protectedWorkflows.RevokeAsync(DepartmentId, input.ReleaseId, Actor(), cancellationToken));

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DiscardDraft([FromBody] ProtectedReleaseCommandInput input, CancellationToken cancellationToken) =>
			input == null ? BadRequest() : Result(await _protectedWorkflows.DiscardDraftAsync(DepartmentId, input.ReleaseId, Actor(), cancellationToken));

		/// <summary>"Send test with sample data": synthetic values only, recorded as test disclosures.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SendTest([FromBody] ProtectedReleaseCommandInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			if (!await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId))
				return Json(new { success = false, error = ProtectedWorkflowErrorCodes.PermissionDenied });

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var result = await _workflowService.SendProtectedTestAsync(DepartmentId, department?.Code ?? string.Empty, input.WorkflowId, cancellationToken);
			return Json(new
			{
				success = result.Success,
				error = result.ErrorCode,
				validationErrors = result.ValidationErrors.Select(e => e.Code),
				steps = result.Steps.Select(s => new { stepId = s.WorkflowStepId, outcome = s.Outcome, httpStatus = s.HttpStatus, error = s.ErrorCode })
			});
		}

		// ── Helpers ───────────────────────────────────────────────────────────────────────────────────

		/// <summary>A signed-in web session is interactive; the step-up time comes from this session's verified second factor.</summary>
		private ProtectedWorkflowActor Actor() => new ProtectedWorkflowActor
		{
			UserId = UserId,
			IsInteractive = true,
			StepUpVerifiedAtUtc = RequiresRecentTwoFactorAttribute.GetStepUpVerifiedAtUtc(HttpContext, UserId)
		};

		private IActionResult Result(ProtectedWorkflowCommandResult result) => Json(new
		{
			success = result.Success,
			error = result.ErrorCode,
			pendingConfirmation = result.PendingConfirmation,
			validationErrors = result.ValidationErrors.Select(e => e.Code).Distinct(),
			state = result.Release?.State,
			releaseId = result.Release?.WorkflowProtectedReleaseId
		});

		private static ProtectedWorkflowDisclosureFilter Filter(string workflowId, string callId, DateTime? from, DateTime? to, int maxRows) =>
			new ProtectedWorkflowDisclosureFilter
			{
				WorkflowId = string.IsNullOrWhiteSpace(workflowId) ? null : workflowId,
				EntityId = string.IsNullOrWhiteSpace(callId) ? null : callId.Trim(),
				FromUtc = from.HasValue ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc) : null,
				ToUtc = to.HasValue ? DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null,
				MaxRows = maxRows
			};

		private async Task<string> DisplayNameAsync(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return null;
			if (userId.StartsWith("system:", StringComparison.Ordinal))
				return userId;

			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			return profile?.FullName?.AsFirstNameLastName ?? userId;
		}
	}
}
