using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Administration;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Administration
{
	/// <summary>department.info — the reference widget provider (home-dashboard-implementation-contract.md §2).</summary>
	[DashboardWidget("department.info")]
	public class DepartmentInfoWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public DepartmentInfoWidgetProvider(IDepartmentsService departmentsService, IDepartmentSettingsService departmentSettingsService)
		{
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(context.DepartmentId);

			return new DepartmentInfoWidgetData
			{
				DepartmentId = context.DepartmentId,
				DepartmentCode = department?.Code,
				TextToCallNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(context.DepartmentId)
			};
		}
	}
}
