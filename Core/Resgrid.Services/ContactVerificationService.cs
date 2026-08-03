using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
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
		private readonly IOutboundVoiceProvider _outboundVoiceProvider;
		private readonly IPhoneNumberProcesserProvider _phoneNumberProcesser;

		public ContactVerificationService(
			IUserProfileService userProfileService,
			IUsersService usersService,
			IEmailService emailService,
			ISmsService smsService,
			ISystemAuditsService systemAuditsService,
			IEncryptionService encryptionService,
			IOutboundVoiceProvider outboundVoiceProvider,
			IPhoneNumberProcesserProvider phoneNumberProcesser)
		{
			_userProfileService = userProfileService;
			_usersService = usersService;
			_emailService = emailService;
			_smsService = smsService;
			_systemAuditsService = systemAuditsService;
			_encryptionService = encryptionService;
			_outboundVoiceProvider = outboundVoiceProvider;
			_phoneNumberProcesser = phoneNumberProcesser;
		}

		public async Task<ContactVerificationSendStatus> SendEmailVerificationCodeAsync(string userId, int departmentId, CancellationToken cancellationToken = default)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null)
				return ContactVerificationSendStatus.ContactNotConfigured;

			var user = _usersService.GetUserById(userId);
			if (user == null)
				return ContactVerificationSendStatus.ContactNotConfigured;

			string emailAddress = !string.IsNullOrWhiteSpace(profile.MembershipEmail)
				? profile.MembershipEmail
				: user.Email;

			if (string.IsNullOrWhiteSpace(emailAddress))
				return ContactVerificationSendStatus.ContactNotConfigured;

			if (!TryRecordSend(profile.EmailVerificationSendWindowStart, profile.EmailVerificationSendCount, out DateTime emailWindowStart, out int emailSendCount))
				return ContactVerificationSendStatus.RateLimited;

			profile.EmailVerificationSendWindowStart = emailWindowStart;
			profile.EmailVerificationSendCount = emailSendCount;

			string code = GenerateCode();
			profile.EmailVerificationCode = _encryptionService.Encrypt(code);
			profile.EmailVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(Config.VerificationConfig.VerificationCodeExpiryMinutes);

			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			bool sent = await _emailService.SendEmailVerificationCodeAsync(emailAddress, profile.FirstName ?? string.Empty, code);

			await WriteAuditAsync(userId, departmentId, ContactVerificationType.Email, sent, "Send", null, cancellationToken);

			return sent ? ContactVerificationSendStatus.Sent : ContactVerificationSendStatus.DeliveryFailed;
		}

		public async Task<ContactVerificationSendStatus> SendMobileVerificationCodeAsync(string userId, int departmentId, string departmentNumber, CancellationToken cancellationToken = default)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null || string.IsNullOrWhiteSpace(profile.MobileNumber))
				return ContactVerificationSendStatus.ContactNotConfigured;

			// Normalize to E.164 and validate before sending so an invalid/local-format number (e.g. a bare
			// "082446..." with no country code) is rejected here instead of throwing a Twilio "Invalid 'To'" error.
			var mobileResult = _phoneNumberProcesser.Process(profile.MobileNumber);
			if (mobileResult == null || !mobileResult.IsValid || string.IsNullOrWhiteSpace(mobileResult.InternationalNumber))
			{
				Logging.LogInfo($"Mobile verification SMS skipped for user {userId}: phone number is not a valid sendable number (needs international format, e.g. +<country code><number>).");
				await WriteAuditAsync(userId, departmentId, ContactVerificationType.MobileNumber, false, "Send-InvalidNumber", null, cancellationToken);
				return ContactVerificationSendStatus.InvalidContact;
			}

			if (!TryRecordSend(profile.MobileVerificationSendWindowStart, profile.MobileVerificationSendCount, out DateTime mobileWindowStart, out int mobileSendCount))
				return ContactVerificationSendStatus.RateLimited;

			profile.MobileVerificationSendWindowStart = mobileWindowStart;
			profile.MobileVerificationSendCount = mobileSendCount;

			string code = GenerateCode();
			profile.MobileVerificationCode = _encryptionService.Encrypt(code);
			profile.MobileVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(Config.VerificationConfig.VerificationCodeExpiryMinutes);
			profile.MobileVerificationVoiceCodeConsumed = false;

			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			bool sent = await _smsService.SendSmsVerificationCodeAsync(mobileResult.InternationalNumber, code, departmentNumber);

			await WriteAuditAsync(userId, departmentId, ContactVerificationType.MobileNumber, sent, "Send", null, cancellationToken);

			return sent ? ContactVerificationSendStatus.Sent : ContactVerificationSendStatus.DeliveryFailed;
		}

		public async Task<ContactVerificationSendStatus> SendHomeVerificationCodeAsync(string userId, int departmentId, string departmentNumber, CancellationToken cancellationToken = default)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null || string.IsNullOrWhiteSpace(profile.HomeNumber))
				return ContactVerificationSendStatus.ContactNotConfigured;

			// Validate/normalize before placing the Twilio voice call so an invalid number doesn't throw "Invalid 'To'".
			var homeResult = _phoneNumberProcesser.Process(profile.HomeNumber);
			if (homeResult == null || !homeResult.IsValid || string.IsNullOrWhiteSpace(homeResult.InternationalNumber))
			{
				Logging.LogInfo($"Home verification call skipped for user {userId}: phone number is not a valid sendable number.");
				await WriteAuditAsync(userId, departmentId, ContactVerificationType.HomeNumber, false, "SendVoice-InvalidNumber", null, cancellationToken);
				return ContactVerificationSendStatus.InvalidContact;
			}

			if (!TryRecordSend(profile.HomeVerificationSendWindowStart, profile.HomeVerificationSendCount, out DateTime homeWindowStart, out int homeSendCount))
				return ContactVerificationSendStatus.RateLimited;

			profile.HomeVerificationSendWindowStart = homeWindowStart;
			profile.HomeVerificationSendCount = homeSendCount;

			string code = GenerateCode();
			profile.HomeVerificationCode = _encryptionService.Encrypt(code);
			profile.HomeVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(Config.VerificationConfig.VerificationCodeExpiryMinutes);
			profile.HomeVerificationVoiceCodeConsumed = false;

			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			// Use a Twilio voice call instead of SMS for home numbers, since they may be
			// landlines that cannot receive text messages. The call speaks the digits
			// of the verification code, repeating multiple times so the user can note them.
			bool sent = await _outboundVoiceProvider.SendVoiceVerificationCallAsync(
				homeResult.InternationalNumber, userId, (int)ContactVerificationType.HomeNumber);

			await WriteAuditAsync(userId, departmentId, ContactVerificationType.HomeNumber, sent, "SendVoice", null, cancellationToken);

			return sent ? ContactVerificationSendStatus.Sent : ContactVerificationSendStatus.DeliveryFailed;
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
				updatedProfile.MobileVerificationVoiceCodeConsumed = false;
				updatedProfile.MobileVerificationAttempts = 0;
				updatedProfile.MobileVerificationAttemptsResetDate = null;
				updatedProfile.MobileVerificationSendCount = 0;
				updatedProfile.MobileVerificationSendWindowStart = null;
			}

			if (!string.Equals(existingProfile.HomeNumber ?? string.Empty, updatedProfile.HomeNumber ?? string.Empty, StringComparison.OrdinalIgnoreCase))
			{
				updatedProfile.HomeNumberVerified = false;
				updatedProfile.HomeVerificationCode = null;
				updatedProfile.HomeVerificationCodeExpiry = null;
				updatedProfile.HomeVerificationVoiceCodeConsumed = false;
				updatedProfile.HomeVerificationAttempts = 0;
				updatedProfile.HomeVerificationAttemptsResetDate = null;
				updatedProfile.HomeVerificationSendCount = 0;
				updatedProfile.HomeVerificationSendWindowStart = null;
			}

			if (!string.Equals(existingProfile.MembershipEmail ?? string.Empty, updatedProfile.MembershipEmail ?? string.Empty, StringComparison.OrdinalIgnoreCase))
			{
				updatedProfile.EmailVerified = false;
				updatedProfile.EmailVerificationCode = null;
				updatedProfile.EmailVerificationCodeExpiry = null;
				updatedProfile.EmailVerificationAttempts = 0;
				updatedProfile.EmailVerificationAttemptsResetDate = null;
				updatedProfile.EmailVerificationSendCount = 0;
				updatedProfile.EmailVerificationSendWindowStart = null;
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
		/// Enforces the hourly send cap using dedicated send-window state, kept separate from the
		/// daily confirm-attempt counters used by <see cref="ConfirmVerificationCodeAsync"/>.
		/// Returns <c>false</c> when <see cref="Resgrid.Config.VerificationConfig.MaxVerificationSendsPerHour"/>
		/// has been reached inside the current one-hour window; otherwise returns <c>true</c> with the
		/// updated window start and send count, which the caller must persist before delivering the code
		/// so the send is recorded even if delivery fails.
		/// </summary>
		private static bool TryRecordSend(DateTime? windowStart, int sendCount, out DateTime newWindowStart, out int newSendCount)
		{
			DateTime now = DateTime.UtcNow;

			if (!windowStart.HasValue || now - windowStart.Value >= TimeSpan.FromHours(1))
			{
				// No window yet, or the previous window has elapsed — start a new one.
				newWindowStart = now;
				newSendCount = 1;
				return true;
			}

			newWindowStart = windowStart.Value;

			if (sendCount >= Config.VerificationConfig.MaxVerificationSendsPerHour)
			{
				newSendCount = sendCount;
				return false;
			}

			newSendCount = sendCount + 1;
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

