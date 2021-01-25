using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class TemplatesService : ITemplatesService
	{
		private readonly ICallQuickTemplateRepository _callQuickTemplateRepository;

		public TemplatesService(ICallQuickTemplateRepository callQuickTemplateRepository)
		{
			_callQuickTemplateRepository = callQuickTemplateRepository;
		}

		public async Task<List<CallQuickTemplate>> GetAllCallQuickTemplatesForDepartmentAsync(int departmentId)
		{
			var items = await _callQuickTemplateRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<CallQuickTemplate>();
		}

		public async Task<CallQuickTemplate> SaveCallQuickTemplateAsync(CallQuickTemplate template, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _callQuickTemplateRepository.SaveOrUpdateAsync(template, cancellationToken);
		}

		public async Task<CallQuickTemplate> GetCallQuickTemplateByIdAsync(int id)
		{
			return await _callQuickTemplateRepository.GetByIdAsync(id);
		}

		public async Task<bool> DeleteCallQuickTemplateAsync(int id, CancellationToken cancellationToken = default(CancellationToken))
		{
			var template = await GetCallQuickTemplateByIdAsync(id);
			return await _callQuickTemplateRepository.DeleteAsync(template, cancellationToken);
		}
	}
}
