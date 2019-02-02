using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface ICustomStateService
	{
		CustomState GetCustomSateById(int customStateId);
		List<CustomState> GetAllActiveCustomStatesForDepartment(int departmentId);
		CustomState Save(CustomState customState);
		void Delete(CustomState customState);
		void DeleteDetail(CustomStateDetail detail);
		CustomState Update(CustomState state, List<CustomStateDetail> details);
		CustomStateDetail GetCustomDetailForDepartment(int departmentId, int detailId);
		List<CustomState> GetAllCustomStatesForDepartment(int departmentId);
		List<CustomState> GetAllActiveUnitStatesForDepartment(int departmentId);
		CustomState GetActivePersonnelStateForDepartment(int departmentId);
		CustomState GetActiveStaffingLevelsForDepartment(int departmentId);
		CustomStateDetail SaveDetail(CustomStateDetail customStateDetail);
		CustomStateDetail GetCustomDetailById(int detailId);
		void InvalidateCustomStateInCache(int departmentId);
		CustomStateDetail GetCustomPersonnelStaffing(int departmentId, UserState state);
		CustomStateDetail GetCustomPersonnelStatus(int departmentId, ActionLog state);
		CustomStateDetail GetCustomUnitState(UnitState state);
	}
}