using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Administration;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.import (plan §7.11). DepartmentImportController admits a member only through IDepartmentImportAccessService.CanManageAsync
	/// (the master flag plus a current department administrator); the widget re-checks it, then reads the summary method added for
	/// it (IDepartmentImportService.GetImportSummaryAsync: workspaces, plus the current workspace's data sets and runs).
	/// </summary>
	[DashboardWidget("admin.import")]
	public class AdminImportWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IDepartmentImportAccessService _importAccessService;
		private readonly IDepartmentImportService _importService;

		public AdminImportWidgetProvider(IDepartmentImportAccessService importAccessService, IDepartmentImportService importService)
		{
			_importAccessService = importAccessService;
			_importService = importService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			if (!await _importAccessService.CanManageAsync(context.DepartmentId, context.UserId))
				throw new DashboardWidgetStateException(DashboardWidgetErrorCodes.Forbidden, "WidgetErrorForbidden");

			var summary = await _importService.GetImportSummaryAsync(context.DepartmentId, cancellationToken) ?? new DepartmentImportSummary();
			var current = summary.Current;
			return new AdminImportWidgetData
			{
				OpenWorkspaces = summary.OpenWorkspaces,
				ActivatedWorkspaces = summary.ActivatedWorkspaces,
				FinishedWorkspaces = summary.FinishedWorkspaces,
				LastActivityOn = summary.LastActivityOn,
				Current = current == null || current.DepartmentId != context.DepartmentId ? null : new AdminImportWorkspace
				{
					WorkspaceId = current.Id,
					Name = current.Name,
					Status = Enum.GetName(typeof(ImportWorkspaceStatus), current.Status) ?? current.Status.ToString(),
					LastActivityOn = current.LastActivityOn,
					TargetActivationDate = current.TargetActivationDate,
					Datasets = summary.Datasets,
					NeedingReview = summary.DatasetsNeedingReview,
					Ready = summary.DatasetsReady,
					Imported = summary.DatasetsImported,
					Failed = summary.DatasetsFailed,
					RowsTotal = summary.RowsTotal,
					RowsImported = summary.RowsImported,
					RowsWithWarnings = summary.RowsWithWarnings,
					RowsBlocked = summary.RowsBlocked,
					RunsActive = summary.RunsActive,
					RunsFailed = summary.RunsFailed
				}
			};
		}
	}
}
