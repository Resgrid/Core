using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
			return await FindProfileByPhoneAsync(number,
				_userProfileRepository.GetProfileByMobileNumberAsync,
				profile => profile.MobileNumberVerified);
		}

		public async Task<UserProfile> GetProfileByHomeNumberAsync(string number)
		{
			return await FindProfileByPhoneAsync(number,
				_userProfileRepository.GetProfileByHomeNumberAsync,
				profile => profile.HomeNumberVerified);
		}

		/// <summary>
		/// Resolves the profile that owns a phone number, preferring one that has actually proven it.
		/// <para>
		/// The same number can sit on more than one profile - a stale or secondary account, or someone
		/// who mistyped it and never completed verification. A profile that verified the number is the
		/// only one that has demonstrated possession, so it wins outright, even over a closer match on
		/// the number's shape. Everything else falls back to candidate order (the number exactly as
		/// dialled before its country-code variant).
		/// </para>
		/// <para>
		/// Within a single candidate the query does the same ranking, so this only has to arbitrate
		/// between candidates.
		/// </para>
		/// </summary>
		private static async Task<UserProfile> FindProfileByPhoneAsync(string number,
			Func<string, Task<UserProfile>> lookup, Func<UserProfile, bool?> isVerified)
		{
			UserProfile unverifiedMatch = null;

			foreach (var candidate in PhoneLookupCandidates(number))
			{
				var profile = await lookup(candidate);

				if (profile == null)
					continue;

				if (isVerified(profile) == true)
					return profile;

				// Keep the first one found so a candidate that matches nothing verified still resolves,
				// but keep looking in case a later candidate did verify the number.
				unverifiedMatch ??= profile;
			}

			return unverifiedMatch;
		}

		/// <summary>
		/// The stored numbers a lookup should be tried against, most-specific first.
		/// <para>
		/// Profiles are saved in E.164 (+12248304555) while inbound SMS and voice hand us the number in
		/// whatever shape the carrier used, so a lookup has to cover the country code being present on
		/// one side but not the other. The leading "+" is covered by the query itself, which matches the
		/// stored value both bare and plus-prefixed.
		/// </para>
		/// <para>
		/// The order matters and the candidates are tried one at a time rather than matched together:
		/// 2248304555 and 12248304555 can be two different profiles, and the repository takes
		/// FirstOrDefault() with no ORDER BY. Asking for the number exactly as dialled first means the
		/// country-code variant is only ever reached as a fallback.
		/// </para>
		/// </summary>
		private static IEnumerable<string> PhoneLookupCandidates(string number)
		{
			var digits = NormalizePhoneNumber(number);

			// A blank inbound number must never match: the stored column can also be blank and an
			// empty-to-empty compare would hand back an arbitrary profile.
			if (string.IsNullOrWhiteSpace(digits))
				yield break;

			yield return digits;

			if (digits.Length == 11 && digits[0] == '1')
				yield return digits.Substring(1);
			else if (digits.Length == 10)
				yield return "1" + digits;
		}

		/// <summary>
		/// Reduces a number to bare digits. Inbound numbers arrive formatted in assorted ways
		/// ("+1 (224) 830-4555"), and only the digits are comparable against a stored number.
		/// </summary>
		private static string NormalizePhoneNumber(string number)
		{
			if (string.IsNullOrWhiteSpace(number))
				return null;

			var digits = new StringBuilder(number.Length);

			foreach (var character in number)
			{
				if (character >= '0' && character <= '9')
					digits.Append(character);
			}

			return digits.ToString();
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
