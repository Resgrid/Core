using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICallTypesRepository : IRepository<CallType>
	{
		Task<List<CallType>> GetCallTypesForDepartmentAsync(int departmentId);
	}
}
