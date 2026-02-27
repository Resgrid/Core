﻿using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWorkflowRepository : IRepository<Workflow>
	{
		Task<IEnumerable<Workflow>> GetAllActiveByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType);
		Task<IEnumerable<Workflow>> GetAllByDepartmentIdAsync(int departmentId);
		Task<Workflow> GetByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType);
	}
}

