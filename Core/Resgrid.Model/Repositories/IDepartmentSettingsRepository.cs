using System;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentSettingsRepository : IRepository<DepartmentSetting>
	{
		DepartmentSetting GetDepartmentSettingByUserIdType(string userId, DepartmentSettingTypes type);
		DepartmentSetting GetDepartmentSettingByIdType(int departmentId, DepartmentSettingTypes type);
		DepartmentSetting GetDepartmentSettingBySettingType(string setting, DepartmentSettingTypes type);
	}
}
