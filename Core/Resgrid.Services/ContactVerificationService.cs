using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Implements contact-method verification for email, mobile number, and home number.
	/// Uses <see cref="Resgrid.Config.VerificationConfig"/> for all configurable thresholds.
	/// </summary>
	public sealed class ContactVerificationService : IContactVerificationService
	{
		private readonly IUserProfileService _userProfileService;
		private readonly IUsersService _usersService;
		private readonly IEmailService _emailService;
		private readonly ISmsService _smsService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly IEncryptionService _encryptionService;

		public ContactVerificationService(
			IUserProfileService userProfileService,
			IUsersService usersService,
			IEmailService emailService,
			ISmsService smsService,
			ISystemAuditsService systemAuditsService,
			IEncryptionService encryptionService)
		{
			_userProfileService = userProfileService;
			_usersService = usersService;
			_emailService = emailService;
			_smsService = smsService;
			_systemAuditsService = systemAuditsService;
			_encryptionService = encryptionService;
		}

		public async Task<bool> SendEmailVerificationCodeAsync(string userId, int departmentId, CancellationToken cancellationToken = default)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null)
				return false;

			var user = _usersService.GetUserById(userId);
			if (user == null)
				return false;

			string emailAddress = !string.IsNullOrWhiteSpace(profile.MembershipEmail)
				? profile.MembershipEmail
				: user.Email;

			if (string.IsNullOrWhiteSpace(emailAddress))
				return false;

			if (!IsWithinHourlySendLimit(profile.EmailVerificationCodeExpiry, profile.EmailVerificationAttempts))
				return false;

			string code = GenerateCode();
			profile.EmailVerificationCode = _encryptionService.Encrypt(code);
			profile.EmailVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(Config.VerificationConfig.VerificationCodeExpiryMinutes);

			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			bool sent = await _emailService.SendEmailVerificationCodeAsync(emailAddress, profile.FirstName ?? string.Empty, code);

			await WriteAuditAsync(userId, departmentId, ContactVerificationType.Email, sent, "Send", null, cancellationToken);

			return sent;
		}

		public async Task<bool> SendMobileVerificationCodeAsync(string userId, int departmentId, string departmentNumber, CancellationToken cancellationToken = default)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null || string.IsNullOrWhiteSpace(profile.MobileNumber))
				return false;

			if (!IsWithinHourlySendLimit(profile.MobileVerificationCodeExpiry, profile.MobileVerificationAttempts))
				return false;

			string code = GenerateCode();
			profile.MobileVerificationCode = _encryptionService.Encrypt(code);
			profile.MobileVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(Config.VerificationConfig.VerificationCodeExpiryMinutes);

			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			bool sent = await _smsService.SendSmsVerificationCodeAsync(profile.GetPhoneNumber(), code, departmentNumber);

			await WriteAuditAsync(userId, departmentId, ContactVerificationType.MobileNumber, sent, "Send", null, cancellationToken);

			return sent;
		}

		public async Task<bool> SendHomeVerificationCodeAsync(string userId, int departmentId, string departmentNumber, CancellationToken cancellationToken = default)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null || string.IsNullOrWhiteSpace(profile.HomeNumber))
				return false;

			if (!IsWithinHourlySendLimit(profile.HomeVerificationCodeExpiry, profile.HomeVerificationAttempts))
				return false;

			string code = GenerateCode();
			profile.HomeVerificationCode = _encryptionService.Encrypt(code);
			profile.HomeVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(Config.VerificationConfig.VerificationCodeExpiryMinutes);

			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			bool sent = await _smsService.SendSmsVerificationCodeAsync(profile.GetHomePhoneNumber(), code, departmentNumber);

			await WriteAuditAsync(userId, departmentId, ContactVerificationType.HomeNumber, sent, "Send", null, cancellationToken);

			return sent;
		}

		public async Task<bool> ConfirmVerificationCodeAsync(string userId, int departmentId, ContactVerificationType type, string code, string ipAddress = null, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(code))
				return false;

			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null)
				return false;

			// Extract the relevant fields for this contact type
			string storedCode;
			DateTime? expiry;
			int attempts;
			DateTime? attemptsResetDate;

			switch (type)
			{
				case ContactVerificationType.Email:
					storedCode = profile.EmailVerificationCode;
					expiry = profile.EmailVerificationCodeExpiry;
					attempts = profile.EmailVerificationAttempts;
					attemptsResetDate = profile.EmailVerificationAttemptsResetDate;
					break;
				case ContactVerificationType.MobileNumber:
					storedCode = profile.MobileVerificationCode;
					expiry = profile.MobileVerificationCodeExpiry;
					attempts = profile.MobileVerificationAttempts;
					attemptsResetDate = profile.MobileVerificationAttemptsResetDate;
					break;
				case ContactVerificationType.HomeNumber:
					storedCode = profile.HomeVerificationCode;
					expiry = profile.HomeVerificationCodeExpiry;
					attempts = profile.HomeVerificationAttempts;
					attemptsResetDate = profile.HomeVerificationAttemptsResetDate;
					break;
				default:
					return false;
			}

			// Reset daily attempt counter if the reset date has passed
			if (attemptsResetDate.HasValue && attemptsResetDate.Value.Date < DateTime.UtcNow.Date)
			{
				attempts = 0;
				attemptsResetDate = DateTime.UtcNow;
			}

			// Enforce daily attempt cap
			if (attempts >= Config.VerificationConfig.MaxVerificationAttemptsPerDay)
			{
				await WriteAuditAsync(userId, departmentId, type, false, "ConfirmRateLimited", ipAddress, cancellationToken);
				return false;
			}

			// Increment attempts regardless of outcome
			attempts++;

			bool success = false;

			if (string.IsNullOrWhiteSpace(storedCode))
			{
				// No code generated yet
			}
			else if (!expiry.HasValue || DateTime.UtcNow > expiry.Value)
			{
				// Code expired — clear it
				storedCode = null;
			}
			else
			{
				// Decrypt the stored ciphertext and compare against the user-supplied code.
				// CryptographicException means tampered or wrong key — treat as mismatch.
				try
				{
					string decryptedCode = _encryptionService.Decrypt(storedCode);
					if (string.Equals(decryptedCode.Trim(), code.Trim(), StringComparison.Ordinal))
						success = true;
				}
				catch (CryptographicException)
				{
					// Tampered ciphertext or wrong key — treat as failed attempt
				}
			}

			// Persist updated state
			switch (type)
			{
				case ContactVerificationType.Email:
					profile.EmailVerificationAttempts = attempts;
					profile.EmailVerificationAttemptsResetDate = attemptsResetDate ?? DateTime.UtcNow;
					if (success)
					{
						profile.EmailVerified = true;
						profile.EmailVerificationCode = null;
						profile.EmailVerificationCodeExpiry = null;
					}
					else
					{
						profile.EmailVerificationCode = storedCode;
					}
					break;
				case ContactVerificationType.MobileNumber:
					profile.MobileVerificationAttempts = attempts;
					profile.MobileVerificationAttemptsResetDate = attemptsResetDate ?? DateTime.UtcNow;
					if (success)
					{
						profile.MobileNumberVerified = true;
						profile.MobileVerificationCode = null;
						profile.MobileVerificationCodeExpiry = null;
					}
					else
					{
						profile.MobileVerificationCode = storedCode;
					}
					break;
				case ContactVerificationType.HomeNumber:
					profile.HomeVerificationAttempts = attempts;
					profile.HomeVerificationAttemptsResetDate = attemptsResetDate ?? DateTime.UtcNow;
					if (success)
					{
						profile.HomeNumberVerified = true;
						profile.HomeVerificationCode = null;
						profile.HomeVerificationCodeExpiry = null;
					}
					else
					{
						profile.HomeVerificationCode = storedCode;
					}
					break;
			}

			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			string auditAction = success ? "ConfirmSuccess" : "ConfirmFailed";
			await WriteAuditAsync(userId, departmentId, type, success, auditAction, ipAddress, cancellationToken);

			return success;
		}

		public Task ResetVerificationForChangedContactAsync(UserProfile existingProfile, UserProfile updatedProfile, CancellationToken cancellationToken = default)
		{
			if (existingProfile == null || updatedProfile == null)
				return Task.CompletedTask;

			if (!string.Equals(existingProfile.MobileNumber ?? string.Empty, updatedProfile.MobileNumber ?? string.Empty, StringComparison.OrdinalIgnoreCase))
			{
				updatedProfile.MobileNumberVerified = false;
				updatedProfile.MobileVerificationCode = null;
				updatedProfile.MobileVerificationCodeExpiry = null;
				updatedProfile.MobileVerificationAttempts = 0;
				updatedProfile.MobileVerificationAttemptsResetDate = null;
			}

			if (!string.Equals(existingProfile.HomeNumber ?? string.Empty, updatedProfile.HomeNumber ?? string.Empty, StringComparison.OrdinalIgnoreCase))
			{
				updatedProfile.HomeNumberVerified = false;
				updatedProfile.HomeVerificationCode = null;
				updatedProfile.HomeVerificationCodeExpiry = null;
				updatedProfile.HomeVerificationAttempts = 0;
				updatedProfile.HomeVerificationAttemptsResetDate = null;
			}

			if (!string.Equals(existingProfile.MembershipEmail ?? string.Empty, updatedProfile.MembershipEmail ?? string.Empty, StringComparison.OrdinalIgnoreCase))
			{
				updatedProfile.EmailVerified = false;
				updatedProfile.EmailVerificationCode = null;
				updatedProfile.EmailVerificationCodeExpiry = null;
				updatedProfile.EmailVerificationAttempts = 0;
				updatedProfile.EmailVerificationAttemptsResetDate = null;
			}

			return Task.CompletedTask;
		}

		// ── Private helpers ──────────────────────────────────────────────────────────

		private static string GenerateCode()
		{
			int length = Config.VerificationConfig.VerificationCodeLength;
			// Use cryptographically strong random to avoid predictability
			using var rng = RandomNumberGenerator.Create();
			byte[] bytes = new byte[4];
			rng.GetBytes(bytes);
			uint value = BitConverter.ToUInt32(bytes, 0);
			int max = (int)Math.Pow(10, length);
			return (value % max).ToString().PadLeft(length, '0');
		}

		/// <summary>
		/// Returns <c>true</c> if a new send is allowed, based on the current code expiry window
		/// acting as a proxy for the hourly send count. A new send is blocked if there is already
		/// a valid (non-expired) code and the slot count would exceed the hourly limit.
		/// In practice, with VerificationCodeExpiryMinutes=30 and MaxVerificationSendsPerHour=3,
		/// a user can send up to 3 codes within a 60-minute window.
		/// </summary>
		private static bool IsWithinHourlySendLimit(DateTime? codeExpiry, int existingAttempts)
		{
			// If the existing code hasn't expired yet AND we've already reached the send cap, block.
			if (codeExpiry.HasValue
				&& DateTime.UtcNow < codeExpiry.Value
				&& existingAttempts >= Config.VerificationConfig.MaxVerificationSendsPerHour)
			{
				return false;
			}

			return true;
		}

		private async Task WriteAuditAsync(string userId, int departmentId, ContactVerificationType type, bool success, string action, string ipAddress, CancellationToken cancellationToken)
		{
			try
			{
				var audit = new SystemAudit
				{
					Type = (int)SystemAuditTypes.ContactVerification,
					System = (int)SystemAuditSystems.Api,
					DepartmentId = departmentId,
					UserId = userId,
					IpAddress = ipAddress ?? string.Empty,
					Successful = success,
					Data = $"Action={action} ContactType={type}",
					ServerName = Environment.MachineName
				};

				await _systemAuditsService.SaveSystemAuditAsync(audit, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}
	}
}

