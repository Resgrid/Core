using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard
{
	[DashboardOptionSource(DashboardOptionSources.Groups)]
	public class DashboardGroupsOptionSource : IDashboardOptionSource
	{
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public DashboardGroupsOptionSource(IDepartmentGroupsService departmentGroupsService)
		{
			_departmentGroupsService = departmentGroupsService;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>();
			return groups.OrderBy(g => g.Name).Select(g => new KeyValuePair<string, string>(g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), g.Name)).ToList();
		}
	}

	[DashboardOptionSource(DashboardOptionSources.Stations)]
	public class DashboardStationsOptionSource : IDashboardOptionSource
	{
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public DashboardStationsOptionSource(IDepartmentGroupsService departmentGroupsService)
		{
			_departmentGroupsService = departmentGroupsService;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>();
			return stations.OrderBy(g => g.Name).Select(g => new KeyValuePair<string, string>(g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), g.Name)).ToList();
		}
	}

	[DashboardOptionSource(DashboardOptionSources.UnitTypes)]
	public class DashboardUnitTypesOptionSource : IDashboardOptionSource
	{
		private readonly IUnitsService _unitsService;

		public DashboardUnitTypesOptionSource(IUnitsService unitsService)
		{
			_unitsService = unitsService;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			var types = await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>();
			return types.OrderBy(t => t.Type).Select(t => new KeyValuePair<string, string>(t.UnitTypeId.ToString(CultureInfo.InvariantCulture), t.Type)).ToList();
		}
	}

	[DashboardOptionSource(DashboardOptionSources.CallPriorities)]
	public class DashboardCallPrioritiesOptionSource : IDashboardOptionSource
	{
		private readonly ICallsService _callsService;

		public DashboardCallPrioritiesOptionSource(ICallsService callsService)
		{
			_callsService = callsService;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
			return priorities.Where(p => !p.IsDeleted)
				.Select(p => new KeyValuePair<string, string>(p.DepartmentCallPriorityId.ToString(CultureInfo.InvariantCulture), p.Name)).ToList();
		}
	}

	[DashboardOptionSource(DashboardOptionSources.MapVisualizations)]
	public class DashboardMapVisualizationsOptionSource : IDashboardOptionSource
	{
		private readonly IMapVisualizationService _mapVisualizationService;

		public DashboardMapVisualizationsOptionSource(IMapVisualizationService mapVisualizationService)
		{
			_mapVisualizationService = mapVisualizationService;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			var saved = await _mapVisualizationService.GetVisibleForUserAsync(departmentId, userId) ?? new List<MapVisualization>();
			return saved.OrderBy(v => v.Name).Select(v => new KeyValuePair<string, string>(v.MapVisualizationId, v.Name)).ToList();
		}
	}
}
