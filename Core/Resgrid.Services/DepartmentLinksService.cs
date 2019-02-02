using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DepartmentLinksService: IDepartmentLinksService
	{
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly IGenericDataRepository<DepartmentLink> _departmentLinksRepository;
		private readonly IGenericDataRepository<Department> _departmentsRepository;
		private readonly ICacheProvider _cacheProvider;

		public DepartmentLinksService(IGenericDataRepository<DepartmentLink> departmentLinksRepository, IGenericDataRepository<Department> departmentsRepository, ICacheProvider cacheProvider)
		{
			_departmentLinksRepository = departmentLinksRepository;
			_departmentsRepository = departmentsRepository;
			_cacheProvider = cacheProvider;
		}

		public List<DepartmentLink> GetAllLinksForDepartment(int departmentId)
		{
			var links = (from dl in _departmentLinksRepository.GetAll()
				where dl.DepartmentId == departmentId || dl.LinkedDepartmentId == departmentId
				select dl).ToList();

			return links;
		}

		public DepartmentLink GetLinkById(int linkId)
		{
			return _departmentLinksRepository.GetAll().FirstOrDefault(x => x.DepartmentLinkId == linkId);
		}

		public DepartmentLink Save(DepartmentLink link)
		{
			_departmentLinksRepository.SaveOrUpdate(link);

			return link;
		}

		public bool DoesLinkExist(int departmentId, int departmentLinkId)
		{
			return (from dl in _departmentLinksRepository.GetAll()
				where dl.DepartmentId == departmentId && dl.DepartmentLinkId == departmentLinkId
				select dl).Any();
		}

		public Department GetDepartmentByLinkCode(string code)
		{
			return (from dl in _departmentsRepository.GetAll()
				where dl.LinkCode == code
				select dl).FirstOrDefault();
		}
	}
}