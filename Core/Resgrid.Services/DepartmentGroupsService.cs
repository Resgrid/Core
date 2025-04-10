using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Model.Identity;
using System.Transactions;

namespace Resgrid.Services
{
	public class DepartmentGroupsService : IDepartmentGroupsService
	{
		private static string CacheKey = "DepartmentGroup_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly IDepartmentGroupsRepository _departmentGroupsRepository;
		private readonly IDepartmentGroupMembersRepository _departmentGroupMembersRepository;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IAddressService _addressService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICacheProvider _cacheProvider;
		private readonly IIdentityRepository _identityRepository;

		public DepartmentGroupsService(IDepartmentGroupsRepository departmentGroupsRepository, IDepartmentGroupMembersRepository departmentGroupMembersRepository,
			ISubscriptionsService subscriptionsService, IAddressService addressService, IDepartmentsService departmentsService, IGeoLocationProvider geoLocationProvider,
			IDepartmentSettingsService departmentSettingsService, IEventAggregator eventAggregator, ICacheProvider cacheProvider,
			IIdentityRepository identityRepository)
		{
			_departmentGroupsRepository = departmentGroupsRepository;
			_departmentGroupMembersRepository = departmentGroupMembersRepository;
			_subscriptionsService = subscriptionsService;
			_addressService = addressService;
			_departmentsService = departmentsService;
			_geoLocationProvider = geoLocationProvider;
			_departmentSettingsService = departmentSettingsService;
			_eventAggregator = eventAggregator;
			_cacheProvider = cacheProvider;
			_identityRepository = identityRepository;
		}

		public async Task<List<DepartmentGroup>> GetAllAsync()
		{
			var items = await _departmentGroupsRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<DepartmentGroup>();
		}

		public async Task<DepartmentGroup> SaveAsync(DepartmentGroup departmentGroup, CancellationToken cancellationToken = default(CancellationToken))
		{
			// New or Updated Address for this group
			if (departmentGroup.Address != null)
			{
				var address = await _addressService.SaveAddressAsync(departmentGroup.Address, cancellationToken);
				if (address != null)
				{
					departmentGroup.AddressId = address.AddressId;
				}
			}

			var saved = await _departmentGroupsRepository.SaveOrUpdateAsync(departmentGroup, cancellationToken);
			await InvalidateGroupInCache(departmentGroup.DepartmentGroupId);

			return saved;
		}

		public async Task<List<DepartmentGroup>> GetAllGroupsForDepartmentAsync(int departmentId)
		{
			//List<DepartmentGroup> departmentGroups = new List<DepartmentGroup>();

			//var groups = await GetAllGroupsForDepartmentUnlimitedAsync(departmentId);

			//int limit = 0;
			//if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
			//	limit = int.MaxValue;
			//else
			//	limit = (await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId)).GetLimitForTypeAsInt(PlanLimitTypes.Groups);

			//int count = groups.Count < limit ? groups.Count : limit;

			//// Only return users up to the plans group limit
			//for (int i = 0; i < count; i++)
			//{
			//	departmentGroups.Add(groups[i]);
			//}

			var departmentGroups = await GetAllGroupsForDepartmentUnlimitedAsync(departmentId);

			foreach (var group in departmentGroups)
			{
				if (group.ParentDepartmentGroupId.HasValue)
				{
					group.Parent = await GetGroupByIdAsync(group.ParentDepartmentGroupId.Value, false);
				}

				var childGroups = await _departmentGroupsRepository.GetAllGroupsByParentGroupIdAsync(group.DepartmentGroupId);

				if (childGroups != null && childGroups.Any())
					group.Children = childGroups.ToList();
				else
					group.Children = new List<DepartmentGroup>();
			}

			return departmentGroups;
		}

		public async Task InvalidateGroupInCache(int groupId)
		{
			await _cacheProvider.RemoveAsync(string.Format(CacheKey, groupId));
		}

		public async Task<List<DepartmentGroup>> GetAllGroupsForDepartmentUnlimitedAsync(int departmentId)
		{
			var groups = await _departmentGroupsRepository.GetAllGroupsByDepartmentIdAsync(departmentId);

			foreach (var g in groups)
			{
				if (g.AddressId.HasValue && g.Address == null)
					g.Address = await _addressService.GetAddressByIdAsync(g.AddressId.Value);

				if (g.ParentDepartmentGroupId.HasValue)
				{
					g.Parent = await GetGroupByIdAsync(g.ParentDepartmentGroupId.Value, false);
				}

				var childGroups = await _departmentGroupsRepository.GetAllGroupsByParentGroupIdAsync(g.DepartmentGroupId);

				if (childGroups != null && childGroups.Any())
					g.Children = childGroups.ToList();
				else
					g.Children = new List<DepartmentGroup>();
			}

			return groups.ToList();
		}

