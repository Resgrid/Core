using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DistributionListsService : IDistributionListsService
	{
		private readonly IDistributionListRepository _distributionListRepository;
		private readonly IDistributionListMemberRepository _distributionListMemberRepository;

		public DistributionListsService(IDistributionListRepository distributionListRepository, IDistributionListMemberRepository distributionListMemberRepository)
		{
			_distributionListRepository = distributionListRepository;
			_distributionListMemberRepository = distributionListMemberRepository;
		}

		public async Task<List<DistributionList>> GetAllAsync()
		{
			var items = await _distributionListRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<DistributionList>();
		}

		public async Task<DistributionList> GetDistributionListByIdAsync(int distributionListId)
		{
			return await _distributionListRepository.GetDistributionListByIdAsync(distributionListId);
		}

		public async Task<List<DistributionList>> GetDistributionListsByDepartmentIdAsync(int departmentId)
		{
			var items = await _distributionListRepository.GetDispatchProtocolsByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<DistributionList>();
		}

		public async Task<DistributionList> GetDistributionListByAddressAsync(string emailAddress)
		{
			return await _distributionListRepository.GetDistributionListByEmailAddressAsync(emailAddress);
		}

		public async Task<bool> DeleteDistributionListsByIdAsync(int distributionListId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var list = await GetDistributionListByIdAsync(distributionListId);
			return await _distributionListRepository.DeleteAsync(list, cancellationToken);
		}

		public async Task<DistributionList> SaveDistributionListAsync(DistributionList distributionList, CancellationToken cancellationToken = default(CancellationToken))
		{
			var savedList = await _distributionListRepository.SaveOrUpdateAsync(distributionList, cancellationToken);

			if (distributionList.Members != null && distributionList.Members.Any())
			{
				foreach (var distributionListMember in distributionList.Members)
				{
					distributionListMember.DistributionListId = savedList.DistributionListId;
					await _distributionListMemberRepository.SaveOrUpdateAsync(distributionListMember, cancellationToken);
				}
			}

			return savedList;
		}

		public async Task<DistributionList> SaveDistributionListOnlyAsync(DistributionList distributionList, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _distributionListRepository.SaveOrUpdateAsync(distributionList, cancellationToken);
		}

		public async Task<List<DistributionList>> GetAllActiveDistributionListsAsync()
		{
			var items = await _distributionListRepository.GetAllActiveDistributionListsAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<DistributionList>();
		}

		public async Task<List<DistributionListMember>> GetAllListMembersByListIdAsync(int listId)
		{
			var items = await _distributionListMemberRepository.GetDistributionListMemberByListIdAsync(listId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<DistributionListMember>();
		}

		public async Task<bool> RemoveUserFromAllListsAsync(string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var members = await _distributionListMemberRepository.GetDistributionListMemberByUserIdAsync(userId);

			foreach (var member in members)
			{
				await _distributionListMemberRepository.DeleteAsync(member, cancellationToken);
			}

			return true;
		}
	}
}
