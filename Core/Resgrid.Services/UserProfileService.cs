using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Services
{
	public class UserProfileService : IUserProfileService
	{
		private static string CacheKey = "UserProfile_{0}";
		private static string AllUserProfilesCacheKey = "AllDepUserProfile_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly IUserProfilesRepository _userProfileRepository;
		private readonly ICacheProvider _cacheProvider;

		public UserProfileService(IUserProfilesRepository userProfileRepository, ICacheProvider cacheProvider)
		{
			_userProfileRepository = userProfileRepository;
			_cacheProvider = cacheProvider;
		}

		public async Task<UserProfile> GetProfileByUserIdAsync(string userId, bool bypassCache = false)
		{
			async Task<UserProfile> getProfileAsync()
			{
				return await _userProfileRepository.GetProfileByUserIdAsync(userId);
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync(string.Format(CacheKey, userId), getProfileAsync, CacheLength);
			}

			return await getProfileAsync();
		}

		public async Task<Dictionary<string, UserProfile>> GetAllProfilesForDepartmentAsync(int departmentId, bool bypassCache = false)
		{
			async Task<List<UserProfile>> getAllUserProfilesAsync()
			{
				var items = await _userProfileRepository.GetAllUserProfilesForDepartmentAsync(departmentId);
				return items.ToList();
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return (await _cacheProvider.RetrieveAsync(string.Format(AllUserProfilesCacheKey, departmentId), getAllUserProfilesAsync, CacheLength))
							.ToDictionary(userProfile => userProfile.UserId);
			}
			else
			{
				var profile = await getAllUserProfilesAsync();
				return profile.ToDictionary(userProfile => userProfile.UserId);
			}
		}

		public async Task<Dictionary<string, UserProfile>> GetAllProfilesForDepartmentIncDisabledDeletedAsync(int departmentId)
		{
			var profile = await _userProfileRepository.GetAllUserProfilesForDepartmentIncDisabledDeletedAsync(departmentId);
			return profile.ToDictionary(userProfile => userProfile.UserId);
		}

		public async Task<UserProfile> SaveProfileAsync(int DepartmentId, UserProfile profile, CancellationToken cancellationToken = default(CancellationToken))
		{
			var savedProfile = await _userProfileRepository.SaveOrUpdateAsync(profile, cancellationToken);

			ClearUserProfileFromCache(savedProfile.UserId);
			ClearAllUserProfilesFromCache(DepartmentId);

			return savedProfile;
		}

		public void ClearUserProfileFromCache(string userId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, userId));
		}

		public void ClearAllUserProfilesFromCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(AllUserProfilesCacheKey, departmentId));
		}

		public async Task<UserProfile> DisableTextMessagesForUserAsync(string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var profile = await GetProfileByUserIdAsync(userId);
			profile.SendMessageSms = false;
			profile.SendSms = false;
			profile.SendNotificationSms = false;

			return await SaveProfileAsync(0, profile, cancellationToken);
		}

		public async Task<UserProfile> GetProfileByMobileNumberAsync(string number)
		{
			string numberToTest =
				number.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim();

			//var profiles = _userProfileRepository.GetProfileByMobileNumberAsync().Where(x => x.MobileNumber != null).ToList();
			//var profile = from p in profiles
			//			  where
			//				  p.MobileNumber != null &&
			//				  p.MobileNumber.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim() == numberToTest
			//			  select p;

			//if (profile.Count() == 1)
			//	return profile.First();
			//else
			//{
			//	if (numberToTest.Length == 11 && numberToTest[0] == char.Parse("1"))
			//	{
			//		numberToTest = numberToTest.Remove(0, 1);

			//		profile = from p in profiles
			//				  where
			//					  p.MobileNumber != null &&
			//					  p.MobileNumber.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim() == numberToTest
			//				  select p;

			//		return profile.FirstOrDefault();
			//	}
			//}

			//return null;

			return await _userProfileRepository.GetProfileByMobileNumberAsync(numberToTest);
		}

		public async Task<UserProfile> GetProfileByHomeNumberAsync(string number)
		{
			string numberToTest =
				number.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim();

			//var profiles = _userProfileRepository.GetAll().Where(x => x.HomeNumber != null).ToList();
			//var profile = from p in profiles
			//			  where
			//				  p.HomeNumber != null &&
			//				  p.HomeNumber.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim() == numberToTest
			//			  select p;

			//if (profile.Count() == 1)
			//	return profile.First();
			//else
			//{
			//	if (numberToTest.Length == 11 && numberToTest[0] == char.Parse("1"))
			//	{
			//		numberToTest = numberToTest.Remove(0, 1);

			//		profile = from p in profiles
			//				  where
			//					  p.HomeNumber != null &&
			//					  p.HomeNumber.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim() == numberToTest
			//				  select p;

			//		return profile.FirstOrDefault();
			//	}
			//}

			return await _userProfileRepository.GetProfileByHomeNumberAsync(numberToTest);
		}

		public async Task<List<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds)
		{
			var items = await _userProfileRepository.GetSelectedUserProfilesAsync(userIds);
			return items.ToList();
		}
	}
}
