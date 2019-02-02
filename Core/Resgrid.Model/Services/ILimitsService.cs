namespace Resgrid.Model.Services
{
	public interface ILimitsService
	{
		bool ValidateDepartmentIsWithinLimits(int departmentId);
		bool CanDepartentAddNewUser(int departmentId);
		bool CanDepartentAddNewGroup(int departmentId);
		int GetPersonnelLimitForDepartment(int departmentId, Plan plan = null);
		int GetGroupslLimitForDepartment(int departmentId, Plan plan = null);
		bool CanDepartentAddNewRole(int departmentId);
		int GetRoleslLimitForDepartment(int departmentId);
		bool CanDepartentAddNewUnit(int departmentId);
		int GetUnitslLimitForDepartment(int departmentId, Plan plan = null);
		bool CanDepartmentProvisionNumber(int departmentId);
		bool CanDepartmentUseVoice(int departmentId);
		bool CanDepartmentUseLinks(int departmentId);
		bool CanDepartmentCreateOrders(int departmentId);
	}
}