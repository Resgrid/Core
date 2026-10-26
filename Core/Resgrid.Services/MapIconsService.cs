using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class MapIconsService : IMapIconsService
	{
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ITypeIconsService _typeIconsService;

		public MapIconsService(ICallsService callsService, IUnitsService unitsService, ICustomStateService customStateService,
			IPersonnelRolesService personnelRolesService, ITypeIconsService typeIconsService)
		{
			_callsService = callsService;
			_unitsService = unitsService;
			_customStateService = customStateService;
			_personnelRolesService = personnelRolesService;
			_typeIconsService = typeIconsService;
		}

		public async Task<MapIconResolver> GetResolverAsync(int departmentId, IEnumerable<UnitState> latestUnitStates = null)
		{
			return new MapIconResolver(departmentId,
				await _callsService.GetCallTypesForDepartmentAsync(departmentId),
				// Deleted priorities included, so an older call keeps the meaning of the priority it was given.
				await _callsService.GetCallPrioritiesForDepartmentAsync(departmentId),
				await _unitsService.GetUnitTypesForDepartmentAsync(departmentId),
				await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId),
				await _personnelRolesService.GetRolesForDepartmentAsync(departmentId),
				await _typeIconsService.GetVariantsForDepartmentAsync(departmentId),
				await _customStateService.GetDefaultPersonnelStatusAsync(departmentId),
				latestUnitStates);
		}

		public async Task<MapIconLegend> GetLegendAsync(int departmentId, string noCallTypeName, string noUnitTypeName, string noRoleName)
		{
			return MapIconLegendBuilder.Build(await GetResolverAsync(departmentId),
				await _callsService.GetActiveCallPrioritiesForDepartmentAsync(departmentId),
				await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId),
				_customStateService.GetDefaultUnitStatuses(),
				await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(departmentId),
				await _personnelRolesService.GetRolesForDepartmentAsync(departmentId),
				noCallTypeName, noUnitTypeName, noRoleName);
		}
	}
}
