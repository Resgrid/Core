using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Intake;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Enhanced AI voice intake (plan D2, D6): who reaches guided intake on a dedicated number, and the Nature → Location → Caller
	/// name → Callback step machine with re-asks, skips, abandonment and CallSid resume.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class VoiceIntakeServiceTests
	{
		private const int DepartmentId = 55;
		private const string Number = "+17755550100";
		private const string Caller = "+15305550199";

		private sealed class FixedClock : TimeProvider
		{
			public static readonly DateTime Now = new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now);
		}

		private Mock<IVoiceIntakeSessionsRepository> _sessions;
		private Mock<IIntakeChannelsRepository> _channels;
		private Mock<IIntakeService> _intake;
		private Mock<IEnhancedAiAccessService> _enhancedAi;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IUserProfileService> _profiles;
		private Mock<IDepartmentsService> _departments;
		private Mock<IIntakeBlockedSendersRepository> _blocked;
		private VoiceIntakeService _service;
		private DepartmentPhoneNumber _number;
		private VoiceIntakeSession _session;
		private IntakeEnvelope _received;
		private (bool Enabled, int Retries, int VoiceLimit, string SelfHosted) _saved;

		[SetUp]
		public void SetUp()
		{
			_saved = (IntakeConfig.Enabled, IntakeConfig.VoiceMaxRetries, DedicatedNumberAddonConfig.MonthlyVoiceIntakeLimit, AiConfig.SelfHostedDepartmentIds);
			IntakeConfig.Enabled = true;
			IntakeConfig.VoiceMaxRetries = 2;
			DedicatedNumberAddonConfig.MonthlyVoiceIntakeLimit = 100;
			AiConfig.SelfHostedDepartmentIds = "";
			_session = null;
			_received = null;

			_number = new DepartmentPhoneNumber
			{
				DepartmentPhoneNumberId = "n-1", DepartmentId = DepartmentId, PhoneNumber = Number, Status = (int)DepartmentPhoneNumberStatuses.Active,
				SmsCapable = true, VoiceCapable = true, SmsIntakeEnabled = true, VoiceIntakeEnabled = true
			};

			_sessions = new Mock<IVoiceIntakeSessionsRepository>();
			_sessions.Setup(s => s.GetByProviderCallIdAsync(It.IsAny<string>())).ReturnsAsync(() => _session);
			_sessions.Setup(s => s.TryInsertAsync(It.IsAny<VoiceIntakeSession>(), It.IsAny<CancellationToken>()))
				.Callback((VoiceIntakeSession s, CancellationToken _) => _session = s).ReturnsAsync(true);
			_sessions.Setup(s => s.UpdateProgressAsync(It.IsAny<VoiceIntakeSession>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_channels = new Mock<IIntakeChannelsRepository>();
			_channels.Setup(c => c.GetLiveByAddressAsync((int)IntakeChannelTypes.Voice, Number))
				.ReturnsAsync(new IntakeChannel { IntakeChannelId = "v-1", DepartmentId = DepartmentId, ChannelType = (int)IntakeChannelTypes.Voice, Address = Number, IsEnabled = true });

			_intake = new Mock<IIntakeService>();
			_intake.Setup(i => i.GetMonthlyCountAsync(DepartmentId, IntakeChannelTypes.Voice, It.IsAny<DateTime>())).ReturnsAsync(3);
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback((IntakeEnvelope e, CancellationToken _) => _received = e)
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.CallCreated, CallId = 900, DepartmentId = DepartmentId });

			_enhancedAi = new Mock<IEnhancedAiAccessService>();
			_enhancedAi.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiVoiceIntake)).ReturnsAsync(true);
			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			_profiles = new Mock<IUserProfileService>();
			_departments = new Mock<IDepartmentsService>();
			_blocked = new Mock<IIntakeBlockedSendersRepository>();

			_service = new VoiceIntakeService(_sessions.Object, _channels.Object, _intake.Object, new TwilioSpeechTranscriber(), _enhancedAi.Object, _flags.Object,
				_profiles.Object, _departments.Object, _blocked.Object, new FixedClock());
		}

		[TearDown]
		public void TearDown()
		{
			(IntakeConfig.Enabled, IntakeConfig.VoiceMaxRetries, DedicatedNumberAddonConfig.MonthlyVoiceIntakeLimit, AiConfig.SelfHostedDepartmentIds) = _saved;
		}

		private void MemberCalling(bool disabled = false, bool? verified = true)
		{
			_profiles.Setup(p => p.GetProfileByMobileNumberAsync("15305550199")).ReturnsAsync(new UserProfile { UserId = "member-1", MobileNumberVerified = verified });
			_departments.Setup(d => d.GetDepartmentMemberAsync("member-1", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "member-1", DepartmentId = DepartmentId, IsDisabled = disabled });
		}

		[TestCase(false)]
		[TestCase(null)]
		public async Task a_member_number_that_is_not_verified_is_a_caller_not_the_member_menu(bool? verified)
		{
			// The member menu acts as the member (status, staffing); caller id alone is not proof (sms-unverified routing policy).
			MemberCalling(verified: verified);

			var routing = await _service.RouteAsync(_number, Caller, FixedClock.Now);

			routing.Route.Should().Be(VoiceIntakeRoutes.Intake);
			routing.MemberUserId.Should().BeNull();
		}

		[Test]
		public async Task a_verified_home_number_reaches_the_member_menu_when_the_mobile_match_is_unverified()
		{
			MemberCalling(verified: false);
			_profiles.Setup(p => p.GetProfileByHomeNumberAsync("15305550199")).ReturnsAsync(new UserProfile { UserId = "member-1", HomeNumberVerified = true });

			var routing = await _service.RouteAsync(_number, Caller, FixedClock.Now);

			routing.Route.Should().Be(VoiceIntakeRoutes.MemberMenu);
			routing.MemberUserId.Should().Be("member-1");
		}

		[Test]
		public async Task a_lapsed_number_is_inactive_for_members_too()
		{
			MemberCalling();
			_number.Status = (int)DepartmentPhoneNumberStatuses.Lapsed;

			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.NumberInactive);
		}

		[Test]
		public async Task an_answer_that_lost_to_an_overlapping_callback_resumes_the_stored_session()
		{
			await _service.StartAsync(_number, "CA1", Caller);
			var stored = new VoiceIntakeSession
			{
				VoiceIntakeSessionId = _session.VoiceIntakeSessionId, DepartmentId = DepartmentId, ProviderCallId = "CA1", FromNumber = Caller, ToNumber = Number,
				Step = (int)VoiceIntakeSteps.Completed, CallId = 900, CompletedOn = FixedClock.Now
			};
			_sessions.SetupSequence(s => s.GetByProviderCallIdAsync("CA1"))
				.ReturnsAsync(new VoiceIntakeSession
				{
					VoiceIntakeSessionId = _session.VoiceIntakeSessionId, DepartmentId = DepartmentId, ProviderCallId = "CA1", FromNumber = Caller, ToNumber = Number,
					Step = (int)VoiceIntakeSteps.Nature, AnswersJson = "{}"
				})
				.ReturnsAsync(stored);
			_sessions.Setup(s => s.UpdateProgressAsync(It.IsAny<VoiceIntakeSession>(), (int)VoiceIntakeSteps.Nature, 0, It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var turn = await Answer(VoiceIntakeSteps.Nature, "Kitchen fire");

			turn.HangUp.Should().BeTrue("the other callback already completed the call");
			turn.Step.Should().Be(VoiceIntakeSteps.Completed);
			turn.CallId.Should().Be(900);
			_received.Should().BeNull("this callback created nothing");
		}

		[Test]
		public async Task a_member_gets_the_member_menu_in_the_numbers_department()
		{
			MemberCalling();

			var routing = await _service.RouteAsync(_number, Caller, FixedClock.Now);

			routing.Route.Should().Be(VoiceIntakeRoutes.MemberMenu);
			routing.MemberUserId.Should().Be("member-1");
			routing.DepartmentId.Should().Be(DepartmentId);
		}

		[Test]
		public async Task a_blocked_caller_is_told_the_line_cannot_take_the_call()
		{
			_blocked.Setup(b => b.GetInForceAsync(DepartmentId, Caller, FixedClock.Now))
				.ReturnsAsync(new IntakeBlockedSender { DepartmentId = DepartmentId, Sender = Caller, BlockedOn = FixedClock.Now.AddDays(-1) });

			var routing = await _service.RouteAsync(_number, Caller, FixedClock.Now);

			routing.Route.Should().Be(VoiceIntakeRoutes.Blocked);
			routing.Reason.Should().Be("SenderBlocked");
			_intake.Verify(i => i.GetMonthlyCountAsync(It.IsAny<int>(), It.IsAny<IntakeChannelTypes>(), It.IsAny<DateTime>()), Times.Never);
		}

		[Test]
		public async Task a_member_is_never_held_by_the_blocked_list()
		{
			MemberCalling();
			_blocked.Setup(b => b.GetInForceAsync(DepartmentId, Caller, It.IsAny<DateTime>())).ReturnsAsync(new IntakeBlockedSender { Sender = Caller });

			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.MemberMenu);
		}

		[Test]
		public async Task a_blocked_list_that_cannot_be_read_lets_the_caller_through()
		{
			_blocked.Setup(b => b.GetInForceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("db down"));

			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.Intake);
		}

		[Test]
		public async Task a_withheld_caller_id_is_never_looked_up_on_the_blocked_list()
		{
			(await _service.RouteAsync(_number, "+266696687", FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.Intake);
			_blocked.Verify(b => b.GetInForceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
		}

		[Test]
		public async Task a_disabled_member_is_treated_as_a_caller()
		{
			MemberCalling(disabled: true);

			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.Intake);
		}

		[Test]
		public async Task a_non_member_reaches_guided_intake_when_everything_allows_it()
		{
			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.Intake);
		}

		[Test]
		public async Task without_enhanced_ai_voice_intake_the_line_asks_for_a_text()
		{
			_enhancedAi.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiVoiceIntake)).ReturnsAsync(false);

			var routing = await _service.RouteAsync(_number, Caller, FixedClock.Now);

			routing.Route.Should().Be(VoiceIntakeRoutes.TextOnly);
			routing.Reason.Should().Be("NotEntitled");
		}

		[Test]
		public async Task a_self_hosted_department_uses_the_module_switch_and_the_flag()
		{
			AiConfig.SelfHostedDepartmentIds = DepartmentId.ToString();
			_enhancedAi.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiVoiceIntake)).ReturnsAsync(false);
			_enhancedAi.Setup(a => a.IsEnabledAsync(DepartmentId)).ReturnsAsync(true);
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AiVoiceIntake, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });

			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.Intake);
		}

		[Test]
		public async Task over_the_monthly_voice_limit_the_line_asks_for_a_text()
		{
			_intake.Setup(i => i.GetMonthlyCountAsync(DepartmentId, IntakeChannelTypes.Voice, It.IsAny<DateTime>())).ReturnsAsync(100);

			var routing = await _service.RouteAsync(_number, Caller, FixedClock.Now);

			routing.Route.Should().Be(VoiceIntakeRoutes.TextOnly);
			routing.Reason.Should().Be("OverLimit");
		}

		[Test]
		public async Task voice_intake_off_on_the_number_asks_for_a_text()
		{
			_number.VoiceIntakeEnabled = false;

			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.TextOnly);
		}

		[Test]
		public async Task the_intake_flag_off_asks_for_a_text()
		{
			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.TextOnly);
		}

		[Test]
		public async Task a_lapsed_number_is_inactive_for_callers()
		{
			_number.Status = (int)DepartmentPhoneNumberStatuses.Lapsed;

			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).Route.Should().Be(VoiceIntakeRoutes.NumberInactive);
		}

		[Test]
		public async Task start_greets_with_the_emergency_disclaimer_and_asks_the_nature()
		{
			var turn = await _service.StartAsync(_number, "CA1", Caller);

			turn.Gather.Should().BeTrue();
			turn.Step.Should().Be(VoiceIntakeSteps.Nature);
			turn.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeGreeting, TwilioVoicePromptCatalog.VoiceIntakeAskNature);
			TwilioVoicePromptCatalog.VoiceIntakeGreeting.Should().Contain("life-threatening emergency, hang up and dial 911");
			_session.ProviderCallId.Should().Be("CA1");
			_session.DepartmentId.Should().Be(DepartmentId);
			_session.FromNumber.Should().Be(Caller);
		}

		[Test]
		public async Task a_retried_start_resumes_the_same_session()
		{
			await _service.StartAsync(_number, "CA1", Caller);
			_session.Step = (int)VoiceIntakeSteps.CallerName;

			var turn = await _service.StartAsync(_number, "CA1", Caller);

			turn.Step.Should().Be(VoiceIntakeSteps.CallerName);
			turn.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeAskCallerName);
			_sessions.Verify(s => s.TryInsertAsync(It.IsAny<VoiceIntakeSession>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		private Task<VoiceIntakeTurn> Answer(VoiceIntakeSteps step, string speech, string digits = null) =>
			_service.AnswerAsync(new VoiceIntakeAnswer { ProviderCallId = "CA1", FromNumber = Caller, ToNumber = Number, Step = (int)step, SpeechResult = speech, Digits = digits });

		[Test]
		public async Task the_questions_run_in_order_and_create_the_pending_call()
		{
			await _service.StartAsync(_number, "CA1", Caller);

			(await Answer(VoiceIntakeSteps.Nature, "my neighbor fell and can't get up")).Step.Should().Be(VoiceIntakeSteps.Location);
			(await Answer(VoiceIntakeSteps.Location, "12 Oak Street in Springfield")).Step.Should().Be(VoiceIntakeSteps.CallerName);
			(await Answer(VoiceIntakeSteps.CallerName, "Pat Doe")).Step.Should().Be(VoiceIntakeSteps.Callback);
			var callback = await Answer(VoiceIntakeSteps.Callback, null, "*");

			callback.HangUp.Should().BeTrue();
			callback.CallId.Should().Be(900);
			callback.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeCompleted);
			_received.ChannelType.Should().Be(IntakeChannelTypes.Voice);
			_received.ChannelAddress.Should().Be(Number);
			_received.ProviderMessageId.Should().Be("CA1");
			_received.Answers["Callback"].Should().Be(Caller, "star uses the caller id");
			_received.Body.Should().Be("Nature: my neighbor fell and can't get up\nLocation: 12 Oak Street in Springfield\nCaller name: Pat Doe\nCallback number: " + Caller);
			_session.Step.Should().Be((int)VoiceIntakeSteps.Completed);
			_session.CallId.Should().Be(900);
			_session.AnswersJson.Should().BeNull("the transcript lives on the call once it exists");
		}

		[Test]
		public async Task nothing_heard_is_asked_again_then_a_later_question_is_skipped()
		{
			await _service.StartAsync(_number, "CA1", Caller);
			await Answer(VoiceIntakeSteps.Nature, "lift assist");

			var first = await Answer(VoiceIntakeSteps.Location, "");
			first.Step.Should().Be(VoiceIntakeSteps.Location);
			first.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeNotHeard, TwilioVoicePromptCatalog.VoiceIntakeAskLocation);
			(await Answer(VoiceIntakeSteps.Location, "   ")).Step.Should().Be(VoiceIntakeSteps.Location);

			var skipped = await Answer(VoiceIntakeSteps.Location, null);
			skipped.Step.Should().Be(VoiceIntakeSteps.CallerName, "after VoiceMaxRetries re-asks a later question is skipped");
			_session.Retries.Should().Be(0);
			JsonConvert.DeserializeObject<Dictionary<string, string>>(_session.AnswersJson).Should().NotContainKey("Location");
		}

		[Test]
		public async Task no_nature_after_the_retries_abandons_the_call_without_a_pending_call()
		{
			await _service.StartAsync(_number, "CA1", Caller);

			await Answer(VoiceIntakeSteps.Nature, null);
			await Answer(VoiceIntakeSteps.Nature, null);
			var last = await Answer(VoiceIntakeSteps.Nature, null);

			last.HangUp.Should().BeTrue();
			last.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeNothingHeard);
			_session.Step.Should().Be((int)VoiceIntakeSteps.Abandoned);
			_intake.Verify(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_stale_answer_for_another_question_re_asks_the_current_one_unchanged()
		{
			await _service.StartAsync(_number, "CA1", Caller);
			await Answer(VoiceIntakeSteps.Nature, "smoke in the hallway");

			var stale = await Answer(VoiceIntakeSteps.Nature, "smoke in the hallway");

			stale.Step.Should().Be(VoiceIntakeSteps.Location);
			stale.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeAskLocation);
			JsonConvert.DeserializeObject<Dictionary<string, string>>(_session.AnswersJson)["Nature"].Should().Be("smoke in the hallway");
		}

		[Test]
		public async Task a_completed_session_answers_retries_with_the_closing_prompt()
		{
			_session = new VoiceIntakeSession { ProviderCallId = "CA1", DepartmentId = DepartmentId, Step = (int)VoiceIntakeSteps.Completed, CallId = 900 };

			var turn = await Answer(VoiceIntakeSteps.Callback, "anything");

			turn.HangUp.Should().BeTrue();
			turn.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeCompleted);
			_intake.Verify(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task the_callback_takes_keyed_digits_or_same_number()
		{
			await _service.StartAsync(_number, "CA1", Caller);
			await Answer(VoiceIntakeSteps.Nature, "water leak");
			await Answer(VoiceIntakeSteps.Location, "1 Main");
			await Answer(VoiceIntakeSteps.CallerName, "Sam");
			await Answer(VoiceIntakeSteps.Callback, null, "5305550123#");
			_received.Answers["Callback"].Should().Be("5305550123");

			_session = null;
			await _service.StartAsync(_number, "CA1", Caller);
			await Answer(VoiceIntakeSteps.Nature, "water leak");
			await Answer(VoiceIntakeSteps.Location, "1 Main");
			await Answer(VoiceIntakeSteps.CallerName, "Sam");
			await Answer(VoiceIntakeSteps.Callback, "same number");
			_received.Answers["Callback"].Should().Be(Caller);
		}

		[Test]
		public async Task a_caller_past_the_hourly_limit_whose_answers_joined_their_open_call_hears_it_was_recorded()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.AddedToCall, CallId = 880, Reason = "SenderLimit" });
			await _service.StartAsync(_number, "CA1", Caller);
			await Answer(VoiceIntakeSteps.Nature, "same fire as before");
			await Answer(VoiceIntakeSteps.Location, "school");
			await Answer(VoiceIntakeSteps.CallerName, "Lee");

			var turn = await Answer(VoiceIntakeSteps.Callback, null, "*");

			turn.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeCompleted);
			turn.CallId.Should().Be(880);
			_session.Step.Should().Be((int)VoiceIntakeSteps.Completed);
		}

		[Test]
		public async Task an_intake_refusal_ends_the_call_with_the_failure_prompt_and_keeps_the_answers()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Failed });
			await _service.StartAsync(_number, "CA1", Caller);
			await Answer(VoiceIntakeSteps.Nature, "fire alarm");
			await Answer(VoiceIntakeSteps.Location, "school");
			await Answer(VoiceIntakeSteps.CallerName, "Lee");

			var turn = await Answer(VoiceIntakeSteps.Callback, null, "*");

			turn.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeFailed);
			_session.Step.Should().Be((int)VoiceIntakeSteps.Abandoned);
			_session.AnswersJson.Should().Contain("fire alarm");
		}

		[Test]
		public async Task an_overlapping_final_callback_whose_intake_is_still_running_never_abandons_the_session()
		{
			// Two final callbacks for the same call (a Twilio retry): the first owns the intake and is still saving the call when the second
			// asks. The second writes nothing; the caller is asked the callback number again, and that answer gets the call the first made.
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Duplicate, Reason = IntakeResultReasons.DuplicateInProgress });
			await _service.StartAsync(_number, "CA1", Caller);
			await Answer(VoiceIntakeSteps.Nature, "fire alarm");
			await Answer(VoiceIntakeSteps.Location, "school");
			await Answer(VoiceIntakeSteps.CallerName, "Lee");

			var turn = await Answer(VoiceIntakeSteps.Callback, null, "*");

			turn.Gather.Should().BeTrue();
			turn.Step.Should().Be(VoiceIntakeSteps.Callback);
			_session.Step.Should().Be((int)VoiceIntakeSteps.Callback, "only the callback that owns the intake records how it ended");
			_session.CompletedOn.Should().BeNull();
			_sessions.Verify(s => s.UpdateProgressAsync(It.Is<VoiceIntakeSession>(v => v.Step == (int)VoiceIntakeSteps.Abandoned), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>()), Times.Never);

			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Duplicate, Reason = IntakeResultReasons.Duplicate, CallId = 900 });
			var again = await Answer(VoiceIntakeSteps.Callback, null, "*");

			again.Prompts.Should().Equal(TwilioVoicePromptCatalog.VoiceIntakeCompleted);
			again.CallId.Should().Be(900);
			_session.Step.Should().Be((int)VoiceIntakeSteps.Completed);
		}

		[Test]
		public async Task a_verified_home_number_of_a_member_wins_over_a_verified_mobile_of_someone_outside_the_department()
		{
			// One number on two profiles: a verified mobile on a user of another department, a verified home number on this department's member.
			_profiles.Setup(p => p.GetProfileByMobileNumberAsync("15305550199")).ReturnsAsync(new UserProfile { UserId = "outsider", MobileNumberVerified = true });
			_profiles.Setup(p => p.GetProfileByHomeNumberAsync("15305550199")).ReturnsAsync(new UserProfile { UserId = "member-1", HomeNumberVerified = true });
			_departments.Setup(d => d.GetDepartmentMemberAsync("member-1", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "member-1", DepartmentId = DepartmentId });

			var routing = await _service.RouteAsync(_number, Caller, FixedClock.Now);

			routing.Route.Should().Be(VoiceIntakeRoutes.MemberMenu);
			routing.MemberUserId.Should().Be("member-1");

			// Both verified members of the department: the mobile match comes first.
			_departments.Setup(d => d.GetDepartmentMemberAsync("outsider", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "outsider", DepartmentId = DepartmentId });
			(await _service.RouteAsync(_number, Caller, FixedClock.Now)).MemberUserId.Should().Be("outsider");
		}

		[Test]
		public void voice_intake_prompts_are_pre_warmed_and_read_as_sentences()
		{
			TwilioVoicePromptCatalog.GetStaticPrompts().Should().Contain(new[]
			{
				TwilioVoicePromptCatalog.VoiceIntakeGreeting, TwilioVoicePromptCatalog.VoiceIntakeAskNature, TwilioVoicePromptCatalog.VoiceIntakeAskLocation,
				TwilioVoicePromptCatalog.VoiceIntakeAskCallerName, TwilioVoicePromptCatalog.VoiceIntakeAskCallback, TwilioVoicePromptCatalog.VoiceIntakeTextOnly
			});
			TwilioVoicePromptCatalog.VoiceIntakeTextOnly.Should().Contain("Please text this number").And.Contain("dial 911");
		}
	}
}
