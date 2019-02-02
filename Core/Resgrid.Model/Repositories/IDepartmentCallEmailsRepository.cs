using System;
using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentCallEmailsRepository : IRepository<DepartmentCallEmail>
	{
		List<DepartmentCallEmail> GetAllDepartmentEmailSettings();
	}
}