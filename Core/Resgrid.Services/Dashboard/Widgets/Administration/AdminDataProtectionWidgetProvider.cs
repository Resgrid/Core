using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Administration;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.dataProtection (plan §7.11, §9A.5): the Data Protection page's state header (policy row, cached) and, only while a
	/// migration is in flight, its progress panel (GetMigrationProgressAsync reads the engine's cursor rows). State only.
	/// </summary>
	[DashboardWidget("admin.dataProtection")]
	public class AdminDataProtectionWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IDepartmentDataProtectionService _dataProtectionService;

		public AdminDataProtectionWidgetProvider(IDepartmentDataProtectionService dataProtectionService)
		{
			_dataProtectionService = dataProtectionService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(departmentId);
			var state = policy == null ? DepartmentDataProtectionState.Disabled : (DepartmentDataProtectionState)policy.State;
			var data = new AdminDataProtectionWidgetData
			{
				State = Enum.GetName(typeof(DepartmentDataProtectionState), state) ?? state.ToString(),
				Enforced = context.Viewer?.AdpEnforced ?? await _dataProtectionService.IsProtectionEnforcedAsync(departmentId),
				CatalogVersion = policy?.CatalogVersion ?? 0,
				StepUpWindowMinutes = policy?.StepUpWindowMinutes ?? 0,
				OffboardingEffectiveOn = policy?.OffboardingEffectiveOn
			};

			if (state == DepartmentDataProtectionState.Disabled)
				return data;

			data.CatalogUpgradePending = await _dataProtectionService.IsCatalogUpgradePendingAsync(departmentId);
			if (policy?.ActiveMigrationKind != null || IsMigrating(state))
			{
				var progress = await _dataProtectionService.GetMigrationProgressAsync(departmentId, cancellationToken);
				if (progress != null && progress.IsRunning)
				{
					data.MigrationRunning = true;
					data.MigrationKind = progress.KindName;
					data.PercentComplete = progress.PercentComplete;
					data.RowsTotal = progress.RowsTotal;
					data.RowsCompleted = progress.RowsCompleted;
					data.RowsAnomalous = progress.RowsAnomalous;
				}
			}

			return data;
		}

		private static bool IsMigrating(DepartmentDataProtectionState state) =>
			state is DepartmentDataProtectionState.Encrypting or DepartmentDataProtectionState.Verifying or DepartmentDataProtectionState.Rotating
				or DepartmentDataProtectionState.Decrypting;
	}
}