		public async Task<List<DepartmentGroup>> GetAllGroupsForDepartmentUnlimitedThinAsync(int departmentId)
		{
			var groups = await _departmentGroupsRepository.GetAllGroupsByDepartmentIdAsync(departmentId);

			return groups.ToList();
		}

		public async Task<DepartmentGroup> GetGroupByIdAsync(int departmentGroupId, bool bypassCache = true)
		{
			async Task<DepartmentGroup> getDepartmentGroup()
			{
				var group1 = await _departmentGroupsRepository.GetGroupByGroupIdAsync(departmentGroupId);

				if (group1 != null)
				{
					if (group1.AddressId.HasValue && group1.Address == null)
						group1.Address = await _addressService.GetAddressByIdAsync(group1.AddressId.Value);

					if (group1.ParentDepartmentGroupId.HasValue)
					{
						group1.Parent = await GetGroupByIdAsync(group1.ParentDepartmentGroupId.Value, true);
					}

					var childGroups = await _departmentGroupsRepository.GetAllGroupsByParentGroupIdAsync(group1.DepartmentGroupId);

					if (childGroups != null && childGroups.Any())
						group1.Children = childGroups.ToList();
					else
						group1.Children = new List<DepartmentGroup>();
				}

				return group1;
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync(string.Format(CacheKey, departmentGroupId), getDepartmentGroup, CacheLength);
			}

			return await getDepartmentGroup();
		}

		public async Task<bool> DeleteGroupByIdAsync(int groupId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var group = await GetGroupByIdAsync(groupId);
			var members = await _departmentGroupMembersRepository.GetAllGroupMembersByGroupIdAsync(groupId);

			foreach (var departmentGroupMember in members)
			{
				await _departmentGroupMembersRepository.DeleteAsync(departmentGroupMember, cancellationToken);
			}

			await _departmentGroupsRepository.DeleteAsync(group, cancellationToken);
			await InvalidateGroupInCache(groupId);

			return true;
		}

		public async Task<DepartmentGroup> UpdateAsync(DepartmentGroup departmentGroup, CancellationToken cancellationToken = default(CancellationToken))
		{
			var members =
				await _departmentGroupMembersRepository.GetAllGroupMembersByGroupIdAsync(departmentGroup
					.DepartmentGroupId);

			if (departmentGroup.Members != null && departmentGroup.Members.Any())
			{
				var membersNoLongerInGroup = members.Where(p => !departmentGroup.Members.Any(p2 => p2.DepartmentGroupMemberId == p.DepartmentGroupMemberId));

				foreach (var departmentGroupMember in membersNoLongerInGroup)
				{
					await _departmentGroupMembersRepository.DeleteAsync(departmentGroupMember, cancellationToken);
				}

				foreach (var newMember in departmentGroup.Members)
				{
					if (!members.Any(x => x.UserId == newMember.UserId))
					{
						newMember.DepartmentGroupId = departmentGroup.DepartmentGroupId;
						await _departmentGroupMembersRepository.SaveOrUpdateAsync(newMember, cancellationToken);
					}
				}
			}
			else
			{
				foreach (var departmentGroupMember in members)
				{
					await _departmentGroupMembersRepository.DeleteAsync(departmentGroupMember, cancellationToken);
				}
			}

			if (departmentGroup.Address != null)
				await _addressService.SaveAddressAsync(departmentGroup.Address, cancellationToken);

			var saved = await _departmentGroupsRepository.SaveOrUpdateAsync(departmentGroup, cancellationToken, true);

			await InvalidateGroupInCache(departmentGroup.DepartmentGroupId);

			return saved;
		}

		public async Task<bool> DeleteAllMembersFromGroupAsync(DepartmentGroup departmentGroup, CancellationToken cancellationToken = default(CancellationToken))
		{
			var members =
				await _departmentGroupMembersRepository.GetAllGroupMembersByGroupIdAsync(departmentGroup
					.DepartmentGroupId);

			foreach (var departmentGroupMember in members)
			{
				await _departmentGroupMembersRepository.DeleteAsync(departmentGroupMember, cancellationToken);
			}

			await InvalidateGroupInCache(departmentGroup.DepartmentGroupId);

			return true;
		}

