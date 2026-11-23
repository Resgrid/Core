using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.NumberProvider;
using Resgrid.Services;
using Resgrid.Web.Services.Twilio;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>Unknown callers and texters to a department number (plan D8, D9), and the pending calls their overflow makes.</summary>
	[TestFixture]
	public class DispatcherInboundHandlersTests
	{
		private const int DepartmentId = 42;
		private const string DepartmentNumber = "15559990000";
		private const string Sender = "+15551234567";

		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDispatcherDeskConfigService> _config;
		private Mock<IDispatcherRoutingService> _routing;
		private Mock<IDispatcherVoiceService> _voice;
		private Mock<ILimitsService> _limits;
		private Mock<INumbersService> _numbers;
		private Mock<ITextCommandService> _textCommands;
		private DispatcherDeskConfig _deskConfig;

		[SetUp]
		public void SetUp()
		{
			_deskConfig = new DispatcherDeskConfig { Enabled = true };

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(x => x.GetDepartmentIdByTextToCallNumberAsync(DepartmentNumber)).ReturnsAsync(DepartmentId);

			_config = new Mock<IDispatcherDeskConfigService>();
			_config.Setup(x => x.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _deskConfig);

			_routing = new Mock<IDispatcherRoutingService>();
			_voice = new Mock<IDispatcherVoiceService>();
			_voice.Setup(x => x.QueueNameFor(It.IsAny<DispatcherQueueItem>())).Returns<DispatcherQueueItem>(i => DispatcherVoiceRoutes.QueueName(i.DispatcherQueueItemId));

			_limits = new Mock<ILimitsService>();
			_limits.Setup(x => x.CanDepartmentProvisionNumberAsync(DepartmentId)).ReturnsAsync(true);

			_numbers = new Mock<INumbersService>();
			_textCommands = new Mock<ITextCommandService>();
			_textCommands.Setup(x => x.DetermineType(It.IsAny<string>())).Returns(new TextCommandPayload { Type = TextCommandTypes.None });
		}

		private static DispatcherQueueItem Item(int kind, int state = (int)DispatcherQueueItemStates.Offered) => new DispatcherQueueItem
		{
			DispatcherQueueItemId = "item-1",
			DepartmentId = DepartmentId,
			Kind = kind,
			State = state,
			ExternalReference = "CA1"
		};

		#region Voice

		private DispatcherInboundVoiceHandler VoiceHandler() =>
			new DispatcherInboundVoiceHandler(_settings.Object, _config.Object, _routing.Object, _voice.Object, _limits.Object);

		[Test]
		public async System.Threading.Tasks.Task Voice_offered_holds_the_caller_in_the_items_queue()
		{
			_routing.Setup(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Offered, Item = Item((int)DispatcherQueueItemKinds.Voice) });

			var twiml = await VoiceHandler().HandleAsync(Sender, "+" + DepartmentNumber, "CA1");

			twiml.Should().Contain("<Enqueue");
			twiml.Should().Contain(">rgdesk-item-1</Enqueue>");
			twiml.Should().Contain("api/Twilio/DeskHold?itemId=item-1");
			twiml.Should().Contain("api/Twilio/DeskEnqueueAction?itemId=item-1");
			_routing.Verify(x => x.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(r => r.DepartmentId == DepartmentId && r.Kind == DispatcherQueueItemKinds.Voice &&
				r.SourceAddress == Sender && r.TargetAddress == "+" + DepartmentNumber && r.ExternalReference == "CA1"), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Voice_overflow_redirects_to_the_overflow_handling()
		{
			_routing.Setup(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Overflow, Item = Item((int)DispatcherQueueItemKinds.Voice) });

			var twiml = await VoiceHandler().HandleAsync(Sender, "+" + DepartmentNumber, "CA1");

			twiml.Should().Contain("<Redirect");
			twiml.Should().Contain("api/Twilio/DeskOverflow?itemId=item-1");
		}

		[Test]
		public async System.Threading.Tasks.Task Voice_not_routed_keeps_todays_handling()
		{
			_routing.Setup(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherRouteResult.NotRouted());

			(await VoiceHandler().HandleAsync(Sender, "+" + DepartmentNumber, "CA1")).Should().BeNull();
		}

		[Test]
		public async System.Threading.Tasks.Task Voice_to_a_number_of_no_department_or_a_desk_without_voice_is_not_taken()
		{
			(await VoiceHandler().HandleAsync(Sender, "+15550000000", "CA1")).Should().BeNull();

			_deskConfig.RouteVoice = false;
			(await VoiceHandler().HandleAsync(Sender, "+" + DepartmentNumber, "CA1")).Should().BeNull();

			_deskConfig = new DispatcherDeskConfig { Enabled = false };
			(await VoiceHandler().HandleAsync(Sender, "+" + DepartmentNumber, "CA1")).Should().BeNull();

			_routing.Verify(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voice_on_a_plan_without_numbers_is_not_taken()
		{
			_limits.Setup(x => x.CanDepartmentProvisionNumberAsync(DepartmentId)).ReturnsAsync(false);

			(await VoiceHandler().HandleAsync(Sender, "+" + DepartmentNumber, "CA1")).Should().BeNull();
			_routing.Verify(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voice_retry_of_the_same_call_holds_without_a_second_item()
		{
			_routing.Setup(x => x.GetItemByExternalReferenceAsync("CA1")).ReturnsAsync(Item((int)DispatcherQueueItemKinds.Voice, (int)DispatcherQueueItemStates.Queued));

			var twiml = await VoiceHandler().HandleAsync(Sender, "+" + DepartmentNumber, "CA1");

			twiml.Should().Contain("<Enqueue");
			_routing.Verify(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voice_failure_keeps_todays_handling()
		{
			_routing.Setup(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			(await VoiceHandler().HandleAsync(Sender, "+" + DepartmentNumber, "CA1")).Should().BeNull();
		}

		#endregion Voice

		#region Text

		private DispatcherInboundTextHandler TextHandler() =>
			new DispatcherInboundTextHandler(_config.Object, _routing.Object, _settings.Object, _limits.Object, _numbers.Object, _textCommands.Object);

		private void RouteText(DispatcherRouteOutcomes outcome, DispatcherQueueItem item = null)
		{
			_routing.Setup(x => x.RouteInboundMessageAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DispatcherRouteResult { Outcome = outcome, Item = item });
		}

		[TestCase(DispatcherRouteOutcomes.Offered)]
		[TestCase(DispatcherRouteOutcomes.Queued)]
		[TestCase(DispatcherRouteOutcomes.Joined)]
		[TestCase(DispatcherRouteOutcomes.Duplicate)]
		public async System.Threading.Tasks.Task Text_routed_is_taken_by_the_desk(DispatcherRouteOutcomes outcome)
		{
			RouteText(outcome, Item((int)DispatcherQueueItemKinds.Text));

			(await TextHandler().HandleAsync(DepartmentId, "15551234567", DepartmentNumber, "Car in the ditch on Route 9", "SM1")).Should().BeTrue();

			_routing.Verify(x => x.RouteInboundMessageAsync(It.Is<DispatcherEnqueueRequest>(r => r.DepartmentId == DepartmentId &&
				r.Kind == DispatcherQueueItemKinds.Text && r.SourceAddress == Sender && r.TargetAddress == "+" + DepartmentNumber &&
				r.Body == "Car in the ditch on Route 9" && r.ProviderMessageId == "SM1"), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Text_not_routed_keeps_todays_handling()
		{
			RouteText(DispatcherRouteOutcomes.NotRouted);

			(await TextHandler().HandleAsync(DepartmentId, Sender, DepartmentNumber, "hello", "SM1")).Should().BeFalse();
		}

		[Test]
		public async System.Threading.Tasks.Task Text_stop_is_never_a_desk_conversation()
		{
			_textCommands.Setup(x => x.DetermineType("STOP")).Returns(new TextCommandPayload { Type = TextCommandTypes.Stop });

			(await TextHandler().HandleAsync(DepartmentId, Sender, DepartmentNumber, "STOP", "SM1")).Should().BeFalse();
			_routing.Verify(x => x.RouteInboundMessageAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Text_from_a_dispatch_source_number_keeps_text_to_call()
		{
			_settings.Setup(x => x.GetTextToCallSourceNumbersForDepartmentAsync(DepartmentId)).ReturnsAsync("1555123*");
			_numbers.Setup(x => x.DoesNumberMatchAnyPattern(It.Is<List<string>>(l => l.Single() == "1555123*"), "15551234567")).Returns(true);

			(await TextHandler().HandleAsync(DepartmentId, Sender, DepartmentNumber, "CAD: STRUCTURE FIRE", "SM1")).Should().BeFalse();
			_routing.Verify(x => x.RouteInboundMessageAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Text_to_a_desk_without_texts_or_without_the_plan_is_not_taken()
		{
			_deskConfig.RouteText = false;
			(await TextHandler().HandleAsync(DepartmentId, Sender, DepartmentNumber, "hello", "SM1")).Should().BeFalse();

			_deskConfig.RouteText = true;
			_limits.Setup(x => x.CanDepartmentProvisionNumberAsync(DepartmentId)).ReturnsAsync(false);
			(await TextHandler().HandleAsync(DepartmentId, Sender, DepartmentNumber, "hello", "SM1")).Should().BeFalse();

			_routing.Verify(x => x.RouteInboundMessageAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Text_to_the_master_number_only_joins_an_open_conversation()
		{
			RouteText(DispatcherRouteOutcomes.Joined, Item((int)DispatcherQueueItemKinds.Text));
			(await TextHandler().HandleAsync(null, Sender, "15550001111", "Yes, the north side", "SM2")).Should().BeTrue();

			_routing.Verify(x => x.RouteInboundMessageAsync(It.Is<DispatcherEnqueueRequest>(r => r.DepartmentId == 0), It.IsAny<CancellationToken>()), Times.Once);
			_config.Verify(x => x.GetConfigAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);

			// Anything else on the master number (a new conversation, or none) keeps the master number's handling.
			RouteText(DispatcherRouteOutcomes.NotRouted);
			(await TextHandler().HandleAsync(null, Sender, "15550001111", "hello", "SM3")).Should().BeFalse();
		}

		[Test]
		public async System.Threading.Tasks.Task Text_help_to_the_master_number_keeps_the_help_text()
		{
			_textCommands.Setup(x => x.DetermineType("HELP")).Returns(new TextCommandPayload { Type = TextCommandTypes.Help });

			(await TextHandler().HandleAsync(null, Sender, "15550001111", "HELP", "SM1")).Should().BeFalse();
			_routing.Verify(x => x.RouteInboundMessageAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Text_overflow_is_taken_and_left_to_the_routing_service()
		{
			// The routing service has already made the pending call from the conversation, acknowledged the sender and (Enhanced AI
			// text overflow) queued the AI: the webhook only answers with its empty reply.
			var item = Item((int)DispatcherQueueItemKinds.Text, (int)DispatcherQueueItemStates.Queued);
			RouteText(DispatcherRouteOutcomes.Overflow, item);

			(await TextHandler().HandleAsync(DepartmentId, Sender, DepartmentNumber, "Tree down on Elm", "SM1")).Should().BeTrue();

			_routing.Verify(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Text_failure_keeps_todays_handling()
		{
			_routing.Setup(x => x.RouteInboundMessageAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("db down"));

			(await TextHandler().HandleAsync(DepartmentId, Sender, DepartmentNumber, "hello", "SM1")).Should().BeFalse();
		}

		#endregion Text

		#region Pending calls

		private Mock<IDepartmentsService> _departments;
		private Mock<ICallsService> _calls;
		private Mock<IEventAggregator> _events;
		private global::Resgrid.Tests.Services.DispatcherDesk.InMemoryDispatcherDeskStore _store;
		private Call _saved;
		private int _saves;

		private DispatcherDeskPendingCalls PendingCalls()
		{
			_saves = 0;
			_saved = null;
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "admin-1", TimeZone = "UTC" });

			_calls = new Mock<ICallsService>();
			_calls.Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call c, CancellationToken _) =>
				{
					_saves++;
					c.CallId = 500 + _saves;
					c.Number = "26-" + c.CallId;
					_saved = c;
					return c;
				});
			// The department's calls logged since the item: the voicemail call once one is saved (found again by its "DQ:" source).
			_calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(() => _saved == null ? new List<Call>() : new List<Call> { _saved });

			_events = new Mock<IEventAggregator>();
			_store = new global::Resgrid.Tests.Services.DispatcherDesk.InMemoryDispatcherDeskStore();

			// As the routing service: the stored item takes the call and waits in the queue again.
			_routing.Setup(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns((DispatcherQueueItem i, string note, CancellationToken _) => LinkInStore(i, note));

			return new DispatcherDeskPendingCalls(_departments.Object, _calls.Object, _routing.Object, _store.ItemsRepository, _store.EventsRepository, _events.Object)
			{
				ClaimCheckInterval = TimeSpan.Zero
			};
		}

		private async System.Threading.Tasks.Task LinkInStore(DispatcherQueueItem item, string note)
		{
			var current = await _store.ItemsRepository.GetItemAsync(item.DispatcherQueueItemId);
			var expected = current.Version;
			current.CallId ??= item.CallId;
			current.OverflowNote = note;
			current.State = (int)DispatcherQueueItemStates.Queued;
			await _store.ItemsRepository.TryUpdateAsync(current, expected);
		}

		private DispatcherQueueItem OverflowedVoiceItem()
		{
			var item = Item((int)DispatcherQueueItemKinds.Voice, (int)DispatcherQueueItemStates.Overflowed);
			item.OverflowNote = "No dispatcher could take the call: voicemail";
			_store?.Items.Add(Clone(item));
			return item;
		}

		[Test]
		public async System.Threading.Tasks.Task Pending_call_from_a_voicemail_carries_the_recording_and_becomes_the_voice_item()
		{
			var sequence = new List<string>();
			var pendingCalls = PendingCalls();
			_routing.Setup(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback(() => sequence.Add("linked"))
				.Returns((DispatcherQueueItem i, string note, CancellationToken _) => LinkInStore(i, note));
			_events.Setup(x => x.SendMessage(It.IsAny<CallUpdatedEvent>())).Callback(() => sequence.Add("announced"));
			var item = OverflowedVoiceItem();

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 23, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			result.Durable.Should().BeTrue();
			result.Call.Should().NotBeNull();
			_saved.State.Should().Be((int)CallStates.Pending);
			_saved.DispatchOn.Should().BeNull();
			_saved.HasBeenDispatched.Should().BeNull();
			_saved.CallSource.Should().Be((int)CallSources.AudioImport);
			_saved.NatureOfCall.Should().Be($"Voicemail from {Sender}");
			_saved.Notes.Should().Contain("https://api.twilio.com/rec/RE1").And.Contain("23 seconds");
			_saved.SourceIdentifier.Should().Be("DQ:item-1");
			_saved.ReportingUserId.Should().Be("admin-1");
			_saved.Dispatches.Should().BeEmpty();

			// The voice item becomes the voicemail call (MarkOverflowedAsync stores the CallId and reopens it): no second desk item.
			_routing.Verify(x => x.MarkOverflowedAsync(It.Is<DispatcherQueueItem>(i => i.DispatcherQueueItemId == "item-1" && i.CallId == 501),
				"Voicemail saved as pending call 26-501", It.IsAny<CancellationToken>()), Times.Once);
			_routing.Verify(x => x.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.LinkCallAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.RouteDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
			_store.Items.Single().CallId.Should().Be(501);

			// Announced after the link, so the desk's call listener finds the call already on the desk (Duplicate).
			sequence.Should().Equal("linked", "announced");
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_for_an_item_that_already_has_its_call_does_nothing()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			item.CallId = 9;

			(await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 23, "RE1")).Outcome
				.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);

			_calls.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_whose_item_is_already_claimed_saves_no_second_call()
		{
			var pendingCalls = PendingCalls();
			var first = OverflowedVoiceItem();
			var second = Clone(first);

			// Two overlapping requests read the same snapshot (no CallId): the first claims and saves, the second finds the claim.
			(await pendingCalls.FromVoicemailAsync(first, Sender, "https://api.twilio.com/rec/RE1", 23, "RE1")).Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			var repeat = await pendingCalls.FromVoicemailAsync(second, Sender, "https://api.twilio.com/rec/RE1", 23, "RE1");

			repeat.Outcome.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);
			repeat.Durable.Should().BeTrue();
			_saves.Should().Be(1);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_claim_survives_a_failed_link_so_a_repeat_saves_nothing()
		{
			var pendingCalls = PendingCalls();
			_routing.Setup(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("desk down"));
			var item = OverflowedVoiceItem();

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", null, "RE1");

			// The call is kept (and announced) although the desk could not link it; the claim on the item names the recording.
			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once);
			_routing.Verify(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Exactly(DispatcherDeskPendingCalls.WriteAttempts));
			_store.Items.Single().OverflowNote.Should().StartWith("Voicemail recording RE1 (claimed ").And.EndWith("): https://api.twilio.com/rec/RE1");

			// The repeat finds the call by its "DQ:" source: it is held (durable), and no second call is saved.
			var repeat = await pendingCalls.FromVoicemailAsync(Clone(item), Sender, "https://api.twilio.com/rec/RE1", null, "RE1");
			repeat.Outcome.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);
			repeat.Durable.Should().BeTrue();
			_saves.Should().Be(1);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_repeat_links_the_call_a_failed_link_left_unlinked()
		{
			var pendingCalls = PendingCalls();
			_routing.Setup(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("desk down"));
			var item = OverflowedVoiceItem();
			(await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", null, "RE1")).Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			_store.Items.Single().CallId.Should().BeNull();

			// The desk is back: a repeat of the same recording links the saved call to its voice item, without saving another.
			_routing.Setup(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns((DispatcherQueueItem i, string note, CancellationToken _) => LinkInStore(i, note));
			var repeat = await pendingCalls.FromVoicemailAsync(Clone(item), Sender, "https://api.twilio.com/rec/RE1", null, "RE1");

			repeat.Outcome.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);
			_store.Items.Single().CallId.Should().Be(501);
			_saves.Should().Be(1);
			_routing.Verify(x => x.RouteDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_claimed_by_a_request_still_saving_is_in_progress_not_durable()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			// Another request claimed the item and has not saved its call yet.
			_store.Items.Single().OverflowNote = DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1", DateTime.UtcNow);

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.InProgress);
			result.Durable.Should().BeFalse("nothing is held for certain while the claim's own save has not finished");
			_saves.Should().Be(0);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_that_fails_before_its_save_gives_the_claim_back_so_a_repeat_can_save_it()
		{
			var pendingCalls = PendingCalls();
			_calls.SetupSequence(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("db blip"))
				.ReturnsAsync(new List<DepartmentCallPriority>());
			var item = OverflowedVoiceItem();

			var failed = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			failed.Outcome.Should().Be(DispatcherVoicemailOutcomes.NotSaved);
			_store.Items.Single().OverflowNote.Should().Be("No dispatcher could take the call: voicemail", "the claim is given back with the note it replaced");

			(await pendingCalls.FromVoicemailAsync(Clone(item), Sender, "https://api.twilio.com/rec/RE1", 12, "RE1")).Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			_saves.Should().Be(1);
			_store.Items.Single().CallId.Should().Be(501);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_repeat_takes_back_a_claim_given_back_meanwhile()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			var stored = _store.Items.Single();
			stored.OverflowNote = DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1", DateTime.UtcNow);

			// The claim's request fails before saving and gives the claim back while this repeat looks at it.
			var looks = 0;
			_calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(() =>
			{
				if (++looks == 1)
					stored.OverflowNote = "No dispatcher could take the call: voicemail";
				return _saved == null ? new List<Call>() : new List<Call> { _saved };
			});

			(await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1")).Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			_saves.Should().Be(1);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_save_that_threw_after_storing_its_call_is_finished_not_saved_again()
		{
			var pendingCalls = PendingCalls();
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).Returns((Call c, CancellationToken _) =>
			{
				// Stored, then a later step of saving threw.
				_saves++;
				c.CallId = 600;
				c.Number = "26-600";
				_saved = c;
				throw new InvalidOperationException("protection step failed");
			});
			var item = OverflowedVoiceItem();

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			result.Call.CallId.Should().Be(600);
			_store.Items.Single().CallId.Should().Be(600);
			_saves.Should().Be(1);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_save_that_threw_before_storing_gives_the_claim_back()
		{
			var pendingCalls = PendingCalls();
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
			var item = OverflowedVoiceItem();

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.NotSaved);
			DispatcherDeskPendingCalls.IsClaimed(_store.Items.Single()).Should().BeFalse();
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_whose_screen_refresh_fails_is_still_saved()
		{
			var pendingCalls = PendingCalls();
			_events.Setup(x => x.SendMessage(It.IsAny<CallUpdatedEvent>())).Throws(new InvalidOperationException("bus down"));

			var result = await pendingCalls.FromVoicemailAsync(OverflowedVoiceItem(), Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			result.Durable.Should().BeTrue();
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_link_is_never_confirmed_from_the_item_in_hand()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			// The voice item is gone by the time the call is saved: there is no stored row to link.
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) =>
			{
				_saves++;
				c.CallId = 501;
				_saved = c;
				_store.Items.Clear();
				return c;
			});

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			item.CallId.Should().BeNull("no stored row confirmed the link");
			_routing.Verify(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.RouteDepartmentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_link_is_retried_when_another_webhook_changed_the_item()
		{
			var pendingCalls = PendingCalls();
			var attempts = 0;
			_routing.Setup(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns((DispatcherQueueItem i, string note, CancellationToken _) =>
					// The first attempt loses its conditional write quietly (a status callback touched the item), as the routing service does.
					++attempts == 1 ? System.Threading.Tasks.Task.CompletedTask : LinkInStore(i, note));
			var item = OverflowedVoiceItem();

			(await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1")).Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);

			attempts.Should().Be(2);
			_store.Items.Single().CallId.Should().Be(501);
			_routing.Verify(x => x.RouteDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_whose_save_fails_is_not_durable()
		{
			var pendingCalls = PendingCalls();
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call)null);

			var result = await pendingCalls.FromVoicemailAsync(OverflowedVoiceItem(), Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.NotSaved);
			result.Durable.Should().BeFalse();
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_save_that_threw_with_the_lookup_down_keeps_the_claim_and_is_unconfirmed()
		{
			var pendingCalls = PendingCalls();
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).Returns((Call c, CancellationToken _) =>
			{
				// The call may be stored; then the save fails, and so does the lookup that would tell.
				_saves++;
				c.CallId = 600;
				_saved = c;
				throw new InvalidOperationException("protection step failed");
			});
			_calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ThrowsAsync(new InvalidOperationException("db blip"));
			var item = OverflowedVoiceItem();

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Unconfirmed);
			result.Durable.Should().BeFalse();
			DispatcherDeskPendingCalls.IsClaimed(_store.Items.Single()).Should().BeTrue("a failed lookup is not proof that nothing was saved");

			// A repeat within the claim's life saves nothing either.
			(await pendingCalls.FromVoicemailAsync(Clone(item), Sender, "https://api.twilio.com/rec/RE1", 12, "RE1")).Outcome
				.Should().Be(DispatcherVoicemailOutcomes.InProgress);
			_saves.Should().Be(1);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_repeat_takes_over_an_expired_claim_and_saves_the_call()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			// A request claimed the item and stopped before saving (the host went down).
			_store.Items.Single().OverflowNote = DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1",
				DateTime.UtcNow.AddMinutes(-DispatcherDeskPendingCalls.ClaimExpiryMinutes - 1));

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			_saves.Should().Be(1);
			_store.Items.Single().CallId.Should().Be(501);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_takeover_links_the_call_the_stopped_request_saved_instead_of_saving_another()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			_store.Items.Single().OverflowNote = DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1",
				DateTime.UtcNow.AddMinutes(-DispatcherDeskPendingCalls.ClaimExpiryMinutes - 1));
			// The stopped request saved its call, which a dispatcher has since dispatched (no longer pending), but never linked it.
			_saved = new Call
			{
				CallId = 444, Number = "26-444", DepartmentId = DepartmentId, CallSource = (int)CallSources.AudioImport, SourceIdentifier = "DQ:item-1",
				State = (int)CallStates.Active
			};

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);
			_saves.Should().Be(0);
			_store.Items.Single().CallId.Should().Be(444);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_takeover_with_the_lookup_down_saves_nothing_leaves_the_claim_as_it_was_and_is_unconfirmed()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			var expired = DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1",
				DateTime.UtcNow.AddMinutes(-DispatcherDeskPendingCalls.ClaimExpiryMinutes - 1));
			_store.Items.Single().OverflowNote = expired;
			_calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ThrowsAsync(new InvalidOperationException("db blip"));

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			// The stopped request may have saved the call: the failed lookup proves nothing, so this is no definite "not saved".
			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Unconfirmed);
			result.Durable.Should().BeFalse();
			_saves.Should().Be(0);
			_store.Items.Single().OverflowNote.Should().Be(expired);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_claim_is_stamped_when_it_is_written_not_when_the_request_started()
		{
			var pendingCalls = PendingCalls();
			var clock = new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc);
			pendingCalls.UtcNow = () => clock;
			var item = OverflowedVoiceItem();
			var stored = _store.Items.Single();
			stored.OverflowNote = DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1", clock);

			// The live claim's request gives it back while this one waits on it; time passes before this request claims.
			var looks = 0;
			_calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(() =>
			{
				if (++looks == 1)
				{
					stored.OverflowNote = "No dispatcher could take the call: voicemail";
					clock = clock.AddSeconds(3);
				}

				return _saved == null ? new List<Call>() : new List<Call> { _saved };
			});
			string claimAtSave = null;
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) =>
			{
				claimAtSave = _store.Items.Single().OverflowNote;
				_saves++;
				c.CallId = 501;
				_saved = c;
				return c;
			});

			(await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1")).Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);

			DispatcherDeskPendingCalls.ClaimedOn(new DispatcherQueueItem { OverflowNote = claimAtSave }).Should().Be(clock,
				"a claim must not expire early by the time this request waited before writing it");
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_whose_claim_is_taken_over_before_its_save_saves_nothing()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			var other = DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1", DateTime.UtcNow.AddSeconds(1));
			// This request stalls while it prepares the call; its claim expires and another request takes it over meanwhile.
			_calls.Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() =>
			{
				_store.Items.Single().OverflowNote = other;
				return new List<DepartmentCallPriority>();
			});

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.InProgress);
			_saves.Should().Be(0, "the request that took the claim over saves the voicemail's one call");
			_store.Items.Single().OverflowNote.Should().Be(other);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_whose_item_became_its_voicemail_call_before_its_save_saves_nothing()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			_calls.Setup(x => x.GetCallByIdAsync(777, It.IsAny<bool>())).ReturnsAsync(new Call
			{
				CallId = 777, Number = "26-777", DepartmentId = DepartmentId, CallSource = (int)CallSources.AudioImport, SourceIdentifier = "DQ:item-1"
			});
			// A request that took the claim over saved and linked the voicemail's call while this one stalled.
			_calls.Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() =>
			{
				_store.Items.Single().CallId = 777;
				return new List<DepartmentCallPriority>();
			});

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);
			result.Durable.Should().BeTrue();
			_saves.Should().Be(0);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_whose_item_became_another_call_before_its_save_still_saves_the_recording()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			_calls.Setup(x => x.GetCallByIdAsync(888, It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 888, DepartmentId = DepartmentId, CallSource = (int)CallSources.User });
			_calls.Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() =>
			{
				_store.Items.Single().CallId = 888;
				return new List<DepartmentCallPriority>();
			});

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			_saves.Should().Be(1, "a call that is not the voicemail never stands in for the recording");
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_second_call_delete_is_tried_again_after_a_blip()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			var other = new Call { CallId = 777, Number = "26-777", DepartmentId = DepartmentId, CallSource = (int)CallSources.AudioImport, SourceIdentifier = "DQ:item-1" };
			_calls.Setup(x => x.GetCallByIdAsync(777, It.IsAny<bool>())).ReturnsAsync(other);
			var saves = new List<Call>();
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) =>
			{
				saves.Add(c);
				if (saves.Count == 1)
				{
					// Another request took the claim over, saved and linked its own call while this one was saving.
					c.CallId = 501;
					_store.Items.Single().CallId = 777;
				}
				else if (saves.Count == 2)
				{
					throw new InvalidOperationException("db blip");
				}

				return c;
			});

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);
			saves.Should().HaveCount(3);
			saves[2].IsDeleted.Should().BeTrue("no later request comes back for the second call, so its delete is tried again");
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_whose_claim_was_taken_over_deletes_its_own_second_call()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			var other = new Call { CallId = 777, Number = "26-777", DepartmentId = DepartmentId, CallSource = (int)CallSources.AudioImport, SourceIdentifier = "DQ:item-1" };
			_calls.Setup(x => x.GetCallByIdAsync(777, It.IsAny<bool>())).ReturnsAsync(other);
			var saves = new List<Call>();
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) =>
			{
				saves.Add(c);
				if (saves.Count == 1)
				{
					// This request stalled past the claim's life: another took the claim over, saved and linked its own call meanwhile.
					c.CallId = 501;
					_store.Items.Single().CallId = 777;
				}

				return c;
			});

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);
			result.Durable.Should().BeTrue();
			saves.Should().HaveCount(2);
			saves[1].CallId.Should().Be(501);
			saves[1].IsDeleted.Should().BeTrue("one voicemail never waits as two pending calls");
			saves[1].DeletedByUserId.Should().Be(DispatcherDeskPendingCalls.SystemUserId);
			_store.Items.Single().CallId.Should().Be(777);
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_call_is_kept_when_the_item_is_linked_to_a_call_that_is_not_its_voicemail()
		{
			var pendingCalls = PendingCalls();
			var item = OverflowedVoiceItem();
			_calls.Setup(x => x.GetCallByIdAsync(888, It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 888, DepartmentId = DepartmentId, CallSource = (int)CallSources.User });
			var saves = 0;
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) =>
			{
				saves++;
				c.CallId = 501;
				_store.Items.Single().CallId = 888;
				return c;
			});

			var result = await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", 12, "RE1");

			// The recording is not dropped: the voicemail waits as its own pending call.
			result.Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);
			saves.Should().Be(1);
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_repeat_links_a_saved_call_that_is_no_longer_pending()
		{
			var pendingCalls = PendingCalls();
			_routing.Setup(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("desk down"));
			var item = OverflowedVoiceItem();
			(await pendingCalls.FromVoicemailAsync(item, Sender, "https://api.twilio.com/rec/RE1", null, "RE1")).Outcome.Should().Be(DispatcherVoicemailOutcomes.Saved);

			// A dispatcher dispatched the pending call before the desk came back.
			_saved.State = (int)CallStates.Active;
			_routing.Setup(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns((DispatcherQueueItem i, string note, CancellationToken _) => LinkInStore(i, note));

			(await pendingCalls.FromVoicemailAsync(Clone(item), Sender, "https://api.twilio.com/rec/RE1", null, "RE1")).Outcome
				.Should().Be(DispatcherVoicemailOutcomes.AlreadyTaken);
			_store.Items.Single().CallId.Should().Be(501);
			_saves.Should().Be(1);
		}

		[Test]
		public void Voicemail_claim_notes_carry_their_time_and_expire()
		{
			var now = new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc);
			var item = new DispatcherQueueItem { OverflowNote = DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1", now) };

			item.OverflowNote.Should().Be("Voicemail recording RE1 (claimed 2026-11-22T18:00:00.0000000Z): https://api.twilio.com/rec/RE1");
			DispatcherDeskPendingCalls.ClaimedOn(item).Should().Be(now);
			DispatcherDeskPendingCalls.ClaimExpired(item, now.AddMinutes(DispatcherDeskPendingCalls.ClaimExpiryMinutes - 1)).Should().BeFalse();
			DispatcherDeskPendingCalls.ClaimExpired(item, now.AddMinutes(DispatcherDeskPendingCalls.ClaimExpiryMinutes)).Should().BeTrue();

			// A claim with no time (written before claims carried one) cannot be waited for.
			DispatcherDeskPendingCalls.ClaimExpired(new DispatcherQueueItem { OverflowNote = "Voicemail recording RE1: https://api.twilio.com/rec/RE1" }, now)
				.Should().BeTrue();
			DispatcherDeskPendingCalls.ClaimExpired(new DispatcherQueueItem { OverflowNote = "No dispatcher free: voicemail" }, now).Should().BeFalse();
		}

		[Test]
		public void Voicemail_claim_notes_give_back_their_recording()
		{
			var now = new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc);

			DispatcherDeskPendingCalls.TryReadClaim(DispatcherDeskPendingCalls.ClaimNote("RE1", "https://api.twilio.com/rec/RE1", now), out var sid, out var url)
				.Should().BeTrue();
			sid.Should().Be("RE1");
			url.Should().Be("https://api.twilio.com/rec/RE1");

			DispatcherDeskPendingCalls.TryReadClaim(DispatcherDeskPendingCalls.ClaimNote(null, "https://api.twilio.com/rec/RE2", now), out sid, out url).Should().BeTrue();
			sid.Should().BeNull("a recording with no id is noted as \"(no id)\"");
			url.Should().Be("https://api.twilio.com/rec/RE2");

			// A claim written before claims carried a time.
			DispatcherDeskPendingCalls.TryReadClaim("Voicemail recording RE3: https://api.twilio.com/rec/RE3", out sid, out url).Should().BeTrue();
			sid.Should().Be("RE3");
			url.Should().Be("https://api.twilio.com/rec/RE3");

			DispatcherDeskPendingCalls.TryReadClaim("No dispatcher free: voicemail", out _, out _).Should().BeFalse();
			DispatcherDeskPendingCalls.TryReadClaim("Voicemail recording RE4 (claimed 2026-11-22T18:00:00.0000000Z): ", out _, out _).Should().BeFalse();
		}

		/// <summary>The stored item as a voicemail request that claimed it and then stopped (or could not confirm its save) leaves it.</summary>
		private DispatcherQueueItem StrandedVoicemailItem(DateTime claimedOn, string recordingUrl = "https://api.twilio.com/rec/RE1")
		{
			OverflowedVoiceItem();
			var stored = _store.Items.Single();
			stored.CreatedOn = DateTime.UtcNow.AddMinutes(-30);
			stored.SourceAddress = Sender;
			stored.OverflowNote = DispatcherDeskPendingCalls.ClaimNote("RE1", recordingUrl, claimedOn);
			if (stored.OverflowNote.Length > 250)
				stored.OverflowNote = stored.OverflowNote.Substring(0, 250);
			return stored;
		}

		private static DateTime ExpiredClaimTime() => DateTime.UtcNow.AddMinutes(-DispatcherDeskPendingCalls.ClaimExpiryMinutes - 1);

		[Test]
		public async System.Threading.Tasks.Task Worker_recovers_a_stranded_voicemail_by_saving_its_pending_call()
		{
			var pendingCalls = PendingCalls();
			StrandedVoicemailItem(ExpiredClaimTime());

			var recovered = await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2));

			recovered.Should().Be(1);
			_saves.Should().Be(1);
			_saved.State.Should().Be((int)CallStates.Pending);
			_saved.SourceIdentifier.Should().Be("DQ:item-1");
			_saved.ContactNumber.Should().Be(Sender, "the caller's number comes from the item");
			_saved.Notes.Should().Contain("https://api.twilio.com/rec/RE1");
			_store.Items.Single().CallId.Should().Be(501);
			_store.Items.Single().State.Should().Be((int)DispatcherQueueItemStates.Queued, "the voicemail call waits for a dispatcher");
		}

		[Test]
		public async System.Threading.Tasks.Task Worker_recovery_links_the_call_a_stopped_voicemail_request_saved_instead_of_saving_another()
		{
			var pendingCalls = PendingCalls();
			StrandedVoicemailItem(ExpiredClaimTime());
			_saved = new Call
			{
				CallId = 444, Number = "26-444", DepartmentId = DepartmentId, CallSource = (int)CallSources.AudioImport, SourceIdentifier = "DQ:item-1",
				State = (int)CallStates.Pending
			};

			var recovered = await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2));

			recovered.Should().Be(1);
			_saves.Should().Be(0);
			_store.Items.Single().CallId.Should().Be(444);
		}

		[Test]
		public async System.Threading.Tasks.Task Worker_recovery_leaves_a_live_voicemail_claim_to_its_request()
		{
			var pendingCalls = PendingCalls();
			var stored = StrandedVoicemailItem(DateTime.UtcNow.AddMinutes(-1));
			var note = stored.OverflowNote;

			var recovered = await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2));

			recovered.Should().Be(0);
			_saves.Should().Be(0);
			_store.Items.Single().OverflowNote.Should().Be(note);
			_store.Items.Single().CallId.Should().BeNull();
		}

		[Test]
		public async System.Threading.Tasks.Task Worker_recovery_looks_only_at_recent_overflowed_voice_items_with_a_claim_and_no_call()
		{
			var pendingCalls = PendingCalls();
			var stored = StrandedVoicemailItem(ExpiredClaimTime());

			// Older than the window.
			stored.CreatedOn = DateTime.UtcNow.AddDays(-3);
			(await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2))).Should().Be(0);

			// Back in the queue (a dispatcher can still answer it): not a stranded voicemail.
			stored.CreatedOn = DateTime.UtcNow.AddMinutes(-30);
			stored.State = (int)DispatcherQueueItemStates.Queued;
			(await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2))).Should().Be(0);

			// An overflow with no voicemail claim (the caller hung up before recording).
			stored.State = (int)DispatcherQueueItemStates.Overflowed;
			stored.OverflowNote = "No dispatcher free: voicemail";
			(await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2))).Should().Be(0);

			_saves.Should().Be(0);
			_store.Items.Single().CallId.Should().BeNull();
		}

		[Test]
		public async System.Threading.Tasks.Task Worker_recovery_with_the_call_lookup_down_saves_nothing_and_keeps_the_claim_for_its_next_pass()
		{
			var pendingCalls = PendingCalls();
			var stored = StrandedVoicemailItem(ExpiredClaimTime());
			var note = stored.OverflowNote;
			_calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ThrowsAsync(new InvalidOperationException("database down"));

			var recovered = await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2));

			recovered.Should().Be(0);
			_saves.Should().Be(0, "the stopped request may have saved the call");
			_store.Items.Single().OverflowNote.Should().Be(note, "the expired claim is put back as it was, for the next pass");

			// Storage is back: the next pass finishes it.
			_calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(() => _saved == null ? new List<Call>() : new List<Call> { _saved });
			(await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2))).Should().Be(1);
			_saves.Should().Be(1);
		}

		[Test]
		public async System.Threading.Tasks.Task Worker_recovery_takes_the_whole_recording_url_from_history_when_the_claim_note_was_cut()
		{
			var pendingCalls = PendingCalls();
			var url = "https://api.twilio.com/2010-04-01/Accounts/AC" + new string('a', 32) + "/Recordings/RE" + new string('b', 32) + "?" + new string('c', 120);
			var stored = StrandedVoicemailItem(ExpiredClaimTime(), url);
			stored.OverflowNote.Length.Should().Be(250, "the claim note is cut at 250 characters");
			_store.Events.Add(new DispatcherQueueEvent
			{
				DispatcherQueueEventId = "ev-1", DepartmentId = DepartmentId, DispatcherQueueItemId = "item-1", OccurredOn = DateTime.UtcNow.AddMinutes(-20),
				EventType = (int)DispatcherQueueEventTypes.VoiceEnded, Detail = $"The voicemail's call may have been saved; that could not be confirmed. Recording: {url}"
			});

			(await pendingCalls.RecoverExpiredVoicemailClaimsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2))).Should().Be(1);

			_saved.Notes.Should().Contain(url);
		}

		private static DispatcherQueueItem Clone(DispatcherQueueItem item) => new DispatcherQueueItem
		{
			DispatcherQueueItemId = item.DispatcherQueueItemId, DepartmentId = item.DepartmentId, Kind = item.Kind, State = item.State,
			ExternalReference = item.ExternalReference, CallId = item.CallId, OverflowNote = item.OverflowNote, Version = item.Version
		};

		#endregion Pending calls

		[TestCase("+15551234567", "+15551234567")]
		[TestCase("15551234567", "+15551234567")]
		[TestCase(" +1 (555) 123-4567 ", "+15551234567")]
		[TestCase("alice@example.com", "alice@example.com")]
		public void Desk_addresses_are_e164(string input, string expected)
		{
			DeskPhoneNumbers.ToE164(input).Should().Be(expected);
		}

		[TestCase("+15551234567", "+15551234567")]
		[TestCase("15551234567", "+15551234567")]
		[TestCase("", null)]
		public void Voice_provider_dials_e164(string input, string expected)
		{
			DispatcherVoiceProvider.ToE164(input).Should().Be(expected);
		}

		[Test]
		public async System.Threading.Tasks.Task Voice_provider_refuses_bad_input_without_calling_twilio()
		{
			var provider = new DispatcherVoiceProvider();

			(await provider.PlaceCallAsync("", "+15559990000", "https://api.example/x", null, 30)).Should().BeNull();
			(await provider.PlaceCallAsync("+15551234567", "+15559990000", "not a url", null, 30)).Should().BeNull();
			(await provider.RedirectCallAsync("", "https://api.example/x")).Should().BeFalse();
			(await provider.EndCallAsync(null)).Should().BeFalse();
		}
	}
}
