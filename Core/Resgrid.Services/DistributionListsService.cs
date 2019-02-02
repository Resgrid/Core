using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DistributionListsService : IDistributionListsService
	{
		private readonly IGenericDataRepository<DistributionList> _distributionListRepository;
		private readonly IGenericDataRepository<DistributionListMember> _distributionListMemberRepository;

		public DistributionListsService(IGenericDataRepository<DistributionList> distributionListRepository, IGenericDataRepository<DistributionListMember> distributionListMemberRepository)
		{
			_distributionListRepository = distributionListRepository;
			_distributionListMemberRepository = distributionListMemberRepository;
		}

		public DistributionList GetDistributionListById(int distributionListId)
		{
			return _distributionListRepository.GetAll().FirstOrDefault(x => x.DistributionListId == distributionListId);
		}

		public List<DistributionList> GetDistributionListsByDepartmentId(int departmentId)
		{
			return _distributionListRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public DistributionList GetDistributionListByAddress(string emailAddress)
		{
			return _distributionListRepository.GetAll().FirstOrDefault(x => x.EmailAddress == emailAddress);
		}

		public void DeleteDistributionListsById(int distributionListId)
		{
			var list = GetDistributionListById(distributionListId);
			_distributionListRepository.DeleteOnSubmit(list);
		}

		public DistributionList SaveDistributionList(DistributionList distributionList)
		{
			if (distributionList.DistributionListId == 0)
			{
				_distributionListRepository.SaveOrUpdate(distributionList);
			}
			else
			{
				var members = (from m in _distributionListMemberRepository.GetAll()
											 where m.DistributionListId == distributionList.DistributionListId
											 select m).AsEnumerable();

				_distributionListMemberRepository.DeleteAll(members);

				_distributionListRepository.SaveOrUpdate(distributionList);
			}

			return distributionList;
		}

		public DistributionList SaveDistributionListOnly(DistributionList distributionList)
		{
			_distributionListRepository.SaveOrUpdate(distributionList);

			return distributionList;
		}

		public List<DistributionList> GetAllActiveDistributionLists()
		{
			return _distributionListRepository.GetAll().Where(x => x.IsDisabled == false).ToList();
		}

		public List<DistributionListMember> GetAllListMembersByListId(int listId)
		{
			return _distributionListMemberRepository.GetAll().Where(x => x.DistributionListId == listId).ToList();
		}

		public void RemoveUserFromAllLists(string userId)
		{
			var members = _distributionListMemberRepository.GetAll().Where(x => x.UserId == userId).ToList();

			foreach (var member in members)
			{
				_distributionListMemberRepository.DeleteOnSubmit(member);
			}
		}
	}
}