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
		private readonly IChatbotIdentityRepository _chatbotIdentityRepository;

		public UserProfileService(IUserProfilesRepository userProfileRepository, ICacheProvider cacheProvider,
			IChatbotIdentityRepository chatbotIdentityRepository)
		{
			_userProfileRepository = userProfileRepository;
			_cacheProvider = cacheProvider;
			_chatbotIdentityRepository = chatbotIdentityRepository;
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

				if (items != null && items.Any())
					return items.ToList();

				return new List<UserProfile>();
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
			// Load existing profile directly from repository (bypass cache) to detect contact changes
			var existing = await _userProfileRepository.GetProfileByUserIdAsync(profile.UserId);

			// A null SecurityPin means "not supplied by this caller" — keep the stored (encrypted) PIN
			// so profile saves from flows that don't know about PINs can't silently wipe it. Clearing a
			// PIN intentionally is done by saving an empty string.
			if (existing != null && profile.SecurityPin == null && existing.SecurityPin != null)
				profile.SecurityPin = existing.SecurityPin;

			if (existing == null)
			{
				// Brand-new profile (admin-created user) — mark all contact methods as pending
				if (!string.IsNullOrWhiteSpace(profile.MobileNumber))
					profile.MobileNumberVerified = false;
				if (!string.IsNullOrWhiteSpace(profile.HomeNumber))
					profile.HomeNumberVerified = false;
				if (!string.IsNullOrWhiteSpace(profile.MembershipEmail))
					profile.EmailVerified = false;
			}
			else
			{
				// Reset verification if any contact field value changed
				if (!string.Equals(existing.MobileNumber ?? string.Empty, profile.MobileNumber ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				{
					profile.MobileNumberVerified = false;
					profile.MobileVerificationCode = null;
					profile.MobileVerificationCodeExpiry = null;
					profile.MobileVerificationVoiceCodeConsumed = false;
					profile.MobileVerificationAttempts = 0;
					profile.MobileVerificationAttemptsResetDate = null;

					// The old number no longer identifies this user: remove any SMS chatbot identity
					// links so inbound texts can't act as this account until the new number is verified
					// and re-linked.
					await RemoveSmsChatbotIdentitiesAsync(profile.UserId, cancellationToken);
				}
				if (!string.Equals(existing.HomeNumber ?? string.Empty, profile.HomeNumber ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				{
					profile.HomeNumberVerified = false;
					profile.HomeVerificationCode = null;
					profile.HomeVerificationCodeExpiry = null;
					profile.HomeVerificationVoiceCodeConsumed = false;
					profile.HomeVerificationAttempts = 0;
					profile.HomeVerificationAttemptsResetDate = null;
				}
				if (!string.Equals(existing.MembershipEmail ?? string.Empty, profile.MembershipEmail ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				{
					profile.EmailVerified = false;
					profile.EmailVerificationCode = null;
					profile.EmailVerificationCodeExpiry = null;
					profile.EmailVerificationAttempts = 0;
					profile.EmailVerificationAttemptsResetDate = null;
				}
			}

			profile.LastUpdated = DateTime.UtcNow;
			var savedProfile = await _userProfileRepository.SaveOrUpdateAsync(profile, cancellationToken);

			ClearUserProfileFromCache(savedProfile.UserId);
			ClearAllUserProfilesFromCache(DepartmentId);

			return savedProfile;
		}

		private async Task RemoveSmsChatbotIdentitiesAsync(string userId, CancellationToken cancellationToken)
		{
			var identities = await _chatbotIdentityRepository.GetAllByUserIdAsync(userId);
			if (identities == null)
				return;

			foreach (var identity in identities.Where(i =>
						 i.Platform == ChatbotIdentity.PlatformSmsTwilio || i.Platform == ChatbotIdentity.PlatformSmsSignalWire))
			{
				await _chatbotIdentityRepository.DeleteAsync(identity, cancellationToken);
			}
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

			var profile = await _userProfileRepository.GetProfileByMobileNumberAsync(numberToTest);

			if (profile != null)
				return profile;

			if (numberToTest.Length == 11 && numberToTest[0] == char.Parse("1"))
			{
				numberToTest = numberToTest.Remove(0, 1);

				return await _userProfileRepository.GetProfileByMobileNumberAsync(numberToTest);
			}

			return null;
		}

		public async Task<UserProfile> GetProfileByHomeNumberAsync(string number)
		{
			string numberToTest =
				number.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim();

			var profile = await _userProfileRepository.GetProfileByMobileNumberAsync(numberToTest);

			if (profile != null)
				return profile;

			if (numberToTest.Length == 11 && numberToTest[0] == char.Parse("1"))
			{
				numberToTest = numberToTest.Remove(0, 1);

				return await _userProfileRepository.GetProfileByMobileNumberAsync(numberToTest);
			}

			return null;
		}

		public async Task<List<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds)
		{
			if (userIds == null || userIds.Count <= 0)
				return new List<UserProfile>();

			var items = await _userProfileRepository.GetSelectedUserProfilesAsync(userIds);

			if (items != null && items.Any())
				return items.ToList();

			return new List<UserProfile>();
		}
	}
}
