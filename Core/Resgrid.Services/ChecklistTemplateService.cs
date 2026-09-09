using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ChecklistTemplateService : IChecklistTemplateService
	{
		private readonly IReadinessAccessService _access;
		public ChecklistTemplateService(IReadinessAccessService access) => _access = access;

		public async Task<IReadOnlyList<ChecklistTemplate>> SearchAsync(int departmentId, string query = null)
		{
			if (!await _access.CanUseChecklistsAsync(departmentId))
				return null;
			if (query?.Length > 256)
				throw new ArgumentException("Search must be 256 characters or fewer.", nameof(query));
			return ChecklistTemplateCatalog.Search(query);
		}

		public async Task<ChecklistTemplate> GetByIdAsync(int departmentId, string templateId)
		{
			if (!await _access.CanUseChecklistsAsync(departmentId))
				return null;
			return ChecklistTemplateCatalog.GetById(templateId);
		}
	}
}
