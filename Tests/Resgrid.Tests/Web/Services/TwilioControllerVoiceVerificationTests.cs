using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class TwilioControllerVoiceVerificationTests : TestBase
	{
		private Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;
		private Mock<INumbersService> _numbersServiceMock;
		private Mock<ILimitsService> _limitsServiceMock;
		private Mock<ICallsService> _callsServiceMock;
		private Mock<IQueueService> _queueServiceMock;
		private Mock<IDepartmentsService> _departmentsServiceMock;
		private Mock<IUserProfileService> _userProfileServiceMock;
		private Mock<ITextCommandService> _textCommandServiceMock;
		private Mock<IActionLogsService> _actionLogsServiceMock;
		private Mock<IUserStateService> _userStateServiceMock;
		private Mock<ICommunicationService> _communicationServiceMock;
		private Mock<IGeoLocationProvider> _geoLocationProviderMock;
		private Mock<IDepartmentGroupsService> _departmentGroupsServiceMock;
		private Mock<ICustomStateService> _customStateServiceMock;
		private Mock<IUnitsService> _unitsServiceMock;
		private Mock<IUsersService> _usersServiceMock;
		private Mock<ICalendarService> _calendarServiceMock;
		private Mock<ICommunicationTestService> _communicationTestServiceMock;
		private Mock<IEncryptionService> _encryptionServiceMock;

		protected override void Before_all_tests()
		{
			_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();
			_numbersServiceMock = new Mock<INumbersService>();
			_limitsServiceMock = new Mock<ILimitsService>();
			_callsServiceMock = new Mock<ICallsService>();
			_queueServiceMock = new Mock<IQueueService>();
			_departmentsServiceMock = new Mock<IDepartmentsService>();
			_userProfileServiceMock = new Mock<IUserProfileService>();
			_textCommandServiceMock = new Mock<ITextCommandService>();
			_actionLogsServiceMock = new Mock<IActionLogsService>();
			_userStateServiceMock = new Mock<IUserStateService>();
			_communicationServiceMock = new Mock<ICommunicationService>();
			_geoLocationProviderMock = new Mock<IGeoLocationProvider>();
			_departmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
			_customStateServiceMock = new Mock<ICustomStateService>();
			_unitsServiceMock = new Mock<IUnitsService>();
			_usersServiceMock = new Mock<IUsersService>();
			_calendarServiceMock = new Mock<ICalendarService>();
			_communicationTestServiceMock = new Mock<ICommunicationTestService>();
			_encryptionServiceMock = new Mock<IEncryptionService>();
		}

		private TwilioController BuildController()
		{
			return new TwilioController(
				_departmentSettingsServiceMock.Object,
				_numbersServiceMock.Object,
				_limitsServiceMock.Object,
				_callsServiceMock.Object,
				_queueServiceMock.Object,
				_departmentsServiceMock.Object,
				_userProfileServiceMock.Object,
				_textCommandServiceMock.Object,
				_actionLogsServiceMock.Object,
				_userStateServiceMock.Object,
				_communicationServiceMock.Object,
				_geoLocationProviderMock.Object,
				_departmentGroupsServiceMock.Object,
				_customStateServiceMock.Object,
				_unitsServiceMock.Object,
				_usersServiceMock.Object,
				_calendarServiceMock.Object,
				_communicationTestServiceMock.Object,
				_encryptionServiceMock.Object);
		}

		[Test]
		public async Task should_mark_home_code_consumed_after_successful_voice_generation()
		{
			var profile = new UserProfile
			{
				UserId = "user1",
				HomeVerificationCode = "ENC:123456",
				HomeVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(10)
			};
			var department = new Department { DepartmentId = 7 };
			UserProfile savedProfile = null;

			_userProfileServiceMock.Setup(x => x.GetProfileByUserIdAsync("user1", true)).ReturnsAsync(profile);
			_encryptionServiceMock.Setup(x => x.Decrypt("ENC:123456")).Returns("123456");
			_departmentsServiceMock.Setup(x => x.GetDepartmentByUserIdAsync("user1", false)).ReturnsAsync(department);
			_userProfileServiceMock
				.Setup(x => x.SaveProfileAsync(7, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
				.Callback<int, UserProfile, CancellationToken>((_, p, _) => savedProfile = p)
				.ReturnsAsync(profile);

			var result = await BuildController().VoiceVerification("user1", (int)ContactVerificationType.HomeNumber);

			var content = ((ContentResult)result).Content;
			content.Should().Contain("Your verification code is: 1, 2, 3, 4, 5, 6.");
			savedProfile.Should().NotBeNull();
			savedProfile!.HomeVerificationVoiceCodeConsumed.Should().BeTrue();
			savedProfile.HomeVerificationCode.Should().Be("ENC:123456");
		}

		[Test]
		public async Task should_return_generic_message_when_home_code_already_consumed()
		{
			var profile = new UserProfile
			{
				UserId = "user1",
				HomeVerificationCode = "ENC:123456",
				HomeVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(10),
				HomeVerificationVoiceCodeConsumed = true
			};

			_userProfileServiceMock.Setup(x => x.GetProfileByUserIdAsync("user1", true)).ReturnsAsync(profile);

			var result = await BuildController().VoiceVerification("user1", (int)ContactVerificationType.HomeNumber);

			var content = ((ContentResult)result).Content;
			content.Should().Contain("We couldn't complete your verification call.");
			content.Should().NotContain("123456");
			_userProfileServiceMock.Verify(x => x.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task should_return_generic_message_when_decryption_fails()
		{
			var profile = new UserProfile
			{
				UserId = "user1",
				HomeVerificationCode = "ENC:broken",
				HomeVerificationCodeExpiry = DateTime.UtcNow.AddMinutes(10)
			};

			_userProfileServiceMock.Setup(x => x.GetProfileByUserIdAsync("user1", true)).ReturnsAsync(profile);
			_encryptionServiceMock.Setup(x => x.Decrypt("ENC:broken")).Throws(new CryptographicException("bad"));

			var result = await BuildController().VoiceVerification("user1", (int)ContactVerificationType.HomeNumber);

			var content = ((ContentResult)result).Content;
			content.Should().Contain("We couldn't complete your verification call.");
			content.Should().NotContain("broken");
		}
	}
}