		public async Task<bool> IsUserInAGroupAsync(string userId, int departmentId)
		{
			var members =
				await _departmentGroupMembersRepository
					.GetAllGroupMembersByUserAndDepartmentAsync(userId, departmentId);

			if (members != null && members.Any())
				return true;

			return false;
		}

		public async Task<bool> IsUserAGroupAdminAsync(string userId, int departmentId)
		{
			var members = from m in await _departmentGroupMembersRepository.GetAllGroupMembersByUserAndDepartmentAsync(userId, departmentId)
						  where m.IsAdmin.HasValue && m.IsAdmin.Value && m.DepartmentId == departmentId
						  select m;

			if (members != null && members.Any())
				return true;

			return false;
		}

		public async Task<bool> IsUserInAGroupAsync(string userId, int excludedGroupId, int departmentId)
		{
			var members = from m in await _departmentGroupMembersRepository.GetAllGroupMembersByUserAndDepartmentAsync(userId, departmentId)
						  where m.DepartmentGroupId != excludedGroupId
						  select m;

			if (members != null && members.Any())
				return true;

			return false;
		}

		public async Task<bool> DeleteUserFromGroupsAsync(string userId, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var groupMemberships =
				await _departmentGroupMembersRepository
					.GetAllGroupMembersByUserAndDepartmentAsync(userId, departmentId);

			foreach (var membership in groupMemberships)
			{
				await _departmentGroupMembersRepository.DeleteAsync(membership, cancellationToken);
				await InvalidateGroupInCache(membership.DepartmentGroupId);
			}

			return true;
		}

		public async Task<List<DepartmentGroup>> GetAllChildDepartmentGroupsAsync(int parentDepartmentGroupId)
		{
			var groups = await _departmentGroupsRepository.GetAllGroupsByParentGroupIdAsync(parentDepartmentGroupId);

			return groups.ToList();
		}

		public async Task<List<DepartmentGroup>> GetAllStationGroupsForDepartmentAsync(int departmentId)
		{
			return await _departmentGroupsRepository.GetAllStationGroupsForDepartmentAsync(departmentId);
		}

		public async Task<DepartmentGroup> GetGroupForUserAsync(string userId, int departmentId)
		{
			var depMember =
				(await _departmentGroupMembersRepository.GetAllGroupMembersByUserAndDepartmentAsync(userId,
					departmentId)).FirstOrDefault();

			if (depMember == null)
				return null;

			//if (depMember.DepartmentGroup != null)
			//	return depMember.DepartmentGroup;

			return await GetGroupByIdAsync(depMember.DepartmentGroupId);
		}

		public async Task<DepartmentGroupMember> GetGroupMemberForUserAsync(string userId, int departmentId)
		{
			var depMember = (await _departmentGroupMembersRepository.GetAllGroupMembersByUserAndDepartmentAsync(userId, departmentId)).FirstOrDefault();

			return depMember;
		}

		public async Task<DepartmentGroupMember> SaveGroupMember(DepartmentGroupMember depMember, CancellationToken cancellationToken = default(CancellationToken))
		{
			await _departmentGroupMembersRepository.SaveOrUpdateAsync(depMember, cancellationToken);

			await InvalidateGroupInCache(depMember.DepartmentGroupId);

			return depMember;
		}

		public async Task<Dictionary<string, DepartmentGroup>> GetAllDepartmentGroupsForDepartmentAsync(int departmentId)
		{
			var data = new Dictionary<string, DepartmentGroup>();
			var departments = await _departmentGroupsRepository.GetAllGroupsByDepartmentIdAsync(departmentId);

			foreach (var departmentGroup in departments)
			{
				foreach (var departmentGroupMember in departmentGroup.Members)
				{
					if (!data.ContainsKey(departmentGroupMember.UserId))
						data.Add(departmentGroupMember.UserId, departmentGroup);
				}
			}

			return data;
		}

		public async Task<List<string>> AllGroupedUserIdsForDepartmentAsync(int departmentId)
		{
			var groups = await _departmentGroupsRepository.GetAllGroupsByDepartmentIdAsync(departmentId);

			return (from departmentGroup in groups from member in departmentGroup.Members select member.UserId).ToList();
		}

		public async Task<Coordinates> GetMapCenterCoordinatesForGroupAsync(int departmentGroupId)
		{
			Coordinates coordinates = null;

			var departmentGroup = await GetGroupByIdAsync(departmentGroupId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentGroup.DepartmentId);

			if (departmentGroup.Address != null)
			{
				coordinates = new Coordinates();
				string coordinateString = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", departmentGroup.Address.Address1,
				departmentGroup.Address.City, departmentGroup.Address.State, departmentGroup.Address.PostalCode,
				departmentGroup.Address.Country));

