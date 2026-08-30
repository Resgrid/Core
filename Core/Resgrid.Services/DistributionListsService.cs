using System;
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
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;
		private readonly IDistributionListMemberRepository _distributionListMemberRepository;

		public DistributionListsService(IDistributionListRepository distributionListRepository,
			IDistributionListMemberRepository distributionListMemberRepository,
			Lazy<IProtectedWriteService> protectedWriteService)
		{
			_protectedWriteService = protectedWriteService;
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
			DistributionList existing = null;
			if (distributionList != null && distributionList.DistributionListId > 0)
				existing = await _distributionListRepository.GetByIdAsync(distributionList.DistributionListId);

			// ADP write safety net (plan 4.2/19.2, catalog v9).
			// An UPDATE already has its identity, so it is enveloped BEFORE the save and no plaintext
			// version of a cataloged field ever reaches the table - the same split CertificationService
			// uses. Only an INSERT has to be persisted first, because the AAD row key IS the identity
			// pk and cannot be bound until the database assigns it (plan 4.2/19.2). Fails closed
			// either way.
			var isExistingRow = distributionList != null && distributionList.DistributionListId > 0;

			if (isExistingRow)
			{
				var preSaveWrite = await _protectedWriteService.Value.PrepareDistributionListWriteAsync(
					distributionList.DepartmentId, distributionList, existing, null, null, workloadCaller: true, cancellationToken);
				if (!preSaveWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({preSaveWrite.Reason}); distribution list {distributionList.DistributionListId} was NOT saved.");
			}

			var savedList = await _distributionListRepository.SaveOrUpdateAsync(distributionList, cancellationToken);

			if (!isExistingRow)
			{
				var protectedWrite = await _protectedWriteService.Value.PrepareDistributionListWriteAsync(
					savedList.DepartmentId, savedList, existing, null, null, workloadCaller: true, cancellationToken);
				if (!protectedWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); distribution list {savedList.DistributionListId} has transient plaintext credentials pending re-encryption.");
				if (protectedWrite.Changed)
					savedList = await _distributionListRepository.SaveOrUpdateAsync(savedList, cancellationToken);
			}

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

		public async Task<bool> RemoveUserFromAllListsInDepartmentAsync(string userId, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var members = await _distributionListMemberRepository.GetDistributionListMemberByUserIdAsync(userId);

			if (members == null || !members.Any())
				return true;

			var departmentLists = await GetDistributionListsByDepartmentIdAsync(departmentId);
			var departmentListIds = departmentLists.Select(x => x.DistributionListId).ToHashSet();

			foreach (var member in members.Where(x => departmentListIds.Contains(x.DistributionListId)))
			{
				await _distributionListMemberRepository.DeleteAsync(member, cancellationToken);
			}

			return true;
		}
	}
}
