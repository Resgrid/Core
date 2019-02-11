using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Microsoft.AspNet.Identity.EntityFramework6;

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

		public DepartmentGroup Save(DepartmentGroup departmentGroup)
		{
			_departmentGroupsRepository.SaveOrUpdate(departmentGroup);
			InvalidateGroupInCache(departmentGroup.DepartmentGroupId);

			return departmentGroup;
		}

		public List<DepartmentGroup> GetAllGroupsForDepartment(int departmentId)
		{
			List<DepartmentGroup> departmentGroups = new List<DepartmentGroup>();

			var groups = GetAllGroupsForDepartmentUnlimited(departmentId);

			int limit = 0;
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				limit = int.MaxValue;
			else
				limit = _subscriptionsService.GetCurrentPlanForDepartment(departmentId).GetLimitForTypeAsInt(PlanLimitTypes.Groups);

			int count = groups.Count < limit ? groups.Count : limit;

			// Only return users up to the plans group limit
			for (int i = 0; i < count; i++)
			{
				departmentGroups.Add(groups[i]);
			}

			// Remove all Disabled or Hidden Users
			//foreach (var dg in departmentGroups)
			//{
			//	 dg.Members = (from m in dg.Members
			//				  where !_departmentsService.IsUserDisabled(m.UserId) && !_departmentsService.IsUserHidden(m.UserId)
			//				  select m).ToList();
			//}

			return departmentGroups;
		}

		public async Task<List<DepartmentGroup>> GetAllGroupsForDepartmentAsync(int departmentId)
		{
			List<DepartmentGroup> departmentGroups = new List<DepartmentGroup>();

			var groups = await GetAllGroupsForDepartmentUnlimitedAsync(departmentId);

			int limit = (await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId)).GetLimitForTypeAsInt(PlanLimitTypes.Groups);
			int count = groups.Count < limit ? groups.Count : limit;

			// Only return users up to the plans group limit
			for (int i = 0; i < count; i++)
			{
				departmentGroups.Add(groups[i]);
			}

			return departmentGroups;
		}

		public void InvalidateGroupInCache(int groupId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, groupId));
		}

		public List<DepartmentGroup> GetAllGroupsForDepartmentUnlimited(int departmentId)
		{
			var groups = (from g in _departmentGroupsRepository.GetAll()
						  where g.DepartmentId == departmentId
						  select g).ToList();

			foreach (var g in groups)
			{
				if (g.AddressId.HasValue && g.Address == null)
					g.Address = _addressService.GetAddressById(g.AddressId.Value);
			}

			return groups;
		}

		public async Task<List<DepartmentGroup>> GetAllGroupsForDepartmentUnlimitedAsync(int departmentId)
		{
			var groups = await _departmentGroupsRepository.GetAllGroupsForDepartmentUnlimitedAsync(departmentId);

			foreach (var g in groups)
			{
				if (g.AddressId.HasValue && g.Address == null)
					g.Address = await _addressService.GetAddressByIdAsync(g.AddressId.Value);
			}

			return groups;
		}

		public DepartmentGroup GetGroupById(int departmentGroupId, bool bypassCache = true)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<DepartmentGroup> getDepartmentGroup = delegate ()
				{
					var group1 = (from g in _departmentGroupsRepository.GetAll()
								  where g.DepartmentGroupId == departmentGroupId
								  select g).FirstOrDefault();

					if (group1 != null && group1.AddressId.HasValue && group1.Address == null)
						group1.Address = _addressService.GetAddressById(group1.AddressId.Value);

					return group1;
				};

				return _cacheProvider.Retrieve(string.Format(CacheKey, departmentGroupId), getDepartmentGroup, CacheLength);
			}

			var group = (from g in _departmentGroupsRepository.GetAll()
						 where g.DepartmentGroupId == departmentGroupId
						 select g).FirstOrDefault();

			if (group != null && group.AddressId.HasValue && group.Address == null)
				group.Address = _addressService.GetAddressById(group.AddressId.Value);

			return group;
		}

		public void DeleteGroupById(int groupId)
		{
			var group = GetGroupById(groupId);
			var members = (from m in _departmentGroupMembersRepository.GetAll()
						   where m.DepartmentGroupId == groupId
						   select m).ToList();

			_departmentGroupMembersRepository.DeleteAll(members);
			_departmentGroupsRepository.DeleteOnSubmit(group);
			InvalidateGroupInCache(groupId);
		}

		public DepartmentGroup Update(DepartmentGroup departmentGroup)
		{
			var members = (from m in _departmentGroupMembersRepository.GetAll()
						   where m.DepartmentGroupId == departmentGroup.DepartmentGroupId
						   select m).AsEnumerable();

			_departmentGroupMembersRepository.DeleteAll(members);
			_departmentGroupsRepository.SaveOrUpdate(departmentGroup);

			InvalidateGroupInCache(departmentGroup.DepartmentGroupId);

			return departmentGroup;
		}

		public bool IsUserInAGroup(string userId, int departmentId)
		{
			var members = from m in _departmentGroupMembersRepository.GetAll()
						  where m.UserId == userId && m.DepartmentId == departmentId
						  select m;

			if (members != null && members.Any())
				return true;

			return false;
		}

		public bool IsUserAGroupAdmin(string userId, int departmentId)
		{
			var members = from m in _departmentGroupMembersRepository.GetAll()
						  where m.UserId == userId && m.IsAdmin.HasValue && m.IsAdmin.Value && m.DepartmentId == departmentId
						  select m;

			if (members != null && members.Any())
				return true;

			return false;
		}

		public bool IsUserInAGroup(string userId, int excludedGroupId, int departmentId)
		{
			var members = from m in _departmentGroupMembersRepository.GetAll()
						  where m.UserId == userId && m.DepartmentId == departmentId && m.DepartmentGroupId != excludedGroupId
						  select m;

			if (members != null && members.Any())
				return true;

			return false;
		}

		public void DeleteUserFromGroups(string userId, int departmentId)
		{
			var groupMemberships = (from dgm in _departmentGroupMembersRepository.GetAll()
									where dgm.UserId == userId && dgm.DepartmentId == departmentId
									select dgm).ToList();

			_departmentGroupMembersRepository.DeleteAll(groupMemberships);

			foreach (var groupMember in groupMemberships)
			{
				InvalidateGroupInCache(groupMember.DepartmentGroupId);
			}
		}

		public List<DepartmentGroup> GetAllChildDepartmentGroups(int parentDepartmentGroupId)
		{
			var groups = (from g in _departmentGroupsRepository.GetAll()
						  where g.ParentDepartmentGroupId == parentDepartmentGroupId
						  select g).ToList();

			return groups;
		}

		public List<DepartmentGroup> GetAllStationGroupsForDepartment(int departmentId)
		{
			return _departmentGroupsRepository.GetAllStationGroupsForDepartment(departmentId);
		}

		public DepartmentGroup GetGroupForUser(string userId, int departmentId)
		{
			var depMember = (from member in _departmentGroupMembersRepository.GetAll()
							 where member.UserId == userId && member.DepartmentId == departmentId
							 select member).FirstOrDefault();

			if (depMember == null)
				return null;

			if (depMember.DepartmentGroup != null)
				return depMember.DepartmentGroup;

			return GetGroupById(depMember.DepartmentGroupId);
		}

		public DepartmentGroupMember GetGroupMemberForUser(string userId, int departmentId)
		{
			var depMember = (from member in _departmentGroupMembersRepository.GetAll()
				where member.UserId == userId && member.DepartmentId == departmentId
				select member).FirstOrDefault();

			return depMember;
		}

		public DepartmentGroupMember SaveGroupMember(DepartmentGroupMember depMember)
		{
			_departmentGroupMembersRepository.SaveOrUpdate(depMember);

			InvalidateGroupInCache(depMember.DepartmentGroupId);

			return depMember;
		}

		public Dictionary<string, DepartmentGroup> GetAllDepartmentGroupsForDepartment(int departmentId)
		{
			var depMember = (from member in _departmentGroupMembersRepository.GetAll()
							 where member.DepartmentGroup.DepartmentId == departmentId
							 select member).ToList();

			return depMember.ToDictionary(departmentGroupMember => departmentGroupMember.UserId, departmentGroupMember => departmentGroupMember.DepartmentGroup);
		}

		public List<string> AllGroupedUserIdsForDepartment(int departmentId)
		{
			var groups = GetAllGroupsForDepartment(departmentId);

			return (from departmentGroup in groups from member in departmentGroup.Members select member.UserId).ToList();
		}

		public Coordinates GetMapCenterCoordinatesForGroup(int departmentGroupId)
		{
			Coordinates coordinates = null;

			var departmentGroup = GetGroupById(departmentGroupId);
			var department = _departmentsService.GetDepartmentById(departmentGroup.DepartmentId);

			if (departmentGroup.Address != null)
			{
				coordinates = new Coordinates();
				string coordinateString = _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", departmentGroup.Address.Address1,
				departmentGroup.Address.City, departmentGroup.Address.State, departmentGroup.Address.PostalCode,
				departmentGroup.Address.Country));

				var coords = coordinateString.Split(char.Parse(","));
				coordinates.Latitude = double.Parse(coords[0]);
				coordinates.Longitude = double.Parse(coords[1]);
			}

			if (coordinates == null && department.Address != null)
			{
				coordinates = new Coordinates();
				string coordinateString = _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", department.Address.Address1,
				department.Address.City, department.Address.State, department.Address.PostalCode, department.Address.Country));

				var coords = coordinateString.Split(char.Parse(","));
				coordinates.Latitude = double.Parse(coords[0]);
				coordinates.Longitude = double.Parse(coords[1]);
			}

			var gpsCoordinates = _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartment(departmentGroup.DepartmentId);
			if (coordinates == null && !string.IsNullOrWhiteSpace(gpsCoordinates))
			{
				coordinates = new Coordinates();

				var coords = gpsCoordinates.Split(char.Parse(","));
				coordinates.Latitude = double.Parse(coords[0]);
				coordinates.Longitude = double.Parse(coords[1]);
			}


			return coordinates;
		}

		public bool MoveUserIntoGroup(string userId, int groupId, bool isAdmin, int departmentId)
		{
			var departmentGroup = GetGroupById(groupId);
			if (departmentGroup == null)
				return false;

			var depMember = (from member in _departmentGroupMembersRepository.GetAll()
							 where member.UserId == userId && member.DepartmentId == departmentId
							 select member).FirstOrDefault();

			if (depMember == null)
				depMember = new DepartmentGroupMember();

			depMember.DepartmentGroupId = groupId;
			depMember.DepartmentId = departmentId;
			depMember.IsAdmin = isAdmin;
			depMember.UserId = userId;

			_departmentGroupMembersRepository.SaveOrUpdate(depMember);
			InvalidateGroupInCache(groupId);

			_eventAggregator.SendMessage<UserAssignedToGroupEvent>(new UserAssignedToGroupEvent() { DepartmentId = departmentGroup.DepartmentId, UserId = userId, Group = departmentGroup });

			return true;
		}

		public List<DepartmentGroupMember> GetAllMembersForGroup(int groupId)
		{
			return _departmentGroupMembersRepository.GetAllMembersForGroup(groupId);
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

		public List<DepartmentGroupMember> GetAllAdminsForGroup(int groupId)
		{
			var members = (from dgm in _departmentGroupMembersRepository.GetAll()
						   where dgm.DepartmentGroupId == groupId
						   select dgm).ToList();

			return members.Where(x => x.IsAdmin.Equals(true)).ToList();
		}

		public DepartmentGroup GetGroupByDispatchEmailCode(string code)
		{
			var group = (from g in _departmentGroupsRepository.GetAll()
						 where g.DispatchEmail == code
						 select g).FirstOrDefault();

			return group;
		}

		public DepartmentGroup GetGroupByMessageEmailCode(string code)
		{
			var group = (from g in _departmentGroupsRepository.GetAll()
						 where g.MessageEmail == code
						 select g).FirstOrDefault();

			return group;
		}

		public List<IdentityUser> GetAllUsersForGroup(int groupId)
		{
			return _identityRepository.GetAllUsersForGroup(groupId);
		}
	}
}
