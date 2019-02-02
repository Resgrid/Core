using System;
using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IUnitStatesRepository : IRepository<UnitState>
	{
		List<UnitState> GetLatestUnitStatesForDepartment(int departmentId);
		List<UnitStateRole> GetCurrentRolesForUnit(int unitId);
	}
}
