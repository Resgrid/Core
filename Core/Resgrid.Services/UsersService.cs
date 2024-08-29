using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Identity;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using MongoDB.Bson;
using System.Collections.ObjectModel;
using Resgrid.Model.Events;
using Resgrid.Framework;

namespace Resgrid.Services
{
	public class UsersService : IUsersService
	{
		public string UserRoleId
		{
			get { return Config.DataConfig.UsersIdentityRoleId; }
		}
		public string AdminRoleId
		{
			get { return Config.DataConfig.AdminsIdentityRoleId; }
		}
		public string AffiliateRoleId
		{
			get { return Config.DataConfig.AffiliatesIdentityRoleId; }
		}

		private static string UserCacheKey = "User_{0}";
		private static string UserGroupRolesCacheKey = "UGRDepartment_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(7);
		private static TimeSpan UGRCacheLength = TimeSpan.FromDays(14);

		private readonly IDepartmentMembersRepository _departmentMembersRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IIdentityRepository _identityRepository;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IMongoRepository<PersonnelLocation> _personnelLocationRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly ILimitsService _limitsService;

		public UsersService(IDepartmentMembersRepository departmentMembersRepository, ICacheProvider cacheProvider,
			IIdentityRepository identityRepository, IDepartmentSettingsService departmentSettingsService,
			IMongoRepository<PersonnelLocation> personnelLocationRepository, IEventAggregator eventAggregator,
			ILimitsService limitsService)
		{
			_departmentMembersRepository = departmentMembersRepository;
			_cacheProvider = cacheProvider;
			_identityRepository = identityRepository;
			_departmentSettingsService = departmentSettingsService;
			_personnelLocationRepository = personnelLocationRepository;
			_eventAggregator = eventAggregator;
			_limitsService = limitsService;
		}

		public List<IdentityUser> GetAll()
		{
			return _identityRepository.GetAll();
		}

		public void AddUserToUserRole(string userId)
		{
			if (!IsUserInRole(userId, UserRoleId))
			{
				_identityRepository.AddUserToRole(userId, UserRoleId);
			}
		}

		public void AddUserToAffiliteRole(string userId)
		{
			if (!IsUserInRole(userId, AffiliateRoleId))
			{
				_identityRepository.AddUserToRole(userId, AffiliateRoleId);
			}
		}

		public bool IsUserInRole(string userId, string roleId)
		{
			var role = _identityRepository.GetRoleForUserRole(userId, roleId);

			if (role != null)
				return true;

			return false;
		}

		public IdentityUser GetUserByEmail(string emailAddress)
		{
			return _identityRepository.GetUserByEmail(emailAddress);
		}

		public async Task<bool> DoesUserHaveAnyActiveDepartments(string userName)
		{
			var user = await GetUserByNameAsync(userName);
			var memberships = await _departmentMembersRepository.GetAllByUserIdAsync(user.UserId);

			return memberships.Any(x => x.IsDeleted == false);
		}

		public IdentityUser GetUserById(string userId, bool bypassCache = true)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<IdentityUser> getUser = delegate ()
				{
					return _identityRepository.GetUserById(userId.ToString());
				};

				return _cacheProvider.Retrieve(string.Format(UserCacheKey, userId), getUser, CacheLength);
			}

