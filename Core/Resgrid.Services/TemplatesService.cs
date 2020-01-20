using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class TemplatesService : ITemplatesService
	{
		private readonly IGenericDataRepository<CallQuickTemplate> _callQuickTemplateRepository;

		public TemplatesService(IGenericDataRepository<CallQuickTemplate> callQuickTemplateRepository)
		{
			_callQuickTemplateRepository = callQuickTemplateRepository;
		}

		public List<CallQuickTemplate> GetAllCallQuickTemplatesForDepartment(int departmentId)
		{
			return _callQuickTemplateRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public CallQuickTemplate SaveCallQuickTemplate(CallQuickTemplate template)
		{
			_callQuickTemplateRepository.SaveOrUpdate(template);

			return template;
		}

		public CallQuickTemplate GetCallQuickTemplateById(int id)
		{
			return _callQuickTemplateRepository.GetAll().FirstOrDefault(x => x.CallQuickTemplateId == id);
		}

		public void DeleteCallQuickTemplate(int id)
		{
			var template = GetCallQuickTemplateById(id);
			_callQuickTemplateRepository.DeleteOnSubmit(template);
		}
	}
}
