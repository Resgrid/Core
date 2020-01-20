using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface ITemplatesService
	{
		List<CallQuickTemplate> GetAllCallQuickTemplatesForDepartment(int departmentId);
		CallQuickTemplate SaveCallQuickTemplate(CallQuickTemplate template);
		CallQuickTemplate GetCallQuickTemplateById(int id);
		void DeleteCallQuickTemplate(int id);
	}
}
