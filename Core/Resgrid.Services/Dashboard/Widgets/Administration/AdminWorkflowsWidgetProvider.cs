using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Administration;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.workflows (plan §7.11, §9A.2). Gates are the Workflows pages' policies (Workflow_View for the list, WorkflowRun_View for
	/// Pending / Health); inside, the same permission rows WorkflowsController checks — CreateWorkflow for the workflow list,
	/// ViewWorkflowRuns for run data — through IPermissionsService.IsUserAllowed. Reads: permissions, workflows, one grouped run count
	/// (IWorkflowService.GetDepartmentRunStatusCountsAsync). The run error text is never selected (catalogued, not reveal-capable).
	/// </summary>
	[DashboardWidget("admin.workflows")]
	public class AdminWorkflowsWidgetProvider : IDashboardWidgetDataProvider
	{
		private static readonly int[] Open = { (int)WorkflowRunStatus.Pending, (int)WorkflowRunStatus.Running, (int)WorkflowRunStatus.Retrying };

		private readonly IWorkflowService _workflowService;
		private readonly IPermissionsService _permissionsService;

		public AdminWorkflowsWidgetProvider(IWorkflowService workflowService, IPermissionsService permissionsService)
		{
			_workflowService = workflowService;
			_permissionsService = permissionsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var viewer = context.Viewer;
			var departmentId = context.DepartmentId;
			var rows = await _permissionsService.GetAllPermissionsForDepartmentAsync(departmentId) ?? new List<Permission>();
			var roles = (viewer?.PersonnelRoleIds ?? Array.Empty<int>()).Select(id => new PersonnelRole { PersonnelRoleId = id }).ToList();
			bool Allowed(PermissionTypes type) => _permissionsService.IsUserAllowed(rows.FirstOrDefault(p => p.PermissionType == (int)type),
				viewer?.IsDepartmentAdmin == true, viewer?.IsGroupAdmin == true, roles);

			if (!Allowed(PermissionTypes.CreateWorkflow) || !Allowed(PermissionTypes.ViewWorkflowRuns))
				throw new DashboardWidgetStateException(DashboardWidgetErrorCodes.Forbidden, "WidgetErrorForbidden");

			var workflows = (await _workflowService.GetWorkflowsByDepartmentIdAsync(departmentId, cancellationToken) ?? new List<Workflow>())
				.Where(w => w.DepartmentId == departmentId).ToList();
			var byId = workflows.ToDictionary(w => w.WorkflowId, StringComparer.OrdinalIgnoreCase);
			var since = context.NowUtc.AddHours(-24);
			var counts = (await _workflowService.GetDepartmentRunStatusCountsAsync(departmentId, since, cancellationToken) ?? new List<WorkflowRunStatusCount>())
				.Where(c => c.WorkflowId != null && byId.ContainsKey(c.WorkflowId)).ToList();
			// Finished statuses only ever match the 24-hour window in the query; open ones (pending, running, retrying) match at any age.
			var recent = counts.Where(c => !Open.Contains(c.Status)).ToList();

			int Count(IEnumerable<WorkflowRunStatusCount> source, WorkflowRunStatus status) => source.Where(c => c.Status == (int)status).Sum(c => c.Total);
			var completed = Count(recent, WorkflowRunStatus.Completed);
			var failed = Count(recent, WorkflowRunStatus.Failed);

			return new AdminWorkflowsWidgetData
			{
				Workflows = workflows.Count,
				ActiveWorkflows = workflows.Count(w => w.IsEnabled),
				Pending = Count(counts, WorkflowRunStatus.Pending),
				Running = Count(counts, WorkflowRunStatus.Running),
				Retrying = Count(counts, WorkflowRunStatus.Retrying),
				Completed24h = completed,
				Failed24h = failed,
				Total24h = recent.Sum(c => c.Total),
				SuccessPercent24h = completed + failed == 0 ? null : (int)Math.Round(completed * 100.0 / (completed + failed)),
				ErrorDetailsProtected = viewer?.AdpEnforced == true,
				Unhealthy = recent.GroupBy(c => c.WorkflowId, StringComparer.OrdinalIgnoreCase)
					.Select(g => new AdminWorkflowHealthRow
					{
						WorkflowId = byId[g.Key].WorkflowId,
						Name = byId[g.Key].Name,
						IsEnabled = byId[g.Key].IsEnabled,
						Failed24h = Count(g, WorkflowRunStatus.Failed),
						Total24h = g.Sum(c => c.Total),
						LastFailureOn = g.Where(c => c.Status == (int)WorkflowRunStatus.Failed).Max(c => c.LastOn)
					})
					.Where(r => r.Failed24h > 0)
					.OrderByDescending(r => r.Failed24h).ThenByDescending(r => r.LastFailureOn)
					.Take(context.GetInt("maxRows", 5)).ToList()
			};
		}
	}
}
