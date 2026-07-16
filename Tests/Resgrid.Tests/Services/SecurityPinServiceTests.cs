using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace SecurityPinUtilityTests
	{
		[TestFixture]
		public class when_validating_pin_format
		{
			[TestCase("2580", ExpectedResult = true)]
			[TestCase("7392", ExpectedResult = true)]
			[TestCase("123", ExpectedResult = false)]
			[TestCase("12345", ExpectedResult = false)]
			[TestCase("12a4", ExpectedResult = false)]
			[TestCase("", ExpectedResult = false)]
			[TestCase(null, ExpectedResult = false)]
			[TestCase(" 123", ExpectedResult = false)]
			public bool validates_format(string pin) => SecurityPinUtility.IsValidFormat(pin);
		}

		[TestFixture]
		public class when_checking_pin_strength
		{
			[TestCase("0000")]
			[TestCase("1111")]
			[TestCase("9999")]
			[TestCase("1234")]
			[TestCase("0123")]
			[TestCase("6789")]
			[TestCase("4321")]
			[TestCase("9876")]
			[TestCase("3210")]
			public void rejects_weak_pins(string pin)
			{
				SecurityPinUtility.IsWeak(pin).Should().BeTrue();
				SecurityPinUtility.IsAcceptable(pin).Should().BeFalse();
			}

			[TestCase("2580")]
			[TestCase("1357")]
			[TestCase("7392")]
			[TestCase("1233")]
			[TestCase("1235")]
			public void accepts_normal_pins(string pin)
			{
				SecurityPinUtility.IsWeak(pin).Should().BeFalse();
				SecurityPinUtility.IsAcceptable(pin).Should().BeTrue();
			}
		}

		[TestFixture]
		public class when_generating_a_pin
		{
			[Test]
			public void generated_pins_are_always_acceptable()
			{
				for (int i = 0; i < 250; i++)
				{
					var pin = SecurityPinUtility.Generate();
					SecurityPinUtility.IsAcceptable(pin).Should().BeTrue($"generated pin '{pin}' should be acceptable");
				}
			}
		}
	}

	namespace SecurityPinServiceTests
	{
		public class with_the_security_pin_service : TestBase
		{
			protected Mock<IUserProfileService> _userProfileServiceMock;
			protected Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;
			protected Mock<IEncryptionService> _encryptionServiceMock;
			protected ISecurityPinService _securityPinService;

			protected const string UserId = "user1";
			protected const int DepartmentId = 42;

			[SetUp]
			public void SetUpSecurityPinService()
			{
				_userProfileServiceMock = new Mock<IUserProfileService>();
				_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();

				// Passthrough encryption mock: Encrypt wraps with "ENC:" so tests can verify the
				// persisted value is transformed, and Decrypt strips it back.
				_encryptionServiceMock = new Mock<IEncryptionService>();
				_encryptionServiceMock
					.Setup(e => e.Encrypt(It.IsAny<string>()))
					.Returns<string>(plain => "ENC:" + plain);
				_encryptionServiceMock
					.Setup(e => e.Decrypt(It.IsAny<string>()))
					.Returns<string>(cipher => cipher.StartsWith("ENC:") ? cipher.Substring(4) : cipher);

				_securityPinService = new SecurityPinService(
					_userProfileServiceMock.Object,
					_departmentSettingsServiceMock.Object,
					_encryptionServiceMock.Object);
			}

			protected void SetupProfile(UserProfile profile)
			{
				_userProfileServiceMock
					.Setup(s => s.GetProfileByUserIdAsync(UserId, It.IsAny<bool>()))
					.ReturnsAsync(profile);
			}

			protected void SetupForcePin(bool forced)
			{
				_departmentSettingsServiceMock
					.Setup(s => s.GetForceChatbotSecurityPinAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(forced);
			}
		}

		[TestFixture]
		public class when_checking_if_a_pin_is_required : with_the_security_pin_service
		{
			[Test]
			public async Task required_when_department_forces_pins()
			{
				SetupForcePin(true);
				SetupProfile(new UserProfile { UserId = UserId, SecurityPinEnabled = false });

				(await _securityPinService.IsPinRequiredAsync(UserId, DepartmentId)).Should().BeTrue();
			}

			[Test]
			public async Task required_when_the_user_opted_in()
			{
				SetupForcePin(false);
				SetupProfile(new UserProfile { UserId = UserId, SecurityPinEnabled = true });

				(await _securityPinService.IsPinRequiredAsync(UserId, DepartmentId)).Should().BeTrue();
			}

			[Test]
			public async Task not_required_when_neither_forced_nor_opted_in()
			{
				SetupForcePin(false);
				SetupProfile(new UserProfile { UserId = UserId, SecurityPinEnabled = false });

				(await _securityPinService.IsPinRequiredAsync(UserId, DepartmentId)).Should().BeFalse();
			}

			[Test]
			public async Task not_required_when_no_profile_exists_and_not_forced()
			{
				SetupForcePin(false);
				SetupProfile(null);

				(await _securityPinService.IsPinRequiredAsync(UserId, DepartmentId)).Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_validating_a_pin : with_the_security_pin_service
		{
			[Test]
			public async Task correct_pin_validates()
			{
				SetupProfile(new UserProfile { UserId = UserId, SecurityPin = "ENC:2580" });

				(await _securityPinService.ValidatePinAsync(UserId, "2580")).Should().BeTrue();
			}

			[Test]
			public async Task wrong_pin_fails()
			{
				SetupProfile(new UserProfile { UserId = UserId, SecurityPin = "ENC:2580" });

				(await _securityPinService.ValidatePinAsync(UserId, "2581")).Should().BeFalse();
			}

			[Test]
			public async Task malformed_pin_fails_without_touching_the_store()
			{
				(await _securityPinService.ValidatePinAsync(UserId, "not-a-pin")).Should().BeFalse();
				_userProfileServiceMock.Verify(s => s.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
			}

			[Test]
			public async Task fails_when_no_pin_is_set()
			{
				SetupProfile(new UserProfile { UserId = UserId, SecurityPin = null });

				(await _securityPinService.ValidatePinAsync(UserId, "2580")).Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_ensuring_a_pin : with_the_security_pin_service
		{
			[Test]
			public async Task generates_and_saves_a_pin_when_missing()
			{
				var profile = new UserProfile { UserId = UserId };
				SetupProfile(profile);
				_userProfileServiceMock
					.Setup(s => s.SaveProfileAsync(DepartmentId, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, UserProfile p, CancellationToken c) => p);

				var saved = await _securityPinService.EnsurePinAsync(UserId, DepartmentId);

				saved.SecurityPin.Should().StartWith("ENC:");
				SecurityPinUtility.IsAcceptable(saved.SecurityPin.Substring(4)).Should().BeTrue();
				_userProfileServiceMock.Verify(s => s.SaveProfileAsync(DepartmentId, profile, It.IsAny<CancellationToken>()), Times.Once);
			}

			[Test]
			public async Task keeps_the_existing_pin()
			{
				SetupProfile(new UserProfile { UserId = UserId, SecurityPin = "ENC:2580" });

				var saved = await _securityPinService.EnsurePinAsync(UserId, DepartmentId);

				saved.SecurityPin.Should().Be("ENC:2580");
				_userProfileServiceMock.Verify(s => s.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Never);
			}
		}

		[TestFixture]
		public class when_ensuring_pins_for_a_department : with_the_security_pin_service
		{
			[Test]
			public async Task only_members_without_a_pin_get_one()
			{
				var withPin = new UserProfile { UserId = "hasPin", SecurityPin = "ENC:2580" };
				var withoutPin = new UserProfile { UserId = "noPin" };

				_userProfileServiceMock
					.Setup(s => s.GetAllProfilesForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(new Dictionary<string, UserProfile> { { withPin.UserId, withPin }, { withoutPin.UserId, withoutPin } });
				_userProfileServiceMock
					.Setup(s => s.SaveProfileAsync(DepartmentId, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, UserProfile p, CancellationToken c) => p);

				await _securityPinService.EnsurePinsForDepartmentAsync(DepartmentId);

				withPin.SecurityPin.Should().Be("ENC:2580");
				withoutPin.SecurityPin.Should().StartWith("ENC:");
				_userProfileServiceMock.Verify(s => s.SaveProfileAsync(DepartmentId, withoutPin, It.IsAny<CancellationToken>()), Times.Once);
				_userProfileServiceMock.Verify(s => s.SaveProfileAsync(DepartmentId, withPin, It.IsAny<CancellationToken>()), Times.Never);
			}
		}
	}
}
