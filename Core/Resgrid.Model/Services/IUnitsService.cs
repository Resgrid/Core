using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IUnitsService
	{
		List<Unit> GetAll();
		List<Unit> GetUnitsForDepartment(int departmentId);
		List<UnitType> GetUnitTypesForDepartment(int departmentId);
		UnitType AddUnitType(int departmentId, string name);
		UnitType GetUnitTypeById(int unitTypeId);
		void DeleteUnitType(int unitTypeId);
		Unit SaveUnit(Unit unit);
		UnitState GetLastUnitStateByUnitId(int unitId);
		List<UnitState> GetAllLatestStatusForUnitsByDepartmentId(int departmentId);
		UnitState SetUnitState(int unitId, int unitStateType);
		Unit GetUnitById(int unitId);
		void DeleteUnit(int unitId);
		Unit GetUnitByNameDepartmentId(int departmentId, string name);
		UnitLog SaveUnitLog(UnitLog unitLog);
		List<UnitLog> GetLogsForUnit(int unitId);
		List<Unit> GetUnitsForDepartmentUnlimited(int departmentId);
		UnitState SetUnitState(UnitState state, int departmentId = 0);
		List<UnitState> GetAllStatesForUnit(int unitId);
		List<UnitRole> GetRolesForUnit(int unitId);
		List<UnitRole> SetRolesForUnit(int unitId, List<UnitRole> roles);
		void ClearRolesForUnit(int unitId);
		UnitRole GetRoleById(int unitRoleId);
		void AddUnitStateRoleForEvent(int unitStateId, string userId, int roleId, string unitName, DateTime timestamp, string roleName = "Unknown");
		UnitState GetUnitStateById(int unitStateId);
		UnitState GetLastUnitStateBeforeId(int unitId, int unitStateId);
		void ClearGroupForUnits(int departmentGroupId);
		void DeleteStatesForUnit(int unitId);
		UnitType AddUnitType(int departmentId, string name, int customStatesId);
		UnitType SaveUnitType(UnitType unitType);
		void AddAllUnitStateRoles(List<UnitStateRole> roles);
		List<Unit> GetAllUnitsForType(int departmentId, string type);
		List<Unit> GetAllUnitsForGroup(int groupId);
		List<UnitState> GetUnitStatesForCall(int departmentId, int callId);
		UnitLocation AddUnitLocation(UnitLocation location);
		UnitLocation GetLatestUnitLocation(int unitId, DateTime? timestamp = null);
		UnitType GetUnitTypeByName(int departmentId, string type);
		List<UnitStateRole> GetCurrentRolesForUnit(int unitId);
	}
}