			return _identityRepository.GetUserById(userId.ToString());
		}

		public async Task<List<UserGroupRole>> GetUserGroupAndRolesByDepartmentIdAsync(int departmentId, bool retrieveHidden, bool retrieveDisabled, bool retrieveDeleted)
		{
			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(departmentId);
			var users = await _identityRepository.GetAllUsersGroupsAndRolesAsync(departmentId, retrieveHidden, retrieveDisabled, retrieveDeleted);

			if (users != null && users.Any())
			{
				switch (personnelSortOrder)
				{
					case PersonnelSortOrders.FirstName:
						users = users.OrderBy(x => x.FirstName).ToList();
						break;
					case PersonnelSortOrders.LastName:
						users = users.OrderBy(x => x.LastName).ToList();
						break;
					case PersonnelSortOrders.Group:
						users = users.OrderBy(x => x.DepartmentGroupId).ToList();
						break;
				}
			}

			return users;
		}

		public async Task<List<UserGroupRole>> GetUserGroupAndRolesByDepartmentIdInLimitAsync(int departmentId, bool retrieveHidden, bool retrieveDisabled, bool retrieveDeleted)
		{
			////Func<List<UserGroupRole>> getUserGroupRole = delegate ()
			//{
			//	return _identityRepository.GetAllUsersGroupsAndRolesAsync(deparmentId, retrieveHidden, retrieveDisabled, retrieveDeleted);
			//};

			//if (Config.SystemBehaviorConfig.CacheEnabled)
			//	return _cacheProvider.Retrieve(string.Format(UserGroupRolesCacheKey, deparmentId), getUserGroupRole, UGRCacheLength);
			//else
			//	return getUserGroupRole();

			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(departmentId);
			var users = await _identityRepository.GetAllUsersGroupsAndRolesAsync(departmentId, retrieveHidden, retrieveDisabled, retrieveDeleted);

			var limit = await _limitsService.GetLimitsForEntityPlanWithFallbackAsync(departmentId);

			if (users != null && users.Any())
			{
				if (users.Count > limit.PersonnelLimit)
					users = users.Take(limit.PersonnelLimit).ToList();

				switch (personnelSortOrder)
				{
					case PersonnelSortOrders.FirstName:
						users = users.OrderBy(x => x.FirstName).ToList();
						break;
					case PersonnelSortOrders.LastName:
						users = users.OrderBy(x => x.LastName).ToList();
						break;
					case PersonnelSortOrders.Group:
						users = users.OrderBy(x => x.DepartmentGroupId).ToList();
						break;
				}
			}

			return users;
		}

		public void ClearCacheForDepartment(int departmentId)
		{
			_cacheProvider.Remove(string.Format(UserGroupRolesCacheKey, departmentId));
		}

		public IdentityUser GetMembershipByUserId(string userId)
		{
			return _identityRepository.GetUserById(userId.ToString());
		}

		public async Task<IdentityUser> GetUserByNameAsync(string userName)
		{
			return await _identityRepository.GetUserByUserNameAsync(userName);
		}

		public void InitUserExtInfo(string userId)
		{
			_identityRepository.InitUserExtInfo(userId);
		}

		public async Task<IdentityUser> UpdateUsername(string oldUsername, string newUsername)
		{
			if (String.IsNullOrEmpty(oldUsername) || String.IsNullOrEmpty(newUsername))
				return null;

			_identityRepository.UpdateUsername(oldUsername, newUsername);
			var user = await _identityRepository.GetUserByUserNameAsync(newUsername);

			return user;
		}

		public IdentityUser UpdateEmail(string userId, string newEmail)
		{
			if (String.IsNullOrEmpty(userId) || String.IsNullOrEmpty(newEmail))
				return null;

			_identityRepository.UpdateEmail(userId, newEmail);
			var user = _identityRepository.GetUserById(userId);

			return user;
		}

		public IdentityUser SaveUser(IdentityUser user)
		{
			_identityRepository.Update(user);

			return user;
		}

		public List<IdentityUser> GetAllMembershipsForDepartment(int departmentId)
		{
			return _identityRepository.GetAllMembershipsForDepartment(departmentId);
		}

		public Dictionary<string, int> GetNewUsersCountForLast5Days()
		{
			Dictionary<string, int> data = new Dictionary<string, int>();

			var startDate = DateTime.UtcNow.AddDays(-5);
			var filteredRecords = _identityRepository.GetAllUsersCreatedAfterTimestamp(startDate);

			data.Add(DateTime.UtcNow.ToShortDateString(), filteredRecords.Count(x => x.CreateDate.ToShortDateString() == DateTime.UtcNow.ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-1).ToShortDateString(), filteredRecords.Count(x => x.CreateDate.ToShortDateString() == DateTime.UtcNow.AddDays(-1).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-2).ToShortDateString(), filteredRecords.Count(x => x.CreateDate.ToShortDateString() == DateTime.UtcNow.AddDays(-2).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-3).ToShortDateString(), filteredRecords.Count(x => x.CreateDate.ToShortDateString() == DateTime.UtcNow.AddDays(-3).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-4).ToShortDateString(), filteredRecords.Count(x => x.CreateDate.ToShortDateString() == DateTime.UtcNow.AddDays(-4).ToShortDateString()));

			return data;
		}

		public int GetUsersCount()
		{
			return _identityRepository.GetAll().Count();
		}

		public async Task<PersonnelLocation> SavePersonnelLocationAsync(PersonnelLocation personnelLocation)
		{
			try
			{
				if (personnelLocation.Id.Timestamp == 0)
					await _personnelLocationRepository.InsertOneAsync(personnelLocation);
				else
					await _personnelLocationRepository.ReplaceOneAsync(personnelLocation);

				_eventAggregator.SendMessage<PersonnelLocationUpdatedEvent>(new PersonnelLocationUpdatedEvent() {
					DepartmentId = personnelLocation.DepartmentId,
					UserId = personnelLocation.UserId,
					Latitude = personnelLocation.Latitude,
					Longitude = personnelLocation.Longitude,
					RecordId = personnelLocation.Id.ToString(),
				});
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return personnelLocation;
		}

		public async Task<List<PersonnelLocation>> GetLatestLocationsForDepartmentPersonnelAsync(int departmentId)
		{
			try
			{
				var locations = _personnelLocationRepository
									.AsQueryable()
									.Where(x => x.DepartmentId == departmentId).OrderByDescending(y => y.Timestamp)
									.GroupBy(pv => pv.UserId, (key, group) => new
									{
										key,
										LatestLocation = group.First()
									});

				if (locations != null)
					return locations.Select(x => x.LatestLocation).ToList();

				return null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return new List<PersonnelLocation>();
		}

		public async Task<PersonnelLocation> GetPersonnelLocationByIdAsync(string id)
		{
			try
			{
				var layers = await _personnelLocationRepository.FindByIdAsync(id);

				return layers;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return null;
		}

		public async Task<bool> ClearOutUserLoginAsync(string userId)
		{
			await _identityRepository.ClearOutUserLoginAsync(userId);
			await _identityRepository.CleanUpOIDCTokensByUserAsync(userId);

			return true;
		}
	}
}
