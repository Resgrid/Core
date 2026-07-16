using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
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

		public SecurityPinService(IUserProfileService userProfileService, IDepartmentSettingsService departmentSettingsService,
			IEncryptionService encryptionService)
		{
			_userProfileService = userProfileService;
			_departmentSettingsService = departmentSettingsService;
			_encryptionService = encryptionService;
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

			foreach (var profile in profiles.Values)
			{
				if (!string.IsNullOrWhiteSpace(profile.SecurityPin))
					continue;

				profile.SecurityPin = _encryptionService.Encrypt(SecurityPinUtility.Generate());
				await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);
			}
		}
	}
}