				var coords = coordinateString.Split(char.Parse(","));
				coordinates.Latitude = double.Parse(coords[0]);
				coordinates.Longitude = double.Parse(coords[1]);
			}

			if (coordinates == null && department.Address != null)
			{
				coordinates = new Coordinates();
				string coordinateString = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", department.Address.Address1,
				department.Address.City, department.Address.State, department.Address.PostalCode, department.Address.Country));

				var coords = coordinateString.Split(char.Parse(","));
				coordinates.Latitude = double.Parse(coords[0]);
				coordinates.Longitude = double.Parse(coords[1]);
			}

			var gpsCoordinates = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(departmentGroup.DepartmentId);
			if (coordinates == null && !string.IsNullOrWhiteSpace(gpsCoordinates))
			{
				coordinates = new Coordinates();

				var coords = gpsCoordinates.Split(char.Parse(","));
				coordinates.Latitude = double.Parse(coords[0]);
				coordinates.Longitude = double.Parse(coords[1]);
			}


			return coordinates;
		}

		public async Task<DepartmentGroupMember> MoveUserIntoGroupAsync(string userId, int groupId, bool isAdmin, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var departmentGroup = await GetGroupByIdAsync(groupId);
			if (departmentGroup == null || departmentGroup.DepartmentId != departmentId)
				return null;

			var depMember = (await _departmentGroupMembersRepository.GetAllGroupMembersByUserAndDepartmentAsync(userId, departmentId)).FirstOrDefault();

			if (depMember == null)
				depMember = new DepartmentGroupMember();

			depMember.DepartmentGroupId = groupId;
			depMember.DepartmentId = departmentId;
			depMember.IsAdmin = isAdmin;
			depMember.UserId = userId;

			var saved = await _departmentGroupMembersRepository.SaveOrUpdateAsync(depMember, cancellationToken);
			await InvalidateGroupInCache(groupId);

			_eventAggregator.SendMessage<UserAssignedToGroupEvent>(new UserAssignedToGroupEvent() { DepartmentId = departmentGroup.DepartmentId, UserId = userId, Group = departmentGroup });

			return saved;
		}

		public async Task<List<DepartmentGroupMember>> GetAllMembersForGroupAsync(int groupId)
		{
			var items = await _departmentGroupMembersRepository.GetAllGroupMembersByGroupIdAsync(groupId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<DepartmentGroupMember>();
		}

		public List<DepartmentGroupMember> GetAllMembersForGroupAndChildGroups(DepartmentGroup group)
		{
			var result = new List<DepartmentGroupMember>();

			if (group != null && group.Members.Any())
				result.AddRange(group.Members);

			if (group != null && group.Children != null && group.Children.Any())
			{
				foreach (var child in group.Children)
				{
					result.AddRange(GetAllMembersForGroupAndChildGroups(child));
				}
			}

			return result;
		}

		public async Task<List<DepartmentGroupMember>> GetAllAdminsForGroupAsync(int groupId)
		{
			var members = await _departmentGroupMembersRepository.GetAllGroupMembersByGroupIdAsync(groupId);

			return members.Where(x => x.IsAdmin.Equals(true)).ToList();
		}

		public async Task<DepartmentGroup> GetGroupByDispatchEmailCodeAsync(string code)
		{
			var group = await _departmentGroupsRepository.GetGroupByDispatchCodeAsync(code);

			return group;
		}

		public async Task<DepartmentGroup> GetGroupByMessageEmailCodeAsync(string code)
		{
			var group = await _departmentGroupsRepository.GetGroupByMessageCodeAsync(code);

			return group;
		}

		public List<IdentityUser> GetAllUsersForGroup(int groupId)
		{
			return _identityRepository.GetAllUsersForGroup(groupId);
		}

		public async Task<bool> DeleteGroupMembersByGroupIdAsync(int groupId, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _departmentGroupMembersRepository.DeleteGroupMembersByGroupIdAsync(groupId, departmentId, cancellationToken);
		}

		public async Task<List<DepartmentGroupMember>> GetAllGroupAdminsByDepartmentIdAsync(int departmentId)
		{
			var admins = await _departmentGroupMembersRepository.GetAllGroupAdminsByDepartmentIdAsync(departmentId);

			return admins.ToList();
		}
	}
}
