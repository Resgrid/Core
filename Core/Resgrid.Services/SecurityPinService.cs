using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Implements the per-user security PIN used as a step-up check for dangerous/department-wide
	/// chatbot and SMS actions. PINs are stored AES-encrypted (via <see cref="IEncryptionService"/>)
	/// on the user profile, mirroring how contact verification codes are stored.
	/// </summary>
	public class SecurityPinService : ISecurityPinService
	{
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEncryptionService _encryptionService;
		private readonly IUserProfilesRepository _userProfileRepository;

		public SecurityPinService(IUserProfileService userProfileService, IDepartmentSettingsService departmentSettingsService,
			IEncryptionService encryptionService, IUserProfilesRepository userProfileRepository)
		{
			_userProfileService = userProfileService;
			_departmentSettingsService = departmentSettingsService;
			_encryptionService = encryptionService;
			_userProfileRepository = userProfileRepository;
		}

		public async Task<bool> IsPinRequiredAsync(string userId, int departmentId)
		{
			if (await _departmentSettingsService.GetForceChatbotSecurityPinAsync(departmentId))
				return true;

			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			return profile != null && profile.SecurityPinEnabled;
		}

		public async Task<bool> HasPinAsync(string userId)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			return !string.IsNullOrWhiteSpace(profile?.SecurityPin);
		}

		public async Task<bool> ValidatePinAsync(string userId, string pin)
		{
			if (!SecurityPinUtility.IsValidFormat(pin))
				return false;

			var storedPin = await GetPinAsync(userId);
			return storedPin != null && string.Equals(storedPin, pin, StringComparison.Ordinal);
		}

		public async Task<string> GetPinAsync(string userId)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			if (string.IsNullOrWhiteSpace(profile?.SecurityPin))
				return null;

			try
			{
				return _encryptionService.Decrypt(profile.SecurityPin);
			}
			catch (Exception ex)
			{
				// An undecryptable value (corrupted or written with a different key) is treated as no PIN.
				Logging.LogException(ex);
				return null;
			}
		}

		public async Task<UserProfile> EnsurePinAsync(string userId, int departmentId, CancellationToken cancellationToken = default)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null)
				return null;

			if (!string.IsNullOrWhiteSpace(profile.SecurityPin))
				return profile;

			profile.SecurityPin = _encryptionService.Encrypt(SecurityPinUtility.Generate());
			return await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);
		}

		public async Task EnsurePinsForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId, bypassCache: true);
			if (profiles == null)
				return;

			// Batch: generate all PINs in memory, then persist in a single bulk update rather than a
			// full profile save per member.
			var toUpdate = profiles.Values
				.Where(p => string.IsNullOrWhiteSpace(p.SecurityPin))
				.ToList();

			if (toUpdate.Count == 0)
				return;

			var now = DateTime.UtcNow;
			foreach (var profile in toUpdate)
			{
				profile.SecurityPin = _encryptionService.Encrypt(SecurityPinUtility.Generate());
				profile.LastUpdated = now;
			}

			await _userProfileRepository.UpdateSecurityPinsAsync(toUpdate, cancellationToken);

			// Match SaveProfileAsync's cache eviction: each member's profile cache plus the
			// department-wide profile list.
			foreach (var profile in toUpdate)
				_userProfileService.ClearUserProfileFromCache(profile.UserId);

			_userProfileService.ClearAllUserProfilesFromCache(departmentId);
		}
	}
}
