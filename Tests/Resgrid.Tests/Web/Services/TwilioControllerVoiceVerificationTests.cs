using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers;
using Resgrid.Web.Services.Twilio;
using Twilio.AspNet.Common;
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
		private Mock<IFeatureToggleService> _featureToggleServiceMock;

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
			_featureToggleServiceMock = new Mock<IFeatureToggleService>();
			_twilioVoiceResponseServiceMock = new Mock<ITwilioVoiceResponseService>();
			_departmentSettingsServiceMock.Setup(x => x.GetTtsLanguageForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((string)null);
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
			_twilioVoiceResponseServiceMock
				.Setup(x => x.AppendPromptAsync(It.IsAny<Gather>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<Gather, string, CancellationToken, string>((gather, text, _, __) =>
				{
					gather.Append(new Play
					{
						Url = new Uri($"https://tts.example/{Uri.EscapeDataString(text)}.wav")
					});
					return System.Threading.Tasks.Task.CompletedTask;
				});
			_twilioVoiceResponseServiceMock
				.Setup(x => x.AppendPromptsAsync(It.IsAny<VoiceResponse>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<VoiceResponse, IEnumerable<string>, CancellationToken, string>((response, prompts, _, __) =>
				{
					foreach (var prompt in prompts)
					{
						response.Append(new Play
						{
							Url = new Uri($"https://tts.example/{Uri.EscapeDataString(prompt)}.wav")
						});
					}
					return System.Threading.Tasks.Task.CompletedTask;
				});
			_twilioVoiceResponseServiceMock
				.Setup(x => x.AppendPromptsAsync(It.IsAny<Gather>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<Gather, IEnumerable<string>, CancellationToken, string>((gather, prompts, _, __) =>
				{
					foreach (var prompt in prompts)
					{
						gather.Append(new Play
						{
							Url = new Uri($"https://tts.example/{Uri.EscapeDataString(prompt)}.wav")
						});
					}
					return System.Threading.Tasks.Task.CompletedTask;
				});
			_twilioVoiceResponseServiceMock
				.Setup(x => x.GetPromptUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns<string, string, CancellationToken>((text, _, __) =>
					System.Threading.Tasks.Task.FromResult(new Uri($"https://tts.example/{Uri.EscapeDataString(text)}.wav")));
			_twilioVoiceResponseServiceMock
				.Setup(x => x.PreWarmPromptAsync(It.IsAny<string>(), It.IsAny<string>()))
				.Returns(System.Threading.Tasks.Task.CompletedTask);
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
				_twilioVoiceResponseServiceMock.Object,
				_featureToggleServiceMock.Object,
				Mock.Of<ITextDepartmentSwitchService>());
		}

		private static string InvokeBuildDispatchPrompt(Type controllerType, Call call, string address)
		{
			var buildDispatchPrompt = controllerType.GetMethod("BuildDispatchPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			var prompt = buildDispatchPrompt?.Invoke(null, new object[] { call, address }) as string;

			prompt.Should().NotBeNull();
			return prompt!;
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
		public void voice_call_callback_actions_should_require_validate_request()
		{
			var callbackActions = new[]
			{
				nameof(TwilioController.VoiceCallAction),
				nameof(TwilioController.VoiceCallResponseOptions),
				nameof(TwilioController.VoiceCallRespond)
			};

			foreach (var actionName in callbackActions)
			{
				typeof(TwilioController)
					.GetMethod(actionName)!
					.CustomAttributes
					.Should()
					.Contain(attribute => attribute.AttributeType.Name == "ValidateRequestAttribute");
			}
		}

		[Test]
		public async System.Threading.Tasks.Task should_play_dispatch_before_outbound_response_menu()
		{
			var call = new Call
			{
				CallId = 42,
				DepartmentId = 7,
				Number = "42",
				Name = "Call 42",
				Priority = (int)CallPriority.High,
				Address = "123 Main St",
				NatureOfCall = "Structure fire"
			};

			_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, true)).ReturnsAsync(call);
			_callsServiceMock
				.Setup(x => x.PopulateCallData(It.Is<Call>(c => c.CallId == 42), true, true, false, false, false, false, false, false, false, false))
				.ReturnsAsync(call);

			var result = await BuildController().VoiceCall("user1", 42);

			var content = ((ContentResult)result).Content;
			var dispatchPrompt = Uri.EscapeDataString("Call 42, Priority High Address 123 Main St Nature Structure fire.");
			var menuPrompt = Uri.EscapeDataString(TwilioVoicePromptCatalog.OutboundDispatchMenu);

			content.Should().Contain(dispatchPrompt);
			content.Should().Contain("https://resgridapi.local/api/Twilio/VoiceCallAction?userId=user1&amp;callId=42");
			content.IndexOf(dispatchPrompt, StringComparison.Ordinal).Should().BeLessThan(content.IndexOf(menuPrompt, StringComparison.Ordinal));
		}

		[Test]
		public void dispatch_prompt_helpers_should_end_with_sentence_punctuation()
		{
			var call = new Call
			{
				Name = "Call 42",
				Priority = (int)CallPriority.High,
				Address = "123 Main St",
				NatureOfCall = "Structure fire"
			};

			InvokeBuildDispatchPrompt(typeof(TwilioController), call, "123 Main St")
				.Should().Be("Call 42, Priority High Address 123 Main St Nature Structure fire.");
			InvokeBuildDispatchPrompt(typeof(TwilioController), call, null)
				.Should().Be("Call 42, Priority High Nature Structure fire.");
		}

		[TestCase("1", "https://resgridapi.local/api/Twilio/VoiceCall?userId=user1&amp;callId=42")]
		[TestCase("2", "https://resgridapi.local/api/Twilio/VoiceCallResponseOptions?userId=user1&amp;callId=42")]
		public async System.Threading.Tasks.Task should_route_outbound_menu_selection_to_expected_step(string digits, string expectedUrl)
		{
			var result = await BuildController().VoiceCallAction("user1", 42, new VoiceRequest { Digits = digits });

			var content = ((ContentResult)result).Content;
			content.Should().Contain(expectedUrl);
		}

		[Test]
		public async System.Threading.Tasks.Task should_present_multi_digit_station_response_options()
		{
			var call = new Call { CallId = 42, DepartmentId = 7, Number = "42" };
			var stations = Enumerable.Range(1, 12)
				.Select(i => new DepartmentGroup { DepartmentGroupId = 200 + i, Name = $"Station {i}" })
				.ToList();

			_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, true)).ReturnsAsync(call);
			_departmentGroupsServiceMock.Setup(x => x.GetAllStationGroupsForDepartmentAsync(7)).ReturnsAsync(stations);

			var result = await BuildController().VoiceCallResponseOptions("user1", 42);

			var content = ((ContentResult)result).Content;
			content.Should().Contain("finishOnKey=\"#\"");
			content.Should().Contain(Uri.EscapeDataString(TwilioVoicePromptCatalog.OutboundResponseSelectionIntro));
			content.Should().Contain(Uri.EscapeDataString("To respond to Station 12, enter 13 and press pound."));
			content.Should().Contain(Uri.EscapeDataString(TwilioVoicePromptCatalog.RepeatDispatchWithPound));
		}

		[Test]
		public async System.Threading.Tasks.Task should_mark_station_response_from_multi_digit_selection()
		{
			var call = new Call { CallId = 42, DepartmentId = 7, Number = "42" };
			var stations = Enumerable.Range(1, 12)
				.Select(i => new DepartmentGroup { DepartmentGroupId = 300 + i, Name = $"Station {i}" })
				.ToList();
			var selectedStation = stations.Last();

			_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, true)).ReturnsAsync(call);
			_departmentGroupsServiceMock.Setup(x => x.GetAllStationGroupsForDepartmentAsync(7)).ReturnsAsync(stations);
			_actionLogsServiceMock
				.Setup(x => x.SetUserActionAsync("user1", 7, (int)ActionTypes.RespondingToStation, null, selectedStation.DepartmentGroupId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ActionLog());

			var result = await BuildController().VoiceCallRespond("user1", 42, new VoiceRequest { Digits = "13" });

			var content = ((ContentResult)result).Content;
			content.Should().Contain(Uri.EscapeDataString(TwilioVoicePromptCatalog.RespondingToStation(selectedStation.Name)));
			_actionLogsServiceMock.Verify(x => x.SetUserActionAsync("user1", 7, (int)ActionTypes.RespondingToStation, null, selectedStation.DepartmentGroupId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task should_present_multi_digit_status_options()
		{
			var department = new Department { DepartmentId = 7, Name = "Dept 1" };
			var profile = new UserProfile { UserId = "user1", FirstName = "Pat" };
			var options = Enumerable.Range(1, 12)
				.Select(i => new CustomStateDetail { CustomStateDetailId = 400 + i, ButtonText = $"Status {i}" })
				.ToList();

			_departmentsServiceMock.Setup(x => x.GetDepartmentByUserIdAsync("user1", false)).ReturnsAsync(department);
			_userProfileServiceMock.Setup(x => x.GetProfileByUserIdAsync("user1", false)).ReturnsAsync(profile);
			_customStateServiceMock.Setup(x => x.GetCustomPersonnelStatusesOrDefaultsAsync(7)).ReturnsAsync(options);

			var result = await BuildController().InboundVoiceAction("user1", new VoiceRequest { Digits = "6" });

			var content = ((ContentResult)result).Content;
			content.Should().Contain("finishOnKey=\"#\"");
			content.Should().NotContain("numDigits=\"1\"");
			content.Should().Contain(Uri.EscapeDataString(TwilioVoicePromptCatalog.StatusSelectionIntro));
			content.Should().Contain(Uri.EscapeDataString("For Status 12, enter 12 and press pound."));
			content.Should().Contain(Uri.EscapeDataString(TwilioVoicePromptCatalog.GoBackToMainMenuWithPound));
		}

		[Test]
		public async System.Threading.Tasks.Task should_mark_multi_digit_status_selection_by_menu_position()
		{
			var department = new Department { DepartmentId = 7, Name = "Dept 1" };
			var profile = new UserProfile { UserId = "user1", FirstName = "Pat" };
			var options = Enumerable.Range(1, 12)
				.Select(i => new CustomStateDetail { CustomStateDetailId = 500 + i, ButtonText = $"Status {i}" })
				.ToList();
			var selectedOption = options.Last();

			_departmentsServiceMock.Setup(x => x.GetDepartmentByUserIdAsync("user1", false)).ReturnsAsync(department);
			_userProfileServiceMock.Setup(x => x.GetProfileByUserIdAsync("user1", false)).ReturnsAsync(profile);
			_customStateServiceMock.Setup(x => x.GetCustomPersonnelStatusesOrDefaultsAsync(7)).ReturnsAsync(options);
			_actionLogsServiceMock
				.Setup(x => x.SetUserActionAsync("user1", 7, selectedOption.CustomStateDetailId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ActionLog());

			var result = await BuildController().InboundVoiceActionStatus("user1", new VoiceRequest { Digits = "12" });

			var content = ((ContentResult)result).Content;
			content.Should().Contain(Uri.EscapeDataString(TwilioVoicePromptCatalog.StatusMarked(selectedOption.ButtonText)));
			_actionLogsServiceMock.Verify(x => x.SetUserActionAsync("user1", 7, selectedOption.CustomStateDetailId, It.IsAny<CancellationToken>()), Times.Once);
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

		[Test]
		public void voice_prompt_catalog_should_use_sentence_punctuation_for_tts_playback()
		{
			TwilioVoicePromptCatalog.GetStaticPrompts()
				.Should()
				.OnlyContain(prompt => prompt.EndsWith(".", StringComparison.Ordinal));

			TwilioVoicePromptCatalog.GetStaticPrompts()
				.Should()
				.NotContain(prompt => prompt.Contains(", goodbye.", StringComparison.OrdinalIgnoreCase));

			TwilioVoicePromptCatalog.RespondingToScene.Should().Be("You have been marked responding to the scene. Goodbye.");
			TwilioVoicePromptCatalog.InboundVoiceUnavailable.Should().Be("Thank you for calling the Resgrid automated personnel system. The number you called is not tied to an active department, or the department doesn't have this feature enabled. Goodbye.");
			TwilioVoicePromptCatalog.InvalidStatusSelection.Should().Be("Invalid status selection. Returning to the main menu.");
			TwilioVoicePromptCatalog.NoStatusSelection.Should().Be("No status selection made. Returning to the main menu.");
			TwilioVoicePromptCatalog.CallClosedByNumber("42").Should().Be("This call, ID 42, has been closed. Goodbye.");
			TwilioVoicePromptCatalog.RespondingToStation("Station 12").Should().Be("You have been marked responding to Station 12. Goodbye.");
			TwilioVoicePromptCatalog.MainMenuGreeting("Pat", "Dept 1").Should().Be("Hello Pat. This is the Resgrid automated voice system for Dept 1.");
			TwilioVoicePromptCatalog.StatusMarked("Available").Should().Be("You have been marked as Available. Goodbye.");
		}

	}
}
