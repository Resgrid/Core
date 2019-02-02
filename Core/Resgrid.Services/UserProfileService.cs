using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UserProfileService : IUserProfileService
	{
		private static string CacheKey = "UserProfile_{0}";
		private static string AllUserProfilesCacheKey = "AllDepUserProfile_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly IUserProfilesRepository _userProfileRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IGenericDataRepository<DepartmentMember> _departmentMemberRepository;

		public UserProfileService(IUserProfilesRepository userProfileRepository, IDepartmentsService departmentsService, ICacheProvider cacheProvider,
			IGenericDataRepository<DepartmentMember> departmentMemberRepository)
		{
			_userProfileRepository = userProfileRepository;
			_departmentsService = departmentsService;
			_cacheProvider = cacheProvider;
			_departmentMemberRepository = departmentMemberRepository;
		}

		public UserProfile GetProfileByUserId(string userId, bool bypassCache = false)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<UserProfile> getProfile = delegate()
				{
					//return _userProfileRepository.GetAll().FirstOrDefault(x => x.UserId == userId);
					return _userProfileRepository.GetProfileByUserId(userId);
				};

				return _cacheProvider.Retrieve(string.Format(CacheKey, userId), getProfile, CacheLength);
			}

			//var profile = from p in _userProfileRepository.GetAll()
			//				where p.UserId == userId
			//					select p;

			//return profile.FirstOrDefault();
			return _userProfileRepository.GetProfileByUserId(userId);
		}

		public async Task<UserProfile> GetProfileByUserIdAsync(string userId, bool bypassCache = false)
		{
			Func<Task<UserProfile>> getProfileAsync = async () =>
			{
				return await _userProfileRepository.GetProfileByUserIdAsync(userId);
			};

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync(string.Format(CacheKey, userId), getProfileAsync, CacheLength);
			}

			return await getProfileAsync();
		}

		public UserProfile GetUserProfileForEditing(string userId)
		{
			return _userProfileRepository.GetAll().FirstOrDefault(x => x.UserId == userId);
		}

		public Dictionary<string, UserProfile> GetAllProfilesForDepartment(int departmentId, bool bypassCache = false)
		{
			//var users = _departmentMemberRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
			//var userIds = users.Select(x => x.UserId);

			//var profile = (from p in _userProfileRepository.GetAll()
			//							 where userIds.Contains(p.UserId)
			//				select p).ToList();

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<List<UserProfile>> getAllUserProfiles = delegate()
				{
					return _userProfileRepository.GetAllUserProfilesForDepartment(departmentId);
				};

				return _cacheProvider.Retrieve(string.Format(AllUserProfilesCacheKey, departmentId), getAllUserProfiles, CacheLength)
							.ToDictionary(userProfile => userProfile.UserId);
			}
			else
			{
				var profile = _userProfileRepository.GetAllUserProfilesForDepartment(departmentId);
				return profile.ToDictionary(userProfile => userProfile.UserId);
			}
		}


		public UserProfile SaveProfile(int DepartmentId, UserProfile profile)
		{
			_userProfileRepository.SaveOrUpdate(profile);
			
			ClearUserProfileFromCache(profile.UserId);
			ClearAllUserProfilesFromCache(DepartmentId);

			return profile;
		}

		public void ClearUserProfileFromCache(string userId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, userId));
		}

		public void ClearAllUserProfilesFromCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(AllUserProfilesCacheKey, departmentId));
		}

		public void DeletProfileForUser(string userId)
		{
			var profile = GetProfileByUserId(userId);

			if (profile != null)
				_userProfileRepository.DeleteOnSubmit(profile);
		}

		public void DisableTextMessagesForUser(string userId)
		{
			var profile = GetUserProfileForEditing(userId);
			profile.SendMessageSms = false;
			profile.SendSms = false;
			profile.SendNotificationSms = false;

			SaveProfile(0, profile);
		}

		public UserProfile FindProfileByMobileNumber(string number)
		{
			string numberToTest =
				number.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim();

			var profiles = _userProfileRepository.GetAll().Where(x => x.MobileNumber != null).ToList();
			var profile = from p in profiles
				where
					p.MobileNumber != null &&
					p.MobileNumber.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim() == numberToTest
				select p;

			if (profile.Count() == 1)
				return profile.First();
			else
			{
				if (numberToTest.Length == 11 && numberToTest[0] == char.Parse("1"))
				{
					numberToTest = numberToTest.Remove(0, 1);

					profile = from p in profiles
								where
									p.MobileNumber != null &&
									p.MobileNumber.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim() == numberToTest
								select p;

					return profile.FirstOrDefault();
				}
			}

			return null;
		}

		public UserProfile FindProfileByHomeNumber(string number)
		{
			string numberToTest =
				number.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim();

			var profiles = _userProfileRepository.GetAll().Where(x => x.HomeNumber != null).ToList();
			var profile = from p in profiles
						  where
							  p.HomeNumber != null &&
							  p.HomeNumber.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim() == numberToTest
						  select p;

			if (profile.Count() == 1)
				return profile.First();
			else
			{
				if (numberToTest.Length == 11 && numberToTest[0] == char.Parse("1"))
				{
					numberToTest = numberToTest.Remove(0, 1);

					profile = from p in profiles
							  where
								  p.HomeNumber != null &&
								  p.HomeNumber.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim() == numberToTest
							  select p;

					return profile.FirstOrDefault();
				}
			}

			return null;
		}

		public List<UserProfile> GetSelectedUserProfiles(List<string> userIds)
		{
			//return _userProfileRepository.GetAll().Where(x => userIds.Contains(x.UserId)).ToList();
			return _userProfileRepository.GetSelectedUserProfiles(userIds);
		}

		public async Task<List<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds)
		{
			//return _userProfileRepository.GetAll().Where(x => userIds.Contains(x.UserId)).ToList();
			return await _userProfileRepository.GetSelectedUserProfilesAsync(userIds);
		}

		//public List<UserProfile> GetAllUserProfilesForDepartment(int departmentId)
		//{
		//	var users = _departmentsService.GetAllUsersForDepartment(departmentId);

		//	return users.Select(user => GetProfileByUserId(user.UserId)).ToList();
		//}
	}
}
