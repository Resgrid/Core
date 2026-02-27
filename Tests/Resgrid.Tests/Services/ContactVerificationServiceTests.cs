using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace ContactVerificationServiceTests
	{
		public class with_the_contact_verification_service : TestBase
		{
			protected Mock<IUserProfileService> _userProfileServiceMock;
			protected Mock<IUsersService> _usersServiceMock;
			protected Mock<IEmailService> _emailServiceMock;
			protected Mock<ISmsService> _smsServiceMock;
			protected Mock<ISystemAuditsService> _systemAuditsServiceMock;
			protected Mock<IEncryptionService> _encryptionServiceMock;
			protected IContactVerificationService _contactVerificationService;

			protected with_the_contact_verification_service()
			{
				_userProfileServiceMock = new Mock<IUserProfileService>();
				_usersServiceMock = new Mock<IUsersService>();
				_emailServiceMock = new Mock<IEmailService>();
				_smsServiceMock = new Mock<ISmsService>();
				_systemAuditsServiceMock = new Mock<ISystemAuditsService>();

				// Passthrough encryption mock: Encrypt wraps with "ENC:" prefix so tests can
				// verify the persisted value is transformed, and Decrypt strips it back.
				_encryptionServiceMock = new Mock<IEncryptionService>();
				_encryptionServiceMock
					.Setup(e => e.Encrypt(It.IsAny<string>()))
					.Returns<string>(plain => "ENC:" + plain);
				_encryptionServiceMock
					.Setup(e => e.Decrypt(It.IsAny<string>()))
					.Returns<string>(cipher => cipher.StartsWith("ENC:") ? cipher.Substring(4) : cipher);

				_systemAuditsServiceMock
					.Setup(s => s.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new SystemAudit());

				_contactVerificationService = new ContactVerificationService(
					_userProfileServiceMock.Object,
					_usersServiceMock.Object,
					_emailServiceMock.Object,
					_smsServiceMock.Object,
					_systemAuditsServiceMock.Object,
					_encryptionServiceMock.Object);
			}

			protected static UserProfile BuildProfile(string userId = "user1",
				string mobile = "5551234567",
				string home = "5559876543",
				string email = "user@example.com")
			{
				return new UserProfile
				{
					UserId = userId,
					FirstName = "Test",
					MobileNumber = mobile,
					HomeNumber = home,
					MembershipEmail = email
				};
			}
		}

		[TestFixture]
		public class when_sending_email_verification : with_the_contact_verification_service
		{
			[Test]
			public async Task should_generate_code_and_send_email()
			{
				var profile = BuildProfile();
				UserProfile savedProfile = null;

				_userProfileServiceMock
					.Setup(s => s.GetProfileByUserIdAsync("user1", true))
					.ReturnsAsync(profile);
				_usersServiceMock
					.Setup(s => s.GetUserById("user1", true))
					.Returns(new IdentityUser { Email = "user@example.com" });
				_userProfileServiceMock
					.Setup(s => s.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
					.Callback<int, UserProfile, CancellationToken>((_, p, _) => savedProfile = p)
					.ReturnsAsync(profile);
				_emailServiceMock
					.Setup(e => e.SendEmailVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
					.ReturnsAsync(true);

				var result = await _contactVerificationService.SendEmailVerificationCodeAsync("user1", 1);

				result.Should().BeTrue();
				savedProfile.Should().NotBeNull();
				// The service encrypts the code before persisting; verify the stored value is ciphertext.
				savedProfile.EmailVerificationCode.Should().StartWith("ENC:");
				savedProfile.EmailVerificationCodeExpiry.Should().NotBeNull();
				savedProfile.EmailVerificationCodeExpiry!.Value.Should().BeAfter(DateTime.UtcNow);
			}
		}

		[TestFixture]
		public class when_confirming_verification_code : with_the_contact_verification_service
		{
			[Test]
			public async Task should_mark_verified_on_correct_code_within_expiry()
			{
				var profile = BuildProfile();
				// Store the encrypted form (mock passthrough: "ENC:" + plaintext)
				profile.EmailVerificationCode = "ENC:123456";
				profile.EmailVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(25);
				profile.EmailVerificationAttempts = 0;

				UserProfile saved = null;
				_userProfileServiceMock
					.Setup(s => s.GetProfileByUserIdAsync("user1", true))
					.ReturnsAsync(profile);
				_userProfileServiceMock
					.Setup(s => s.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
					.Callback<int, UserProfile, CancellationToken>((_, p, _) => saved = p)
					.ReturnsAsync(profile);

				var result = await _contactVerificationService.ConfirmVerificationCodeAsync("user1", 1, ContactVerificationType.Email, "123456");

				result.Should().BeTrue();
				saved!.EmailVerified.Should().BeTrue();
				saved.EmailVerificationCode.Should().BeNull();
			}

			[Test]
			public async Task should_fail_on_expired_code()
			{
				var profile = BuildProfile();
				profile.EmailVerificationCode = "ENC:123456";
				profile.EmailVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(-5); // expired
				profile.EmailVerificationAttempts = 0;

				_userProfileServiceMock.Setup(s => s.GetProfileByUserIdAsync("user1", true)).ReturnsAsync(profile);
				_userProfileServiceMock.Setup(s => s.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>())).ReturnsAsync(profile);

				var result = await _contactVerificationService.ConfirmVerificationCodeAsync("user1", 1, ContactVerificationType.Email, "123456");

				result.Should().BeFalse();
				profile.EmailVerified.Should().NotBe(true);
			}

			[Test]
			public async Task should_fail_and_increment_attempts_on_wrong_code()
			{
				var profile = BuildProfile();
				profile.EmailVerificationCode = "ENC:123456";
				profile.EmailVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(25);
				profile.EmailVerificationAttempts = 0;

				UserProfile saved = null;
				_userProfileServiceMock.Setup(s => s.GetProfileByUserIdAsync("user1", true)).ReturnsAsync(profile);
				_userProfileServiceMock
					.Setup(s => s.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
					.Callback<int, UserProfile, CancellationToken>((_, p, _) => saved = p)
					.ReturnsAsync(profile);

				var result = await _contactVerificationService.ConfirmVerificationCodeAsync("user1", 1, ContactVerificationType.Email, "000000");

				result.Should().BeFalse();
				saved!.EmailVerificationAttempts.Should().Be(1);
				saved.EmailVerified.Should().NotBe(true);
			}

			[Test]
			public async Task should_fail_when_daily_attempt_cap_exceeded()
			{
				var profile = BuildProfile();
				profile.EmailVerificationCode = "ENC:123456";
				profile.EmailVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(25);
				profile.EmailVerificationAttempts = Resgrid.Config.VerificationConfig.MaxVerificationAttemptsPerDay; // at cap
				profile.EmailVerificationAttemptsResetDate = DateTime.UtcNow; // same day — no reset

				_userProfileServiceMock.Setup(s => s.GetProfileByUserIdAsync("user1", true)).ReturnsAsync(profile);
				_userProfileServiceMock.Setup(s => s.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>())).ReturnsAsync(profile);

				var result = await _contactVerificationService.ConfirmVerificationCodeAsync("user1", 1, ContactVerificationType.Email, "123456");

				result.Should().BeFalse();
			}

			[Test]
			public async Task should_reset_daily_attempts_after_reset_date_passes()
			{
				var profile = BuildProfile();
				profile.EmailVerificationCode = "ENC:123456";
				profile.EmailVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(25);
				profile.EmailVerificationAttempts = Resgrid.Config.VerificationConfig.MaxVerificationAttemptsPerDay;
				profile.EmailVerificationAttemptsResetDate = DateTime.UtcNow.AddDays(-1); // yesterday — triggers reset

				UserProfile saved = null;
				_userProfileServiceMock.Setup(s => s.GetProfileByUserIdAsync("user1", true)).ReturnsAsync(profile);
				_userProfileServiceMock
					.Setup(s => s.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
					.Callback<int, UserProfile, CancellationToken>((_, p, _) => saved = p)
					.ReturnsAsync(profile);

				// After reset the counter goes back to 0, so 1 attempt should succeed
				var result = await _contactVerificationService.ConfirmVerificationCodeAsync("user1", 1, ContactVerificationType.Email, "123456");

				result.Should().BeTrue();
				saved!.EmailVerified.Should().BeTrue();
			}
		}

		[TestFixture]
		public class when_resetting_verification_for_changed_contact : with_the_contact_verification_service
		{
			[Test]
			public async Task should_reset_mobile_verification_when_number_changes()
			{
				var existing = BuildProfile();
				existing.MobileNumber = "5551234567";
				existing.MobileNumberVerified = true;
				existing.MobileVerificationCode = "123456";

				var updated = BuildProfile();
				updated.MobileNumber = "5559999999"; // changed

				await _contactVerificationService.ResetVerificationForChangedContactAsync(existing, updated);

				updated.MobileNumberVerified.Should().BeFalse();
				updated.MobileVerificationCode.Should().BeNull();
				updated.MobileVerificationAttempts.Should().Be(0);
			}

			[Test]
			public async Task should_not_reset_mobile_when_number_unchanged()
			{
				var existing = BuildProfile();
				existing.MobileNumberVerified = true;
				existing.MobileVerificationCode = "123456";

				var updated = BuildProfile(); // same mobile number
				updated.MobileNumberVerified = true;
				updated.MobileVerificationCode = "123456";

				await _contactVerificationService.ResetVerificationForChangedContactAsync(existing, updated);

				updated.MobileNumberVerified.Should().BeTrue();
				updated.MobileVerificationCode.Should().Be("123456");
			}

			[Test]
			public async Task should_reset_email_verification_when_email_changes()
			{
				var existing = BuildProfile();
				existing.MembershipEmail = "old@example.com";
				existing.EmailVerified = true;

				var updated = BuildProfile();
				updated.MembershipEmail = "new@example.com"; // changed

				await _contactVerificationService.ResetVerificationForChangedContactAsync(existing, updated);

				updated.EmailVerified.Should().BeFalse();
				updated.EmailVerificationCode.Should().BeNull();
			}
		}

		[TestFixture]
		public class when_checking_contact_method_allowed_for_sending : with_the_contact_verification_service
		{
			[Test]
			public void null_status_should_be_allowed()
			{
				bool? status = null;
				status.IsContactMethodAllowedForSending().Should().BeTrue();
			}

			[Test]
			public void verified_true_should_be_allowed()
			{
				bool? status = true;
				status.IsContactMethodAllowedForSending().Should().BeTrue();
			}

			[Test]
			public void pending_false_should_not_be_allowed()
			{
				bool? status = false;
				status.IsContactMethodAllowedForSending().Should().BeFalse();
			}
		}
	}
}






