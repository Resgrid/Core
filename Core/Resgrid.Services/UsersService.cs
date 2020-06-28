using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Identity;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

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

		private readonly IGenericDataRepository<DepartmentMember> _departmentMembersRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IIdentityRepository _identityRepository;

		public UsersService(IGenericDataRepository<DepartmentMember> departmentMembersRepository, ICacheProvider cacheProvider, IIdentityRepository identityRepository)
		{
			_departmentMembersRepository = departmentMembersRepository;
			_cacheProvider = cacheProvider;
			_identityRepository = identityRepository;
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

		public bool DoesUserHaveAnyActiveDepartments(string userName)
		{
			var user = GetUserByName(userName);
			var memberships = _departmentMembersRepository.GetAll().Where(x => x.UserId == user.UserId).ToList();

			return memberships.Any(x => x.IsDeleted == false);
		}

		public IdentityUser GetUserById(string userId, bool bypassCache = true)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<IdentityUser> getUser = delegate()
				{
					return _identityRepository.GetUserById(userId.ToString());
				};

				return _cacheProvider.Retrieve(string.Format(UserCacheKey, userId), getUser, CacheLength);
			}

			return _identityRepository.GetUserById(userId.ToString());
		}

		public List<UserGroupRole> GetUserGroupAndRolesByDepartmentId(int deparmentId, bool retrieveHidden, bool retrieveDisabled, bool retrieveDeleted)
		{
			Func<List<UserGroupRole>> getUserGroupRole = delegate ()
			{
				return _identityRepository.GetAllUsersGroupsAndRoles(deparmentId, retrieveHidden, retrieveDisabled, retrieveDeleted);
			};

			//if (Config.SystemBehaviorConfig.CacheEnabled)
			//	return _cacheProvider.Retrieve(string.Format(UserGroupRolesCacheKey, deparmentId), getUserGroupRole, UGRCacheLength);
			//else
				return getUserGroupRole();
		}

		public void ClearCacheForDepartment(int departmentId)
		{
			_cacheProvider.Remove(string.Format(UserGroupRolesCacheKey, departmentId));
		}

		public IdentityUser GetMembershipByUserId(string userId)
		{
			return _identityRepository.GetUserById(userId.ToString());
		}

		public IdentityUser GetUserByName(string userName)
		{
			return _identityRepository.GetUserByUserName(userName);
		}

		public async Task<IdentityUser> GetUserByNameAsync(string userName)
		{
			return await _identityRepository.GetUserByUserNameAsync(userName);
		}

		public IdentityUser GetIdentityByUserName(string userName)
		{
			return _identityRepository.GetUserByUserName(userName);
		}

		public void InitUserExtInfo(string userId)
		{
			_identityRepository.InitUserExtInfo(userId);
		}

		public IdentityUser UpdateUsername(string oldUsername, string newUsername)
		{
			if (String.IsNullOrEmpty(oldUsername) || String.IsNullOrEmpty(newUsername))
				return null;

			_identityRepository.UpdateUsername(oldUsername, newUsername);
			var user = _identityRepository.GetUserByUserName(newUsername);

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
	}
}
