using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers;
using Resgrid.Web.Services.Models;
using Resgrid.Web.Services.Twilio;
using Twilio.TwiML;
using Gather = Twilio.TwiML.Voice.Gather;
using Play = Twilio.TwiML.Voice.Play;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Twilio routing for a department's dedicated number: texts become intake (acks from the master number), a lapsed number
	/// refuses politely, the master number keeps every existing path; calls reach the member IVR, guided intake or the text-only
	/// message as VoiceIntakeService decides.
	/// </summary>
	[TestFixture]
	public class TwilioDedicatedNumberTests
	{
		private const int DepartmentId = 55;
		private const string Dedicated = "+17755550100";
		private const string Caller = "+15305550199";

		private Mock<IDepartmentPhoneNumbersService> _numbers;
		private Mock<IIntakeService> _intake;
		private Mock<IVoiceIntakeService> _voice;
		private Mock<ITextMessageProvider> _text;
		private Mock<IUserProfileService> _profiles;
		private Mock<IDepartmentsService> _departments;
		private Mock<ITextCommandService> _commands;
		private Mock<INumbersService> _legacyNumbers;
		private Mock<ILimitsService> _limits;
		private Mock<ITwilioVoiceResponseService> _prompts;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IIntakeConversationService> _conversations;
		private Mock<IIntakeConversationOperations> _conversationOperations;
		private Mock<ICommunicationTestService> _communicationTests;
		private IntakeConversation _thread;
		private DepartmentPhoneNumber _number;
		private IntakeEnvelope _received;
		private readonly List<string> _spoken = new List<string>();

		[SetUp]
		public void SetUp()
		{
			_received = null;
			_spoken.Clear();
			_number = new DepartmentPhoneNumber { DepartmentId = DepartmentId, PhoneNumber = Dedicated, Status = (int)DepartmentPhoneNumberStatuses.Active, SmsIntakeEnabled = true, VoiceIntakeEnabled = true, VoiceCapable = true };

			_numbers = new Mock<IDepartmentPhoneNumbersService>();
			_numbers.Setup(n => n.GetByNumberAsync(Dedicated)).ReturnsAsync(() => _number);
			_intake = new Mock<IIntakeService>();
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback((IntakeEnvelope e, CancellationToken _) => _received = e)
				.ReturnsAsync((IntakeEnvelope e, CancellationToken _) => e.RefusalReason != null
					? new IntakeResult { State = IntakeMessageStates.Refused, Reason = e.RefusalReason }
					: new IntakeResult { State = IntakeMessageStates.CallCreated, CallId = 900, Acknowledgement = "Received by Springfield Fire." });
			_voice = new Mock<IVoiceIntakeService>();
			_text = new Mock<ITextMessageProvider>();
			_profiles = new Mock<IUserProfileService>();
			_departments = new Mock<IDepartmentsService>();
			_commands = new Mock<ITextCommandService>();
			_commands.Setup(c => c.DetermineType(It.IsAny<string>())).Returns(new TextCommandPayload { Type = TextCommandTypes.None });
			_legacyNumbers = new Mock<INumbersService>();
			_limits = new Mock<ILimitsService>();
			_flags = new Mock<IFeatureToggleService>();
			_thread = null;
			_conversations = new Mock<IIntakeConversationService>();
			_conversations.Setup(c => c.FindOpenForReplyAsync(IntakeChannelTypes.Sms, Caller, It.IsAny<DateTime>())).ReturnsAsync(() => _thread);
			_conversations.Setup(c => c.HandleParticipantMessageAsync(It.IsAny<IntakeConversation>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_conversationOperations = new Mock<IIntakeConversationOperations>();
			_communicationTests = new Mock<ICommunicationTestService>();
			_prompts = new Mock<ITwilioVoiceResponseService>();
			_prompts.Setup(p => p.AppendPromptAsync(It.IsAny<VoiceResponse>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Callback((VoiceResponse r, string text, CancellationToken _, string __) => { _spoken.Add(text); r.Append(new Play(new Uri("https://tts.example/p.wav"))); })
				.Returns(Task.CompletedTask);
			_prompts.Setup(p => p.AppendPromptAsync(It.IsAny<Gather>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Callback((Gather g, string text, CancellationToken _, string __) => { _spoken.Add(text); g.Append(new Play(new Uri("https://tts.example/p.wav"))); })
				.Returns(Task.CompletedTask);
		}

		private TwilioController Controller()
		{
			return new TwilioController(Mock.Of<IDepartmentSettingsService>(), _legacyNumbers.Object, _limits.Object, Mock.Of<ICallsService>(), Mock.Of<IQueueService>(),
				_departments.Object, _profiles.Object, _commands.Object, Mock.Of<IActionLogsService>(), Mock.Of<IUserStateService>(), Mock.Of<ICommunicationService>(),
				Mock.Of<IGeoLocationProvider>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<ICustomStateService>(), Mock.Of<IUnitsService>(), Mock.Of<IUsersService>(),
				Mock.Of<ICalendarService>(), _communicationTests.Object, Mock.Of<IEncryptionService>(), _prompts.Object, _flags.Object,
				Mock.Of<ITextDepartmentSwitchService>(), Mock.Of<IDispatchRecommendationService>(), Mock.Of<IDispatchScopeService>(),
				Mock.Of<Resgrid.Model.Services.IAuthorizationService>(), Mock.Of<IAdpReleaseService>(), Mock.Of<IProtectedProjectionService>(),
				_numbers.Object, _intake.Object, _voice.Object, _text.Object, _conversations.Object, _conversationOperations.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
			};
		}

		private static TwilioMessage Text(string to = Dedicated, string body = "Need a lift assist at 12 Oak St") =>
			new TwilioMessage { To = to, From = Caller, Body = body, MessageSid = "SM1" };

		private static string Content(ActionResult result) => ((ContentResult)result).Content;

		[Test]
		public async Task a_text_to_a_dedicated_number_is_intake_and_the_ack_goes_from_the_master_number()
		{
			var result = await Controller().IncomingMessage(Text());

			_received.ChannelType.Should().Be(IntakeChannelTypes.Sms);
			_received.ChannelAddress.Should().Be(Dedicated);
			_received.Sender.Should().Be(Caller);
			_received.ProviderMessageId.Should().Be("SM1");
			_received.RefusalReason.Should().BeNull();
			_received.OpensConversation.Should().BeTrue("a text to a dedicated number continues as a conversation on the master number");
			Content(result).Should().NotContain("<Message>", "the dedicated number never sends; replies go from the master number");
			_text.Verify(t => t.SendTextMessage(Caller, "Received by Springfield Fire.", null, It.IsAny<MobileCarriers>(), DepartmentId, false, false, 0), Times.Once);
			_profiles.Verify(p => p.GetProfileByMobileNumberAsync(It.IsAny<string>()), Times.Never, "intake runs before profile routing");
			_legacyNumbers.Verify(n => n.SaveInboundMessageEventAsync(It.IsAny<InboundMessageEvent>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_lapsed_number_records_a_refusal_and_replies_politely()
		{
			_number.Status = (int)DepartmentPhoneNumberStatuses.Lapsed;

			await Controller().IncomingMessage(Text());

			_received.RefusalReason.Should().Be("NumberLapsed");
			_received.OpensConversation.Should().BeFalse();
			_text.Verify(t => t.SendTextMessage(Caller, TwilioController.LapsedNumberReply, null, It.IsAny<MobileCarriers>(), DepartmentId, false, false, 0), Times.Once);
		}

		[Test]
		public async Task stop_to_a_dedicated_number_creates_nothing_and_sends_nothing()
		{
			_commands.Setup(c => c.DetermineType("STOP")).Returns(new TextCommandPayload { Type = TextCommandTypes.Stop });

			await Controller().IncomingMessage(Text(body: "STOP"));

			_received.Should().BeNull();
			_text.Verify(t => t.SendTextMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task the_master_number_keeps_the_existing_paths()
		{
			await Controller().IncomingMessage(Text(to: "+18885550000"));

			_received.Should().BeNull();
			_profiles.Verify(p => p.GetProfileByMobileNumberAsync("15305550199"), Times.AtLeastOnce);
		}

		[Test]
		public async Task a_dedicated_number_with_sms_intake_off_keeps_the_existing_paths()
		{
			_number.SmsIntakeEnabled = false;

			await Controller().IncomingMessage(Text());

			_received.Should().BeNull();
			_profiles.Verify(p => p.GetProfileByMobileNumberAsync("15305550199"), Times.AtLeastOnce);
		}

		private static TwilioGatherRequest Call() => new TwilioGatherRequest { To = Dedicated, From = Caller, CallSid = "CA1" };

		[Test]
		public async Task a_non_member_call_starts_guided_speech_intake()
		{
			_voice.Setup(v => v.RouteAsync(_number, Caller, It.IsAny<DateTime>())).ReturnsAsync(new VoiceIntakeRouting { Route = VoiceIntakeRoutes.Intake, DepartmentId = DepartmentId });
			_voice.Setup(v => v.StartAsync(_number, "CA1", Caller, It.IsAny<CancellationToken>())).ReturnsAsync(new VoiceIntakeTurn
			{
				Step = VoiceIntakeSteps.Nature, Gather = true,
				Prompts = new List<string> { TwilioVoicePromptCatalog.VoiceIntakeGreeting, TwilioVoicePromptCatalog.VoiceIntakeAskNature }
			});

			var twiml = Content(await Controller().InboundVoice(Call()));

			twiml.Should().Contain("<Gather").And.Contain("input=\"speech\"").And.Contain("/api/Twilio/VoiceIntakeAnswer?step=1");
			twiml.Should().Contain("speechTimeout=\"auto\"");
			twiml.Should().Contain("actionOnEmptyResult=\"true\"");
			_spoken.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeGreeting, TwilioVoicePromptCatalog.VoiceIntakeAskNature);
			_profiles.Verify(p => p.GetProfileByMobileNumberAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task a_member_call_gets_the_member_menu_in_the_numbers_department()
		{
			_voice.Setup(v => v.RouteAsync(_number, Caller, It.IsAny<DateTime>())).ReturnsAsync(new VoiceIntakeRouting { Route = VoiceIntakeRoutes.MemberMenu, DepartmentId = DepartmentId, MemberUserId = "member-1" });
			_profiles.Setup(p => p.GetProfileByUserIdAsync("member-1", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "member-1", FirstName = "Pat" });
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Springfield Fire" });
			_limits.Setup(l => l.CanDepartmentProvisionNumberAsync(DepartmentId)).ReturnsAsync(true);

			var twiml = Content(await Controller().InboundVoice(Call()));

			twiml.Should().Contain("InboundVoiceAction?userId=member-1");
			_spoken.Should().Contain(TwilioVoicePromptCatalog.MainMenuGreeting("Pat", "Springfield Fire"));
			_departments.Verify(d => d.GetDepartmentByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never, "the number's department, not the profile's default");
		}

		[TestCase(VoiceIntakeRoutes.TextOnly, TwilioVoicePromptCatalog.VoiceIntakeTextOnly)]
		[TestCase(VoiceIntakeRoutes.NumberInactive, TwilioVoicePromptCatalog.VoiceIntakeNumberInactive)]
		public async Task other_routes_say_one_message_and_hang_up(VoiceIntakeRoutes route, string prompt)
		{
			_voice.Setup(v => v.RouteAsync(_number, Caller, It.IsAny<DateTime>())).ReturnsAsync(new VoiceIntakeRouting { Route = route, DepartmentId = DepartmentId });

			var twiml = Content(await Controller().InboundVoice(Call()));

			_spoken.Should().Equal(prompt);
			twiml.Should().Contain("<Hangup").And.NotContain("<Gather");
			_voice.Verify(v => v.StartAsync(It.IsAny<DepartmentPhoneNumber>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task the_callback_question_also_takes_digits()
		{
			var controller = Controller();
			controller.ControllerContext.HttpContext.Request.QueryString = new QueryString("?step=3&CallSid=CA1&To=%2B17755550100&From=%2B15305550199&SpeechResult=Pat%20Doe&Confidence=0.91");
			_voice.Setup(v => v.AnswerAsync(It.IsAny<VoiceIntakeAnswer>(), It.IsAny<CancellationToken>())).ReturnsAsync(new VoiceIntakeTurn
			{
				Step = VoiceIntakeSteps.Callback, Gather = true, AcceptDigits = true, Prompts = new List<string> { TwilioVoicePromptCatalog.VoiceIntakeAskCallback }
			});

			var twiml = Content(await controller.VoiceIntakeAnswer(3));

			twiml.Should().Contain("input=\"speech dtmf\"").And.Contain("finishOnKey=\"#\"").And.Contain("step=4");
			_voice.Verify(v => v.AnswerAsync(It.Is<VoiceIntakeAnswer>(a => a.ProviderCallId == "CA1" && a.Step == 3 && a.SpeechResult == "Pat Doe" && a.Confidence == 0.91),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task the_closing_turn_hangs_up()
		{
			var controller = Controller();
			controller.ControllerContext.HttpContext.Request.QueryString = new QueryString("?step=4&CallSid=CA1&To=%2B17755550100&From=%2B15305550199&Digits=*");
			_voice.Setup(v => v.AnswerAsync(It.IsAny<VoiceIntakeAnswer>(), It.IsAny<CancellationToken>())).ReturnsAsync(new VoiceIntakeTurn
			{
				Step = VoiceIntakeSteps.Completed, HangUp = true, CallId = 900, Prompts = new List<string> { TwilioVoicePromptCatalog.VoiceIntakeCompleted }
			});

			var twiml = Content(await controller.VoiceIntakeAnswer(4));

			twiml.Should().Contain("<Hangup").And.NotContain("<Gather");
			_spoken.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeCompleted);
		}

		[Test]
		public async Task the_member_ivr_actions_use_the_dedicated_numbers_department_for_members()
		{
			_departments.Setup(d => d.IsMemberOfDepartmentAsync(DepartmentId, "member-1")).ReturnsAsync(true);
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Springfield Fire" });
			_profiles.Setup(p => p.GetProfileByUserIdAsync("member-1", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "member-1", FirstName = "Pat" });

			await Controller().InboundVoiceAction("member-1", new Twilio.AspNet.Common.VoiceRequest { To = Dedicated, Digits = "0" });

			_departments.Verify(d => d.GetDepartmentByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
			_spoken.Should().Contain(TwilioVoicePromptCatalog.MainMenuGreeting("Pat", "Springfield Fire"));
		}

		#region SMS conversations (plan §8 D15)

		private const string Master = "+18885550000";

		private void OpenThread() => _thread = new IntakeConversation
		{
			IntakeConversationId = "conv-1", DepartmentId = DepartmentId, CallId = 900, ParticipantAddress = Caller, State = (int)IntakeConversationStates.Open
		};

		[Test]
		public async Task a_reply_to_the_master_number_continues_the_open_thread_ahead_of_member_commands()
		{
			OpenThread();

			var result = await Controller().IncomingMessage(Text(to: Master, body: "It's at the Shell station"));

			_conversations.Verify(c => c.HandleParticipantMessageAsync(_thread, "SM1", "It's at the Shell station", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			Content(result).Should().NotContain("<Message>");
			_profiles.Verify(p => p.GetProfileByMobileNumberAsync(It.IsAny<string>()), Times.Never, "member commands, text-to-call and the chatbot never see it");
			_legacyNumbers.Verify(n => n.SaveInboundMessageEventAsync(It.IsAny<InboundMessageEvent>(), It.IsAny<CancellationToken>()), Times.Never,
				"the texter's words live only in the call's notes");
			_received.Should().BeNull();
		}

		[Test]
		public async Task pin_and_communication_test_replies_are_answered_before_the_thread()
		{
			OpenThread();

			await Controller().IncomingMessage(Text(to: Master, body: "OPEN 123456"));
			await Controller().IncomingMessage(Text(to: Master, body: "CT-ABC123"));

			_communicationTests.Verify(c => c.RecordSmsResponseAsync("CT-ABC123", "15305550199"), Times.Once);
			_conversations.Verify(c => c.FindOpenForReplyAsync(It.IsAny<IntakeChannelTypes>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
			_conversations.Verify(c => c.HandleParticipantMessageAsync(It.IsAny<IntakeConversation>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task without_an_open_thread_the_master_number_is_unchanged()
		{
			await Controller().IncomingMessage(Text(to: Master, body: "STATUS"));

			_conversations.Verify(c => c.FindOpenForReplyAsync(IntakeChannelTypes.Sms, Caller, It.IsAny<DateTime>()), Times.Once);
			_conversations.Verify(c => c.HandleParticipantMessageAsync(It.IsAny<IntakeConversation>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_profiles.Verify(p => p.GetProfileByMobileNumberAsync("15305550199"), Times.AtLeastOnce);
		}

		[Test]
		public async Task stop_closes_the_thread_and_the_master_numbers_own_stop_handling_still_runs()
		{
			OpenThread();
			_commands.Setup(c => c.DetermineType("STOP")).Returns(new TextCommandPayload { Type = TextCommandTypes.Stop });

			await Controller().IncomingMessage(Text(to: Master, body: "STOP"));

			_conversations.Verify(c => c.HandleParticipantMessageAsync(_thread, "SM1", "STOP", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_profiles.Verify(p => p.GetProfileByMobileNumberAsync("15305550199"), Times.AtLeastOnce, "the profile opt-out path runs too");
		}

		[Test]
		public async Task a_dedicated_number_text_that_opened_a_conversation_gets_no_acknowledgement()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback((IntakeEnvelope e, CancellationToken _) => _received = e)
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.CallCreated, CallId = 900, IntakeConversationId = "conv-1" });

			await Controller().IncomingMessage(Text());

			_received.OpensConversation.Should().BeTrue();
			_text.Verify(t => t.SendTextMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Never,
				"the conversation's welcome (sent by the conversation service) is the only text");
		}

		[Test]
		public async Task stop_to_a_dedicated_number_closes_that_departments_open_thread()
		{
			OpenThread();
			_commands.Setup(c => c.DetermineType("STOP")).Returns(new TextCommandPayload { Type = TextCommandTypes.Stop });
			_conversationOperations.Setup(c => c.GetOpenForParticipantAsync(DepartmentId, IntakeChannelTypes.Sms, Caller)).ReturnsAsync(() => _thread);

			await Controller().IncomingMessage(Text(body: "STOP"));

			_conversations.Verify(c => c.HandleParticipantMessageAsync(_thread, "SM1", "STOP", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_received.Should().BeNull("STOP never creates a call");
		}

		#endregion SMS conversations (plan §8 D15)
	}
}
