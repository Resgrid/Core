using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Intake;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Intake SMS conversations (plan §8 D15–D17): a dedicated-number text opens a thread answered from the master number with the
	/// hand-off welcome, the texter's replies are notes on the call (idempotent per Twilio id), STOP opts out and closes, a dispatcher
	/// connecting is introduced by first name, only the connected dispatcher texts unless they take over, release hands the thread back
	/// to Enhanced AI or nobody, close optionally tells the texter, worker 80 closes idle and ended threads, and the desk's routers hear
	/// about every change once (a router that throws never fails the action). No real SMS: the sender is a mock.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class IntakeConversationServiceTests
	{
		private const int DepartmentId = 55;
		private const int CallId = 900;
		private const string Dedicated = "+17755550100";
		private const string Master = "+18885550123";
		private const string Texter = "+15305550199";

		private FakeIntakeConversationsRepository _conversations;
		private FakeIntakeConversationMessagesRepository _messages;
		private Mock<IIntakeChannelsRepository> _channels;
		private Mock<ICallsService> _calls;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IIntakeSmsProvider> _sms;
		private Mock<IIntakeConversationAssistant> _assistant;
		private Mock<IProtectedReadService> _protectedRead;
		private Mock<IQueueService> _queue;
		private Mock<IEventAggregator> _events;
		private RecordingRouter _router;
		private SteppingClock _clock;
		private IntakeChannel _channel;
		private Call _call;
		private readonly List<CallNote> _notes = new List<CallNote>();
		private readonly List<(string To, string Text)> _sent = new List<(string, string)>();
		private readonly List<AiDispatchQueueItem> _queued = new List<AiDispatchQueueItem>();
		private readonly List<IntakeConversationUpdatedEvent> _published = new List<IntakeConversationUpdatedEvent>();
		private bool _sendFails;
		private IntakeConversationService _service;
		private (bool, bool, string, int, int, int, int, string, bool) _savedConfig;

		[SetUp]
		public void SetUp()
		{
			_savedConfig = (IntakeConfig.Enabled, IntakeConfig.ConversationsEnabled, IntakeConfig.EmergencyNumber, IntakeConfig.ConversationReplyWindowHours,
				IntakeConfig.ConversationIdleCloseMinutes, IntakeConfig.ConversationMaxAssistantTurns, IntakeConfig.ConversationMaxOutboundLength,
				NumberProviderConfig.TwilioResgridNumber, AiDispatchConfig.EnrichEnabled);
			IntakeConfig.Enabled = true;
			IntakeConfig.ConversationsEnabled = true;
			IntakeConfig.EmergencyNumber = "911";
			IntakeConfig.ConversationReplyWindowHours = 24;
			IntakeConfig.ConversationIdleCloseMinutes = 240;
			IntakeConfig.ConversationMaxAssistantTurns = 8;
			IntakeConfig.ConversationMaxOutboundLength = 640;
			NumberProviderConfig.TwilioResgridNumber = Master;
			AiDispatchConfig.EnrichEnabled = true;
			_notes.Clear();
			_sent.Clear();
			_queued.Clear();
			_published.Clear();
			_sendFails = false;

			_conversations = new FakeIntakeConversationsRepository();
			_messages = new FakeIntakeConversationMessagesRepository();
			_channel = new IntakeChannel
			{
				IntakeChannelId = "ch-sms", DepartmentId = DepartmentId, Name = "Dedicated number SMS " + Dedicated, ChannelType = (int)IntakeChannelTypes.Sms,
				Address = Dedicated, ProcessingMode = (int)IntakeProcessingModes.Template, IsEnabled = true
			};
			_channels = new Mock<IIntakeChannelsRepository>();
			_channels.Setup(c => c.GetByIdAsync("ch-sms")).ReturnsAsync(() => _channel);

			_call = new Call { CallId = CallId, DepartmentId = DepartmentId, Number = "26-900", Name = "Dedicated number SMS " + Dedicated + ": " + Texter, State = (int)CallStates.Pending };
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool _) =>
				id == CallId ? _call : new Call { CallId = id, DepartmentId = DepartmentId, Number = "26-" + id, State = (int)CallStates.Pending });
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallNote n, CancellationToken _) => { n.CallNoteId = _notes.Count + 1; _notes.Add(n); return n; });
			_calls.Setup(c => c.PopulateCallData(It.IsAny<Call>(), false, false, true, false, false, false, false, false, false, false))
				.ReturnsAsync((Call c, bool a, bool b, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k) =>
				{
					c.CallNotes = _notes.Where(n => n.CallId == c.CallId).Select(n => new CallNote { CallNoteId = n.CallNoteId, CallId = n.CallId, Note = n.Note, Source = n.Source, UserId = n.UserId }).ToList();
					return c;
				});

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Springfield Fire", ManagingUserId = "owner" });
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync("disp-1", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "disp-1", FirstName = "Jane", LastName = "Doe" });
			_profiles.Setup(p => p.GetProfileByUserIdAsync("disp-2", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "disp-2", FirstName = "Sam", LastName = "Ray" });

			_sms = new Mock<IIntakeSmsProvider>();
			_sms.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string to, string text, int _, int __, CancellationToken ___) =>
				{
					_sent.Add((to, text));
					return _sendFails ? new IntakeSmsSendResult { Error = "Twilio 21610: unsubscribed" } : new IntakeSmsSendResult { Success = true, ProviderMessageId = "SMout" + _sent.Count };
				});

			_assistant = new Mock<IIntakeConversationAssistant>();
			_assistant.Setup(a => a.CheckAsync(DepartmentId)).ReturnsAsync((string)null);
			_protectedRead = new Mock<IProtectedReadService>();
			_protectedRead.Setup(p => p.ResolveNotesForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<CallNote>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());
			_queue = new Mock<IQueueService>();
			_queue.Setup(q => q.EnqueueAiDispatchTriageAsync(It.IsAny<AiDispatchQueueItem>(), It.IsAny<CancellationToken>()))
				.Callback((AiDispatchQueueItem i, CancellationToken _) => _queued.Add(i)).ReturnsAsync(true);
			_events = new Mock<IEventAggregator>();
			_events.Setup(e => e.SendMessageAsync(It.IsAny<IntakeConversationUpdatedEvent>()))
				.Callback((IntakeConversationUpdatedEvent e) => _published.Add(e)).Returns(Task.CompletedTask);
			_router = new RecordingRouter();
			_clock = new SteppingClock();

			_service = NewService(_router);
		}

		[TearDown]
		public void TearDown()
		{
			(IntakeConfig.Enabled, IntakeConfig.ConversationsEnabled, IntakeConfig.EmergencyNumber, IntakeConfig.ConversationReplyWindowHours,
				IntakeConfig.ConversationIdleCloseMinutes, IntakeConfig.ConversationMaxAssistantTurns, IntakeConfig.ConversationMaxOutboundLength,
				NumberProviderConfig.TwilioResgridNumber, AiDispatchConfig.EnrichEnabled) = _savedConfig;
		}

		private IntakeConversationService NewService(params IIntakeConversationRouter[] routers) =>
			new IntakeConversationService(_conversations, _messages, _channels.Object, _calls.Object, _departments.Object, _profiles.Object, _sms.Object,
				_assistant.Object, _protectedRead.Object, _queue.Object, _events.Object, new Lazy<IEnumerable<IIntakeConversationRouter>>(() => routers), _clock);

		private static IntakeEnvelope Envelope(string body = "Car crash on Route 9, two cars", string sid = "SM1") => new IntakeEnvelope
		{
			ChannelType = IntakeChannelTypes.Sms, ChannelAddress = Dedicated, Sender = Texter, Body = body, ProviderMessageId = sid,
			ReceivedOnUtc = DateTime.UtcNow, OpensConversation = true
		};

		private async Task<IntakeConversation> StartAsync(IntakeProcessingModes mode = IntakeProcessingModes.Template)
		{
			_channel.ProcessingMode = (int)mode;
			var conversation = await _service.StartFromIntakeAsync(_channel, Envelope(), _call);
			conversation.Should().NotBeNull();
			return conversation;
		}

		private IntakeConversation Stored(string id) => _conversations.Rows.Single(r => r.IntakeConversationId == id);

		private List<IntakeConversationMessage> Messages(string id) => _messages.Rows.Where(m => m.IntakeConversationId == id).OrderBy(m => m.CreatedOn).ToList();

		#region Start

		[Test]
		public async Task a_dedicated_number_text_opens_a_thread_and_the_welcome_goes_from_the_master_number()
		{
			var conversation = await StartAsync();

			var row = Stored(conversation.IntakeConversationId);
			row.CallId.Should().Be(CallId);
			row.ParticipantAddress.Should().Be(Texter);
			row.InboundAddress.Should().Be(Dedicated);
			row.ReplyFromAddress.Should().Be(Master);
			row.Handler.Should().Be((int)IntakeConversationHandlers.Unassisted, "a template channel has no automated turns");

			_sent.Should().ContainSingle();
			_sent[0].To.Should().Be(Texter);
			_sent[0].Text.Should().Be("Springfield Fire: You texted the dispatch number for Springfield Fire. We'll continue on this number — reply here. " +
				"If this is a life-threatening emergency, call 911. A dispatcher will reply here.");

			var messages = Messages(conversation.IntakeConversationId);
			messages.Should().HaveCount(2);
			messages[0].Direction.Should().Be((int)IntakeConversationMessageDirections.Inbound);
			messages[0].ProviderMessageId.Should().Be("SM1");
			messages[0].Status.Should().Be((int)IntakeConversationMessageStatuses.Received);
			messages[1].Author.Should().Be((int)IntakeConversationAuthors.System);
			messages[1].Status.Should().Be((int)IntakeConversationMessageStatuses.Sent);
			messages[1].ProviderMessageId.Should().Be("SMout1");

			_notes.Should().HaveCount(2);
			_notes[0].Note.Should().Be("Text from caller: Car crash on Route 9, two cars");
			_notes[0].Source.Should().Be((int)CallNoteSources.IntakeText, "the texter's information triggers the AI destination review like a person's note");
			_notes[1].Note.Should().StartWith("Text to caller (Resgrid): Springfield Fire: You texted");
			_notes[1].Source.Should().Be((int)CallNoteSources.System);
			messages[0].CallNoteId.Should().Be(1);
			messages[1].CallNoteId.Should().Be(2);

			_router.Calls.Should().Equal("Route:" + conversation.IntakeConversationId);
			_published.Should().NotBeEmpty();
			_published.Last().CallId.Should().Be(CallId);
			_published.Last().Payload.Should().NotContain(Texter).And.NotContain("Car crash");
			_queued.Should().BeEmpty("nobody automated answers a template channel");
		}

		[Test]
		public async Task with_enhanced_ai_the_welcome_has_no_dispatcher_line_and_the_first_turn_is_queued()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);

			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Assistant);
			_sent.Single().Text.Should().NotContain("A dispatcher will reply here.");
			var turn = _queued.Single();
			turn.Channel.Should().Be(AiDispatchChannels.IntakeConversation);
			turn.IntakeConversationId.Should().Be(conversation.IntakeConversationId);
			turn.IntakeConversationMessageId.Should().Be(Messages(conversation.IntakeConversationId)[0].IntakeConversationMessageId);
		}

		[Test]
		public async Task an_enhanced_ai_channel_without_the_entitlement_is_unassisted()
		{
			_assistant.Setup(a => a.CheckAsync(DepartmentId)).ReturnsAsync(IntakeAssistantOutcomes.ProtectionUnsupported);

			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);

			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Unassisted);
			_sent.Single().Text.Should().EndWith("A dispatcher will reply here.");
			_queued.Should().BeEmpty();
		}

		[Test]
		public async Task without_the_master_number_or_with_conversations_off_nothing_opens()
		{
			NumberProviderConfig.TwilioResgridNumber = "";
			(await _service.StartFromIntakeAsync(_channel, Envelope(), _call)).Should().BeNull();

			NumberProviderConfig.TwilioResgridNumber = Master;
			IntakeConfig.ConversationsEnabled = false;
			(await _service.StartFromIntakeAsync(_channel, Envelope(), _call)).Should().BeNull();

			_conversations.Rows.Should().BeEmpty();
			_sent.Should().BeEmpty();
		}

		[Test]
		public async Task a_racing_second_text_joins_the_open_thread_and_its_extra_call_points_at_it()
		{
			var first = await StartAsync();
			var extraCall = new Call { CallId = 901, DepartmentId = DepartmentId, Number = "26-901", State = (int)CallStates.Pending };

			var joined = await _service.StartFromIntakeAsync(_channel, Envelope("Also a fuel leak", "SM2"), extraCall);

			joined.IntakeConversationId.Should().Be(first.IntakeConversationId);
			_conversations.Rows.Should().ContainSingle();
			Messages(first.IntakeConversationId).Count(m => m.Direction == (int)IntakeConversationMessageDirections.Inbound).Should().Be(2);
			_notes.Should().Contain(n => n.CallId == CallId && n.Note == "Text from caller: Also a fuel leak");
			_notes.Should().Contain(n => n.CallId == 901 && n.Note.Contains("26-900"));
			_sent.Should().ContainSingle("only the first text gets the welcome");
		}

		#endregion Start

		#region Participant messages

		[Test]
		public async Task a_reply_is_noted_once_per_twilio_id_and_the_routers_hear_it()
		{
			var conversation = await StartAsync();
			_router.Calls.Clear();

			(await _service.HandleParticipantMessageAsync(conversation, "SM2", "It's at the Shell station", DateTime.UtcNow)).Should().BeTrue();
			(await _service.HandleParticipantMessageAsync(conversation, "SM2", "It's at the Shell station", DateTime.UtcNow)).Should().BeTrue();

			_notes.Count(n => n.Note == "Text from caller: It's at the Shell station").Should().Be(1);
			Messages(conversation.IntakeConversationId).Count(m => m.ProviderMessageId == "SM2").Should().Be(1);
			_router.Calls.Should().ContainSingle(c => c.StartsWith("Message:"));
		}

		[Test]
		public async Task a_reply_to_an_ai_thread_queues_the_next_turn()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			_queued.Clear();

			await _service.HandleParticipantMessageAsync(conversation, "SM2", "Main and 5th", DateTime.UtcNow);

			_queued.Single().Channel.Should().Be(AiDispatchChannels.IntakeConversation);
		}

		[TestCase("STOP")]
		[TestCase(" unsubscribe ")]
		public async Task stop_opts_out_closes_and_sends_nothing(string word)
		{
			var conversation = await StartAsync();
			_sent.Clear();
			_router.Calls.Clear();

			await _service.HandleParticipantMessageAsync(conversation, "SM2", word, DateTime.UtcNow);

			var row = Stored(conversation.IntakeConversationId);
			row.IsOpen.Should().BeFalse();
			row.OptedOut.Should().BeTrue();
			row.CloseReason.Should().Be("OptedOut");
			_sent.Should().BeEmpty();
			_router.Calls.Should().Equal("Closed:OptedOut");
			(await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1")).Should().Be(IntakeConversationActionResults.Closed);
		}

		[TestCase("CANCEL")]
		[TestCase("END")]
		[TestCase("QUIT")]
		[TestCase("stop the car first")]
		public async Task other_words_never_opt_out(string text)
		{
			var conversation = await StartAsync();

			await _service.HandleParticipantMessageAsync(conversation, "SM2", text, DateTime.UtcNow);

			Stored(conversation.IntakeConversationId).IsOpen.Should().BeTrue();
			Stored(conversation.IntakeConversationId).OptedOut.Should().BeFalse();
		}

		[Test]
		public async Task replies_reach_the_most_recent_open_thread_within_the_window()
		{
			var conversation = await StartAsync();

			(await _service.FindOpenForReplyAsync(IntakeChannelTypes.Sms, "15305550199", _clock.Now)).IntakeConversationId.Should().Be(conversation.IntakeConversationId);
			(await _service.FindOpenForReplyAsync(IntakeChannelTypes.Sms, Texter, _clock.Now.AddHours(25))).Should().BeNull();
			(await _service.FindOpenForReplyAsync(IntakeChannelTypes.Sms, "+15305550100", _clock.Now)).Should().BeNull();

			IntakeConfig.ConversationsEnabled = false;
			(await _service.FindOpenForReplyAsync(IntakeChannelTypes.Sms, Texter, _clock.Now)).Should().BeNull();
		}

		#endregion Participant messages

		#region Dispatcher actions

		[Test]
		public async Task connecting_introduces_the_dispatcher_by_first_name_once()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			_sent.Clear();
			_router.Calls.Clear();

			(await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1")).Should().Be(IntakeConversationActionResults.Done);
			(await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1")).Should().Be(IntakeConversationActionResults.Done);

			_sent.Should().ContainSingle().Which.Text.Should().Be("You are now connected with Jane. Please describe the situation you are having and your location.");
			var row = Stored(conversation.IntakeConversationId);
			row.Handler.Should().Be((int)IntakeConversationHandlers.Dispatcher);
			row.AssignedUserId.Should().Be("disp-1");
			_router.Calls.Should().Equal("Connected:disp-1");
		}

		[Test]
		public async Task a_dispatcher_without_a_first_name_is_a_dispatcher()
		{
			var conversation = await StartAsync();
			_sent.Clear();

			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "no-profile");

			_sent.Single().Text.Should().StartWith("You are now connected with a dispatcher.");
		}

		[Test]
		public async Task only_the_connected_dispatcher_texts_unless_another_takes_over()
		{
			var conversation = await StartAsync();
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			_sent.Clear();
			_router.Calls.Clear();

			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-2", "Hello")).Should().Be(IntakeConversationActionResults.HeldByAnotherDispatcher);
			(await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-2")).Should().Be(IntakeConversationActionResults.HeldByAnotherDispatcher);
			_sent.Should().BeEmpty();
			_router.Calls.Should().BeEmpty();

			(await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-2", takeOver: true)).Should().Be(IntakeConversationActionResults.Done);
			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-2", "Is anyone hurt?")).Should().Be(IntakeConversationActionResults.Done);

			_sent.Select(s => s.Text).Should().Equal("You are now connected with Sam. Please describe the situation you are having and your location.", "Is anyone hurt?");
			_router.Calls.Should().Equal("Connected:disp-2");
			_notes.Last().Note.Should().Be("Text to caller (Sam): Is anyone hurt?");
			_notes.Last().UserId.Should().Be("disp-2");
			Messages(conversation.IntakeConversationId).Last().Author.Should().Be((int)IntakeConversationAuthors.Dispatcher);
		}

		[Test]
		public async Task sending_with_nobody_connected_connects_first()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			_sent.Clear();
			_router.Calls.Clear();

			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "What is the address?")).Should().Be(IntakeConversationActionResults.Done);

			_sent.Select(s => s.Text).Should().HaveCount(2);
			_sent[0].Text.Should().StartWith("You are now connected with Jane.");
			_sent[1].Text.Should().Be("What is the address?");
			_router.Calls.Should().Equal("Connected:disp-1");
			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Dispatcher);
		}

		[Test]
		public async Task empty_over_length_opted_out_and_closed_sends_are_refused()
		{
			var conversation = await StartAsync();
			IntakeConfig.ConversationMaxOutboundLength = 20;

			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "   ")).Should().Be(IntakeConversationActionResults.InvalidMessage);
			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", new string('x', 21))).Should().Be(IntakeConversationActionResults.InvalidMessage);
			(await _service.SendDispatcherMessageAsync(DepartmentId, "missing", "disp-1", "Hi")).Should().Be(IntakeConversationActionResults.NotFound);
			(await _service.SendDispatcherMessageAsync(DepartmentId + 1, conversation.IntakeConversationId, "disp-1", "Hi")).Should().Be(IntakeConversationActionResults.NotFound);

			Stored(conversation.IntakeConversationId).OptedOut = true;
			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "Hi")).Should().Be(IntakeConversationActionResults.OptedOut);

			Stored(conversation.IntakeConversationId).State = (int)IntakeConversationStates.Closed;
			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "Hi")).Should().Be(IntakeConversationActionResults.Closed);
		}

		[Test]
		public async Task a_failed_send_is_logged_on_the_row_and_reported()
		{
			var conversation = await StartAsync();
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			_sendFails = true;

			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "Hello?")).Should().Be(IntakeConversationActionResults.SendFailed);

			var row = Messages(conversation.IntakeConversationId).Last();
			row.Status.Should().Be((int)IntakeConversationMessageStatuses.Failed);
			row.Error.Should().Contain("21610");
		}

		[Test]
		public async Task release_returns_an_ai_channel_to_enhanced_ai_without_a_text()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			_sent.Clear();
			_queued.Clear();
			_router.Calls.Clear();

			(await _service.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-2")).Should().Be(IntakeConversationActionResults.HeldByAnotherDispatcher);
			(await _service.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1")).Should().Be(IntakeConversationActionResults.Done);

			var row = Stored(conversation.IntakeConversationId);
			row.Handler.Should().Be((int)IntakeConversationHandlers.Assistant);
			row.AssignedUserId.Should().BeNull();
			_sent.Should().BeEmpty();
			_router.Calls.Should().Equal("Released:disp-1");
			_queued.Single().Channel.Should().Be(AiDispatchChannels.IntakeConversation, "Enhanced AI answers the texter's last message if nobody did");
		}

		[Test]
		public async Task release_of_a_template_channel_or_a_finished_ai_is_unassisted()
		{
			var conversation = await StartAsync();
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			await _service.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Unassisted);

			_channel.ProcessingMode = (int)IntakeProcessingModes.EnhancedAi;
			Stored(conversation.IntakeConversationId).AssistantTurns = IntakeConfig.ConversationMaxAssistantTurns;
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			await _service.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Unassisted, "Enhanced AI used all its turns");
		}

		[Test]
		public async Task close_can_tell_the_texter_and_the_routers_hear_it_once()
		{
			var conversation = await StartAsync();
			_sent.Clear();
			_router.Calls.Clear();

			(await _service.CloseAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "Units on scene", true)).Should().Be(IntakeConversationActionResults.Done);
			(await _service.CloseAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "again", true)).Should().Be(IntakeConversationActionResults.Closed);

			_sent.Single().Text.Should().Be("Springfield Fire: This text conversation has ended. If this is a life-threatening emergency, call 911.");
			_router.Calls.Should().Equal("Closed:Units on scene");
			Stored(conversation.IntakeConversationId).CloseReason.Should().Be("Units on scene");
		}

		[Test]
		public async Task close_without_notify_sends_nothing()
		{
			var conversation = await StartAsync();
			_sent.Clear();

			(await _service.CloseAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", null, false)).Should().Be(IntakeConversationActionResults.Done);

			_sent.Should().BeEmpty();
			Stored(conversation.IntakeConversationId).IsOpen.Should().BeFalse();
		}

		[Test]
		public async Task a_router_that_throws_never_fails_an_action()
		{
			_router.Throw = true;
			var conversation = await StartAsync();

			(await _service.HandleParticipantMessageAsync(conversation, "SM2", "More info", DateTime.UtcNow)).Should().BeTrue();
			(await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1")).Should().Be(IntakeConversationActionResults.Done);
			(await _service.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1")).Should().Be(IntakeConversationActionResults.Done);
			(await _service.CloseAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", null, false)).Should().Be(IntakeConversationActionResults.Done);

			_router.Calls.Should().HaveCount(5);
		}

		[Test]
		public async Task the_desk_item_written_during_routing_survives_later_changes()
		{
			_router.OnRoute = c => _service.SetDeskItemAsync(DepartmentId, c.IntakeConversationId, "desk-1");
			var conversation = await StartAsync();

			await _service.HandleParticipantMessageAsync(conversation, "SM2", "More info", DateTime.UtcNow);
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			await _service.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");

			Stored(conversation.IntakeConversationId).DeskItemId.Should().Be("desk-1");
			(await _service.GetByIdAsync(DepartmentId, conversation.IntakeConversationId)).DeskItemId.Should().Be("desk-1");
		}

		#endregion Dispatcher actions

		#region Worker 80 and reads

		[Test]
		public async Task idle_threads_and_threads_whose_call_ended_close_once()
		{
			var idle = await StartAsync();
			var other = new IntakeConversation
			{
				IntakeConversationId = "conv-ended", DepartmentId = DepartmentId, CallId = 902, ChannelType = (int)IntakeChannelTypes.Sms, ParticipantAddress = "+15305550111",
				State = (int)IntakeConversationStates.Open, Handler = (int)IntakeConversationHandlers.Unassisted, StartedOn = _clock.Now, LastActivityOn = _clock.Now.AddHours(1)
			};
			_conversations.Rows.Add(other);
			_conversations.EndedCalls.Add(902);
			_router.Calls.Clear();
			_sent.Clear();

			var closed = await _service.CloseIdleAsync(_clock.Now.AddMinutes(IntakeConfig.ConversationIdleCloseMinutes + 5));
			var again = await _service.CloseIdleAsync(_clock.Now.AddMinutes(IntakeConfig.ConversationIdleCloseMinutes + 10));

			closed.Should().Be(2);
			again.Should().Be(0);
			Stored(idle.IntakeConversationId).CloseReason.Should().Be("Idle");
			Stored("conv-ended").CloseReason.Should().Be("CallClosed");
			_router.Calls.Should().BeEquivalentTo(new[] { "Closed:Idle", "Closed:CallClosed" });
			_sent.Should().BeEmpty("worker 80 closes quietly");
		}

		[Test]
		public async Task an_active_thread_is_not_idle()
		{
			await StartAsync();

			(await _service.CloseIdleAsync(_clock.Now.AddMinutes(30))).Should().Be(0);
		}

		[Test]
		public async Task the_view_reads_the_text_from_the_call_notes_without_prefixes()
		{
			var conversation = await StartAsync();
			await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "Where are you?");

			var view = await _service.GetViewAsync(DepartmentId, conversation.IntakeConversationId, "viewer");

			view.Messages.Select(m => m.Text).Should().Equal(
				"Car crash on Route 9, two cars",
				_sent[0].Text,
				"You are now connected with Jane. Please describe the situation you are having and your location.",
				"Where are you?");
			view.Messages.Last().AuthorName.Should().Be("Jane");
			view.Messages.Should().OnlyContain(m => m.CreatedOn.Kind == DateTimeKind.Utc);
			_protectedRead.Verify(p => p.ResolveNotesForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<CallNote>>(), null, "viewer", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task the_view_shows_redacted_text_in_a_protected_department()
		{
			var conversation = await StartAsync();
			_protectedRead.Setup(p => p.ResolveNotesForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<CallNote>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((int _, IReadOnlyList<CallNote> notes, string __, string ___, CancellationToken ____) => { foreach (var n in notes) n.Note = ProtectedDataEnvelope.RedactionValue; })
				.ReturnsAsync(new ProtectedReadResult { IsProtected = true });

			var view = await _service.GetViewAsync(DepartmentId, conversation.IntakeConversationId, "viewer");

			view.Messages.Should().OnlyContain(m => m.Text == ProtectedDataEnvelope.RedactionValue);
		}

		[Test]
		public async Task the_participant_transcript_has_only_the_texters_messages()
		{
			var conversation = await StartAsync();
			await _service.HandleParticipantMessageAsync(conversation, "SM2", "At 12 Oak St", DateTime.UtcNow);

			(await _service.GetParticipantTranscriptAsync(DepartmentId, conversation.IntakeConversationId, 3000))
				.Should().Be("Car crash on Route 9, two cars\nAt 12 Oak St");
		}

		#endregion Worker 80 and reads

		#region Enhanced AI turns (D16)

		private IntakeAssistantRequest _asked;

		private void ModelAnswers(string reply, bool done = false, bool wantsDispatcher = false, string outcome = IntakeAssistantOutcomes.Answered, Func<Task> during = null)
		{
			_assistant.Setup(a => a.AskAsync(It.IsAny<IntakeAssistantRequest>(), It.IsAny<CancellationToken>()))
				.Returns(async (IntakeAssistantRequest r, CancellationToken _) =>
				{
					_asked = r;
					if (during != null)
						await during();
					return new IntakeAssistantAnswer { Outcome = outcome, ReplyText = reply, Done = done, WantsDispatcher = wantsDispatcher };
				});
		}

		private AiDispatchQueueItem LastTurn() => _queued.Last(q => q.Channel == AiDispatchChannels.IntakeConversation);

		private const string Finished = "Thanks — Springfield Fire dispatch has your information. A dispatcher may text you here. If this is a life-threatening emergency, call 911.";

		[Test]
		public async Task an_ai_turn_texts_one_question_and_refreshes_the_call_from_the_transcript()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			ModelAnswers("What is the exact address or nearest cross street?");
			_sent.Clear();

			(await _service.ProcessAssistantTurnAsync(LastTurn())).Should().Be(IntakeAssistantOutcomes.Answered);

			_sent.Single().Text.Should().Be("What is the exact address or nearest cross street?");
			Messages(conversation.IntakeConversationId).Last().Author.Should().Be((int)IntakeConversationAuthors.Assistant);
			_notes.Last().Note.Should().Be("Text to caller (Enhanced AI): What is the exact address or nearest cross street?");
			_notes.Last().Source.Should().Be((int)CallNoteSources.System);
			Stored(conversation.IntakeConversationId).AssistantTurns.Should().Be(1);
			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Assistant);

			_asked.Turn.Should().Be(1);
			_asked.DepartmentName.Should().Be("Springfield Fire");
			_asked.Lines.First().FromCaller.Should().BeTrue();
			_asked.Lines.First().Text.Should().Be("Car crash on Route 9, two cars");
			_asked.Lines.Should().Contain(l => !l.FromCaller && l.Text.StartsWith("Springfield Fire: You texted"));

			var refresh = _queued.Last();
			refresh.Channel.Should().Be(AiDispatchChannels.IntakeSms);
			refresh.IntakeConversationId.Should().Be(conversation.IntakeConversationId);
			refresh.ConversationComplete.Should().BeFalse();
			refresh.NameIsPlaceholder.Should().BeTrue("the call still has the channel-and-sender name");
		}

		[Test]
		public async Task a_redelivered_turn_or_quick_texts_get_one_answer()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			await _service.HandleParticipantMessageAsync(conversation, "SM2", "Main and 5th", DateTime.UtcNow);
			var turns = _queued.Where(q => q.Channel == AiDispatchChannels.IntakeConversation).ToList();
			turns.Should().HaveCount(2);
			ModelAnswers("Is anyone hurt?");

			(await _service.ProcessAssistantTurnAsync(turns[0])).Should().Be(IntakeAssistantOutcomes.Answered);
			(await _service.ProcessAssistantTurnAsync(turns[1])).Should().Be("AlreadyAnswered");
			(await _service.ProcessAssistantTurnAsync(turns[0])).Should().Be("AlreadyAnswered");

			_assistant.Verify(a => a.AskAsync(It.IsAny<IntakeAssistantRequest>(), It.IsAny<CancellationToken>()), Times.Once);
			_asked.Lines.Where(l => l.FromCaller).Select(l => l.Text).Should().Equal("Car crash on Route 9, two cars", "Main and 5th");
		}

		[Test]
		public async Task done_hands_the_thread_to_a_dispatcher_with_the_closing_text()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			ModelAnswers(null, done: true);
			_sent.Clear();

			(await _service.ProcessAssistantTurnAsync(LastTurn())).Should().Be("Finished");

			_sent.Select(s => s.Text).Should().Equal(Finished);
			var row = Stored(conversation.IntakeConversationId);
			row.Handler.Should().Be((int)IntakeConversationHandlers.Unassisted);
			row.AssistantTurns.Should().Be(IntakeConfig.ConversationMaxAssistantTurns, "a later release must not restart Enhanced AI");
			_queued.Last().ConversationComplete.Should().BeTrue("the last refresh writes the AI note");
		}

		[Test]
		public async Task wanting_a_dispatcher_sends_the_reply_then_the_closing_text()
		{
			await StartAsync(IntakeProcessingModes.EnhancedAi);
			ModelAnswers("If anyone is in danger, call 911 now.", wantsDispatcher: true);
			_sent.Clear();

			await _service.ProcessAssistantTurnAsync(LastTurn());

			_sent.Select(s => s.Text).Should().Equal("If anyone is in danger, call 911 now.", Finished);
		}

		[Test]
		public async Task the_turn_limit_ends_enhanced_ai()
		{
			IntakeConfig.ConversationMaxAssistantTurns = 1;
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			ModelAnswers("Where are you?");
			_sent.Clear();

			(await _service.ProcessAssistantTurnAsync(LastTurn())).Should().Be("Finished");
			_sent.Select(s => s.Text).Should().Equal("Where are you?", Finished);

			await _service.HandleParticipantMessageAsync(conversation, "SM2", "Main St", DateTime.UtcNow);
			_queued.Count(q => q.Channel == AiDispatchChannels.IntakeConversation).Should().Be(1, "an unassisted thread queues no more turns");
		}

		[TestCase(IntakeAssistantOutcomes.Unavailable)]
		[TestCase(IntakeAssistantOutcomes.InvalidOutput)]
		[TestCase(IntakeAssistantOutcomes.Busy)]
		[TestCase(IntakeAssistantOutcomes.BudgetExhausted)]
		public async Task a_model_that_cannot_answer_hands_the_thread_to_a_dispatcher(string outcome)
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			ModelAnswers(null, outcome: outcome);
			_sent.Clear();
			_queued.Clear();

			(await _service.ProcessAssistantTurnAsync(new AiDispatchQueueItem
			{
				DepartmentId = DepartmentId, CallId = CallId, Channel = AiDispatchChannels.IntakeConversation, IntakeConversationId = conversation.IntakeConversationId
			})).Should().Be(outcome);

			_sent.Select(s => s.Text).Should().Equal(Finished);
			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Unassisted);
			_queued.Should().BeEmpty("nothing new to enrich from");
		}

		[TestCase(IntakeAssistantOutcomes.NotEntitled)]
		[TestCase(IntakeAssistantOutcomes.ProtectionUnsupported)]
		public async Task a_department_that_lost_enhanced_ai_or_joined_adp_is_never_sent_to_the_model(string outcome)
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			_assistant.Setup(a => a.CheckAsync(DepartmentId)).ReturnsAsync(outcome);
			_sent.Clear();

			(await _service.ProcessAssistantTurnAsync(LastTurn())).Should().Be(outcome);

			_assistant.Verify(a => a.AskAsync(It.IsAny<IntakeAssistantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_sent.Select(s => s.Text).Should().Equal(Finished);
			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Unassisted);
		}

		[Test]
		public async Task a_dispatcher_who_connects_while_the_model_thinks_silences_it()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			ModelAnswers("Is anyone hurt?", during: () => _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1"));
			_sent.Clear();

			(await _service.ProcessAssistantTurnAsync(LastTurn())).Should().Be("Superseded");

			_sent.Select(s => s.Text).Should().Equal("You are now connected with Jane. Please describe the situation you are having and your location.");
		}

		[Test]
		public async Task a_turn_for_a_closed_or_dispatcher_held_thread_does_nothing()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			var turn = LastTurn();
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			(await _service.ProcessAssistantTurnAsync(turn)).Should().Be("NotAssistant");

			await _service.CloseAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", null, false);
			(await _service.ProcessAssistantTurnAsync(turn)).Should().Be("Closed");

			_assistant.Verify(a => a.AskAsync(It.IsAny<IntakeAssistantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_second_worker_for_the_same_text_stands_down_while_the_first_is_answering()
		{
			// The second delivery loads the thread AFTER the first claimed its turn (same count it would claim next): the first's reply row,
			// written before its claim, is what stops it, not the turn count.
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			var turn = LastTurn();
			string second = null;
			ModelAnswers("Is anyone hurt?", during: async () => second = await _service.ProcessAssistantTurnAsync(turn));
			_sent.Clear();

			(await _service.ProcessAssistantTurnAsync(turn)).Should().Be(IntakeAssistantOutcomes.Answered);

			second.Should().Be("AlreadyAnswered");
			_assistant.Verify(a => a.AskAsync(It.IsAny<IntakeAssistantRequest>(), It.IsAny<CancellationToken>()), Times.Once);
			_sent.Select(s => s.Text).Should().Equal("Is anyone hurt?");
			Stored(conversation.IntakeConversationId).AssistantTurns.Should().Be(1);
		}

		[Test]
		public async Task a_text_that_arrives_while_the_model_thinks_gets_its_own_answer()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			var first = LastTurn();
			ModelAnswers("Where are you?", during: () => _service.HandleParticipantMessageAsync(conversation, "SM2", "Main and 5th, two cars, one on fire", DateTime.UtcNow));
			_sent.Clear();

			// The answer to the first text never saw the second: it is not sent, and the newest text is queued for a turn.
			(await _service.ProcessAssistantTurnAsync(first)).Should().Be("Superseded");
			_sent.Should().BeEmpty();
			var next = _queued.Last(q => q.Channel == AiDispatchChannels.IntakeConversation);
			next.IntakeConversationMessageId.Should().Be(Messages(conversation.IntakeConversationId).Last(m => m.Direction == (int)IntakeConversationMessageDirections.Inbound).IntakeConversationMessageId);

			ModelAnswers("Is anyone hurt?");
			(await _service.ProcessAssistantTurnAsync(next)).Should().Be(IntakeAssistantOutcomes.Answered);
			_sent.Select(s => s.Text).Should().Equal("Is anyone hurt?");
			_asked.Lines.Where(l => l.FromCaller).Select(l => l.Text).Should().Contain("Main and 5th, two cars, one on fire");

			// The withdrawn row is not part of the thread anyone reads.
			var view = await _service.GetViewAsync(DepartmentId, conversation.IntakeConversationId, "viewer");
			view.Messages.Where(m => m.Author == (int)IntakeConversationAuthors.Assistant).Select(m => m.Text).Should().Equal("Is anyone hurt?");
		}

		[Test]
		public async Task a_dispatchers_failed_text_is_not_an_answer_when_enhanced_ai_resumes()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			_sendFails = true;
			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "Where are you?")).Should().Be(IntakeConversationActionResults.SendFailed);
			_sendFails = false;
			await _service.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			ModelAnswers("What is the exact address?");
			_sent.Clear();

			(await _service.ProcessAssistantTurnAsync(LastTurn())).Should().Be(IntakeAssistantOutcomes.Answered, "the texter never received the dispatcher's question");
			_sent.Select(s => s.Text).Should().Equal("What is the exact address?");
		}

		[Test]
		public async Task a_turn_the_queue_refuses_hands_the_thread_to_a_dispatcher()
		{
			_queue.Setup(q => q.EnqueueAiDispatchTriageAsync(It.Is<AiDispatchQueueItem>(i => i.Channel == AiDispatchChannels.IntakeConversation), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);

			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);

			Stored(conversation.IntakeConversationId).Handler.Should().Be((int)IntakeConversationHandlers.Unassisted, "nothing would ever answer the texter otherwise");
			_sent.Last().Text.Should().Be(Finished);
		}

		[Test]
		public async Task a_later_turn_the_model_cannot_answer_still_queues_the_final_refresh()
		{
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			ModelAnswers("Where are you?");
			await _service.ProcessAssistantTurnAsync(LastTurn());
			await _service.HandleParticipantMessageAsync(conversation, "SM2", "Main St", DateTime.UtcNow);
			ModelAnswers(null, outcome: IntakeAssistantOutcomes.Unavailable);
			_queued.Clear();

			(await _service.ProcessAssistantTurnAsync(new AiDispatchQueueItem
			{
				DepartmentId = DepartmentId, CallId = CallId, Channel = AiDispatchChannels.IntakeConversation, IntakeConversationId = conversation.IntakeConversationId
			})).Should().Be(IntakeAssistantOutcomes.Unavailable);

			_queued.Should().ContainSingle(q => q.Channel == AiDispatchChannels.IntakeSms && q.ConversationComplete,
				"turn 1's interim refresh wrote no summary; the last one must");
		}

		#endregion Enhanced AI turns (D16)

		#region Races and retries (PR 588 review)

		[Test]
		public async Task a_dispatcher_whose_thread_was_taken_over_during_the_implicit_connect_does_not_text()
		{
			var conversation = await StartAsync();
			_sms.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string to, string text, int _, int __, CancellationToken ___) =>
				{
					_sent.Add((to, text));
					// Sam takes the thread over while Jane's introduction is going out.
					if (text.StartsWith("You are now connected with Jane", StringComparison.Ordinal))
						Stored(conversation.IntakeConversationId).AssignedUserId = "disp-2";
					return new IntakeSmsSendResult { Success = true, ProviderMessageId = "SMout" + _sent.Count };
				});
			_sent.Clear();

			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "What is the address?"))
				.Should().Be(IntakeConversationActionResults.HeldByAnotherDispatcher);

			_sent.Select(s => s.Text).Should().NotContain("What is the address?");
		}

		[Test]
		public async Task a_stop_that_commits_while_a_text_is_being_recorded_wins()
		{
			var conversation = await StartAsync();
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallNote n, CancellationToken _) =>
				{
					n.CallNoteId = _notes.Count + 1;
					_notes.Add(n);
					// The texter's STOP closes the thread between the dispatcher's note and the carrier.
					if (n.Note.EndsWith("Are you safe?", StringComparison.Ordinal))
					{
						Stored(conversation.IntakeConversationId).OptedOut = true;
						Stored(conversation.IntakeConversationId).State = (int)IntakeConversationStates.Closed;
					}
					return n;
				});
			_sent.Clear();

			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "Are you safe?"))
				.Should().Be(IntakeConversationActionResults.OptedOut);

			_sent.Should().BeEmpty();
			Messages(conversation.IntakeConversationId).Last().Error.Should().Be("OptedOut");
		}

		[Test]
		public async Task an_introduction_the_carrier_refused_is_sent_again_and_an_accepted_one_never()
		{
			var conversation = await StartAsync();
			_sendFails = true;
			await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1");
			_sendFails = false;
			_sent.Clear();

			(await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1")).Should().Be(IntakeConversationActionResults.Done);
			(await _service.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, "disp-1")).Should().Be(IntakeConversationActionResults.Done);
			(await _service.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, "disp-1", "Is anyone hurt?")).Should().Be(IntakeConversationActionResults.Done);

			_sent.Select(s => s.Text).Should().Equal(
				"You are now connected with Jane. Please describe the situation you are having and your location.", "Is anyone hurt?");
		}

		[Test]
		public async Task a_stop_whose_close_failed_finishes_closing_on_the_carriers_retry()
		{
			var conversation = await StartAsync();
			_conversations.FailingCloses = 1;

			(await _service.HandleParticipantMessageAsync(conversation, "SM9", "STOP", DateTime.UtcNow)).Should().BeTrue("the STOP is recorded on the thread");
			Stored(conversation.IntakeConversationId).IsOpen.Should().BeTrue("the close failed after the STOP was recorded");

			(await _service.HandleParticipantMessageAsync(conversation, "SM9", "STOP", DateTime.UtcNow)).Should().BeTrue();

			var row = Stored(conversation.IntakeConversationId);
			row.IsOpen.Should().BeFalse();
			row.OptedOut.Should().BeTrue();
			Messages(conversation.IntakeConversationId).Count(m => m.ProviderMessageId == "SM9").Should().Be(1);
		}

		[Test]
		public async Task a_reply_noted_on_the_call_is_handled_even_when_a_later_step_fails()
		{
			// Unhandled would make intake open a second pending call with the same text (the dedicated-number join path).
			var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
			_conversations.FailingTouches = 1;

			(await _service.HandleParticipantMessageAsync(conversation, "SM2", "Main and 5th", DateTime.UtcNow)).Should().BeTrue();

			_notes.Should().Contain(n => n.Note == IntakeConversationTexts.CallerNotePrefix + "Main and 5th");
		}

		[Test]
		public async Task a_reply_that_never_reached_the_call_is_not_handled()
		{
			var conversation = await StartAsync();
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("db"));
			_conversations.FailingTouches = 1;

			(await _service.HandleParticipantMessageAsync(conversation, "SM2", "Main and 5th", DateTime.UtcNow)).Should().BeFalse("intake keeps the text on a call of its own");
		}

		[Test]
		public async Task the_refresh_keeps_a_dispatchers_rename_through_the_placeholder_fingerprint()
		{
			var savedKey = AiConfig.AuditHmacKey;
			AiConfig.AuditHmacKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
			try
			{
				var conversation = await StartAsync(IntakeProcessingModes.EnhancedAi);
				ModelAnswers("Where are you?");

				await _service.ProcessAssistantTurnAsync(LastTurn());

				var refresh = _queued.Last(q => q.Channel == AiDispatchChannels.IntakeSms);
				refresh.NameIsPlaceholder.Should().BeTrue();
				refresh.PlaceholderNameFingerprint.Should().Be(Resgrid.Services.AiDispatch.AiDispatchEnrichmentService.PlaceholderFingerprint(DepartmentId, CallId, _call.Name));
			}
			finally
			{
				AiConfig.AuditHmacKey = savedKey;
			}
		}

		[Test]
		public async Task a_text_between_the_idle_read_and_the_close_keeps_the_thread_open()
		{
			var conversation = await StartAsync();
			var sweepAt = _clock.Now.AddMinutes(IntakeConfig.ConversationIdleCloseMinutes + 5);
			_conversations.AfterIdleRead = () => Stored(conversation.IntakeConversationId).LastActivityOn = sweepAt;

			(await _service.CloseIdleAsync(sweepAt)).Should().Be(0);

			Stored(conversation.IntakeConversationId).IsOpen.Should().BeTrue();
		}

		[Test]
		public void a_dispatcher_name_holding_the_label_end_never_leaks_into_the_text()
		{
			var note = IntakeConversationTexts.DispatcherNotePrefix("Sam): Team") + "Where are you?";

			IntakeConversationTexts.StripPrefix(note).Should().Be("Where are you?");
			IntakeConversationTexts.DispatcherNotePrefix("Jane").Should().Be("Text to caller (Jane): ");
		}

		#endregion Races and retries (PR 588 review)
	}
}
