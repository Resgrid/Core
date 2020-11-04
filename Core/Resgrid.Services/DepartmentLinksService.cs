using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DepartmentLinksService: IDepartmentLinksService
	{
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly IDepartmentLinksRepository _departmentLinksRepository;
		private readonly IDepartmentsRepository _departmentsRepository;
		private readonly ICacheProvider _cacheProvider;

		public DepartmentLinksService(IDepartmentLinksRepository departmentLinksRepository, IDepartmentsRepository departmentsRepository, ICacheProvider cacheProvider)
		{
			_departmentLinksRepository = departmentLinksRepository;
			_departmentsRepository = departmentsRepository;
			_cacheProvider = cacheProvider;
		}

		public async Task<List<DepartmentLink>> GetAllLinksForDepartmentAsync(int departmentId)
		{
			var links = await _departmentLinksRepository.GetAllLinksForDepartmentAsync(departmentId);

			return links.ToList();
		}

		public async Task<DepartmentLink> GetLinkByIdAsync(int linkId)
		{
			return await _departmentLinksRepository.GetByIdAsync(linkId);
		}

		public async Task<DepartmentLink> SaveAsync(DepartmentLink link, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _departmentLinksRepository.SaveOrUpdateAsync(link, cancellationToken);
		}

		public async Task<bool> DoesLinkExistAsync(int departmentId, int departmentLinkId)
		{
			return (from dl in await _departmentLinksRepository.GetAllLinksForDepartmentAndIdAsync(departmentId, departmentLinkId)
				select dl).Any();
		}

		public async Task<Department> GetDepartmentByLinkCodeAsync(string code)
		{
			return await _departmentsRepository.GetByLinkCodeAsync(code);
		}
	}
}
