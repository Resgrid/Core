using System;
using System.Collections.Generic;
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
using Resgrid.Web.Services.Twilio;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

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
		private Mock<ITwilioVoiceResponseService> _twilioVoiceResponseServiceMock;

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
			_twilioVoiceResponseServiceMock = new Mock<ITwilioVoiceResponseService>();
			_twilioVoiceResponseServiceMock
				.Setup(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<VoiceResponse, string, CancellationToken, string>((response, text, _, __) =>
				{
					response.Append(new Play
					{
						Url = new Uri($"https://tts.example/{Uri.EscapeDataString(text)}.wav")
					});
					return System.Threading.Tasks.Task.CompletedTask;
				});
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
				_encryptionServiceMock.Object,
				_twilioVoiceResponseServiceMock.Object);
		}

		[Test]
		public async System.Threading.Tasks.Task should_mark_home_code_consumed_after_successful_voice_generation()
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
			_departmentSettingsServiceMock.Setup(x => x.GetTtsLanguageForDepartmentAsync(7)).ReturnsAsync("es");
			_userProfileServiceMock
				.Setup(x => x.SaveProfileAsync(7, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
				.Callback<int, UserProfile, CancellationToken>((_, p, _) => savedProfile = p)
				.ReturnsAsync(profile);

			var result = await BuildController().VoiceVerification("user1", (int)ContactVerificationType.HomeNumber);

			var content = ((ContentResult)result).Content;
			content.Should().Contain("<Play>");
			content.Should().Contain(Uri.EscapeDataString("Your verification code is: 1, 2, 3, 4, 5, 6."));
			savedProfile.Should().NotBeNull();
			savedProfile!.HomeVerificationVoiceCodeConsumed.Should().BeTrue();
			savedProfile.HomeVerificationCode.Should().Be("ENC:123456");
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), "Hello, this is Resgrid calling with your verification code.", It.IsAny<CancellationToken>(), "es"), Times.Once);
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), "Your verification code is: 1, 2, 3, 4, 5, 6.", It.IsAny<CancellationToken>(), "es"), Times.Exactly(3));
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), "That was your Resgrid verification code. Goodbye.", It.IsAny<CancellationToken>(), "es"), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task should_return_generic_message_when_home_code_already_consumed()
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
			content.Should().Contain("<Play>");
			content.Should().Contain(Uri.EscapeDataString("We couldn't complete your verification call. Please request a new code and try again. Goodbye."));
			content.Should().NotContain("123456");
			_userProfileServiceMock.Verify(x => x.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Never);
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), "We couldn't complete your verification call. Please request a new code and try again. Goodbye.", It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task should_return_generic_message_when_decryption_fails()
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
			content.Should().Contain("<Play>");
			content.Should().Contain(Uri.EscapeDataString("We couldn't complete your verification call. Please request a new code and try again. Goodbye."));
			content.Should().NotContain("broken");
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), "We couldn't complete your verification call. Please request a new code and try again. Goodbye.", It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task should_redirect_invalid_status_selection_back_to_user_scoped_menu()
		{
			var department = new Department { DepartmentId = 7, Name = "Dept 1" };
			var profile = new UserProfile { UserId = "user1", FirstName = "Pat" };

			_departmentsServiceMock.Setup(x => x.GetDepartmentByUserIdAsync("user1", false)).ReturnsAsync(department);
			_userProfileServiceMock.Setup(x => x.GetProfileByUserIdAsync("user1", false)).ReturnsAsync(profile);
			_customStateServiceMock.Setup(x => x.GetCustomPersonnelStatusesOrDefaultsAsync(7)).ReturnsAsync(new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = 1, ButtonText = "Available" }
			});

			var controller = BuildController();
			var action = typeof(TwilioController).GetMethod(nameof(TwilioController.InboundVoiceActionStatus));
			dynamic request = Activator.CreateInstance(action!.GetParameters()[1].ParameterType);
			request.Digits = "9";
			var result = await (Task<ActionResult>)action.Invoke(controller, new object[] { "user1", request });

			var content = ((ContentResult)result).Content;
			content.Should().Contain("https://resgridapi.local/api/Twilio/InboundVoiceAction?userId=user1");
			content.Should().NotContain("https://resgridapi.local/api/Twilio/InboundVoice</Redirect>");
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), TwilioVoicePromptCatalog.InvalidStatusSelection, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task should_redirect_missing_status_selection_back_to_user_scoped_menu()
		{
			var department = new Department { DepartmentId = 7, Name = "Dept 1" };
			var profile = new UserProfile { UserId = "user1", FirstName = "Pat" };

			_departmentsServiceMock.Setup(x => x.GetDepartmentByUserIdAsync("user1", false)).ReturnsAsync(department);
			_userProfileServiceMock.Setup(x => x.GetProfileByUserIdAsync("user1", false)).ReturnsAsync(profile);
			_customStateServiceMock.Setup(x => x.GetCustomPersonnelStatusesOrDefaultsAsync(7)).ReturnsAsync(new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = 1, ButtonText = "Available" }
			});

			var controller = BuildController();
			var action = typeof(TwilioController).GetMethod(nameof(TwilioController.InboundVoiceActionStatus));
			var request = Activator.CreateInstance(action!.GetParameters()[1].ParameterType);
			var result = await (Task<ActionResult>)action.Invoke(controller, new object[] { "user1", request });

			var content = ((ContentResult)result).Content;
			content.Should().Contain("https://resgridapi.local/api/Twilio/InboundVoiceAction?userId=user1");
			content.Should().NotContain("https://resgridapi.local/api/Twilio/InboundVoice</Redirect>");
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), TwilioVoicePromptCatalog.NoStatusSelection, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
		}
	}
}
