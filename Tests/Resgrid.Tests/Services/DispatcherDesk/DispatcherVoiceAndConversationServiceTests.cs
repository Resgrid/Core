using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// The desk's voice and conversation services (M0324): ringing a dispatcher's phone takes work only when it can be dialled and
	/// gives back work a failed dial took; replies are recorded before they are sent; notes follow the reply rule; a provider
	/// message is stored once.
	/// </summary>
	[TestFixture]
	public class DispatcherVoiceAndConversationServiceTests
	{
		private const int Dept = 7;
		private const string ItemId = "item-1";
		private const string Me = "ann";

		private InMemoryDispatcherDeskStore _store;
		private Mock<IDispatcherRoutingService> _routing;
		private Mock<IDispatcherVoiceProvider> _voiceProvider;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IUserProfileService> _profiles;
		private Mock<IDispatcherPresenceService> _presence;
		private Mock<ITextMessageProvider> _texts;
		private HashSet<string> _supervisors;
		private DateTime _now;

		[SetUp]
		public void SetUp()
		{
			_now = new DateTime(2026, 11, 21, 16, 0, 0, DateTimeKind.Utc);
			_store = new InMemoryDispatcherDeskStore();
			_routing = new Mock<IDispatcherRoutingService>();
			_voiceProvider = new Mock<IDispatcherVoiceProvider>();
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(x => x.GetTextToCallNumberForDepartmentAsync(Dept)).ReturnsAsync("+15550000000");
			_profiles = new Mock<IUserProfileService>();
			_supervisors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_presence = new Mock<IDispatcherPresenceService>();
			_presence.Setup(x => x.CanSuperviseAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string u) => _supervisors.Contains(u));
			_texts = new Mock<ITextMessageProvider>();
			_texts.Setup(x => x.SendTextMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(), It.IsAny<int>(),
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>())).ReturnsAsync(true);
		}

		private DispatcherQueueItem AddItem(DispatcherQueueItemKinds kind, DispatcherQueueItemStates state, string assignedTo = null, string target = "+15559990000")
		{
			var item = new DispatcherQueueItem
			{
				DispatcherQueueItemId = ItemId,
				DepartmentId = Dept,
				Kind = (int)kind,
				State = (int)state,
				AssignedUserId = assignedTo,
				SourceAddress = "+15551234567",
				TargetAddress = target,
				ExternalReference = kind == DispatcherQueueItemKinds.Voice ? "CA1" : null,
				CreatedOn = _now.AddMinutes(-1),
				LastActivityOn = _now.AddMinutes(-1)
			};
			_store.Items.Add(item);

			_routing.Setup(x => x.GetItemDetailAsync(Dept, ItemId, It.IsAny<string>()))
				.ReturnsAsync(() => new DispatcherQueueItemDetail { Item = _store.Items.Single() });
			return item;
		}

		// ------------------------------------------------------------------ voice

		private DispatcherVoiceService Voice() =>
			new DispatcherVoiceService(_voiceProvider.Object, _routing.Object, _store.PresencesRepository, _profiles.Object, _settings.Object) { UtcNow = () => _now };

		private void AcceptTakesIt() =>
			_routing.Setup(x => x.AcceptAsync(Dept, ItemId, Me, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
			{
				var item = _store.Items.Single();
				item.State = (int)DispatcherQueueItemStates.Accepted;
				item.AssignedUserId = Me;
				return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
			});

		[Test]
		public async Task Answer_with_no_number_to_call_from_leaves_the_work_where_it_was()
		{
			AddItem(DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Offered, Me, target: null);
			_settings.Setup(x => x.GetTextToCallNumberForDepartmentAsync(Dept)).ReturnsAsync((string)null);

			var result = await Voice().ConnectAsync(Dept, ItemId, Me, "+15557770000");

			result.Success.Should().BeFalse();
			result.Error.Should().Be(DispatcherDeskErrors.ChannelUnavailable);
			_routing.Verify(x => x.AcceptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_voiceProvider.Verify(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Answer_the_provider_refuses_gives_back_the_work_it_took()
		{
			AddItem(DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Offered, Me);
			AcceptTakesIt();
			ReleaseObservedReturns();
			_voiceProvider.Setup(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>())).ReturnsAsync((string)null);

			var result = await Voice().ConnectAsync(Dept, ItemId, Me, "+15557770000");

			result.Error.Should().Be(DispatcherDeskErrors.ChannelUnavailable);
			_routing.Verify(x => x.AcceptAsync(Dept, ItemId, Me, It.IsAny<CancellationToken>()), Times.Once);
			_routing.Verify(x => x.ReleaseObservedAsync(It.Is<DispatcherQueueItem>(i => i.DispatcherQueueItemId == ItemId && i.AssignedUserId == Me),
				It.IsAny<CancellationToken>()), Times.Once);
			_routing.Verify(x => x.RecordVoiceEventAsync(It.IsAny<DispatcherQueueItem>(), DispatcherQueueEventTypes.VoiceConnecting, It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Answer_the_provider_refuses_keeps_work_another_ring_of_the_same_dispatcher_placed_meanwhile()
		{
			AddItem(DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Offered, Me);
			AcceptTakesIt();
			// A second tap (another device) found the item already theirs and placed its leg while this attempt's dial was failing.
			_routing.Setup(x => x.GetItemDetailAsync(Dept, ItemId, It.IsAny<string>())).ReturnsAsync(() => new DispatcherQueueItemDetail
			{
				Item = _store.Items.Single(),
				Events = new List<DispatcherQueueEvent>
				{
					new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnecting, UserId = Me, OccurredOn = _now.AddSeconds(2) }
				}
			});
			_voiceProvider.Setup(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>())).ReturnsAsync((string)null);

			(await Voice().ConnectAsync(Dept, ItemId, Me, "+15557770000")).Error.Should().Be(DispatcherDeskErrors.ChannelUnavailable);

			_routing.Verify(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()), Times.Never,
				"the other ring's live call keeps the work");
		}

		[Test]
		public async Task Answer_the_provider_refuses_still_gives_back_work_when_only_an_older_ring_is_in_its_history()
		{
			AddItem(DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Offered, Me);
			AcceptTakesIt();
			_routing.Setup(x => x.GetItemDetailAsync(Dept, ItemId, It.IsAny<string>())).ReturnsAsync(() => new DispatcherQueueItemDetail
			{
				Item = _store.Items.Single(),
				Events = new List<DispatcherQueueEvent>
				{
					new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnecting, UserId = Me, OccurredOn = _now.AddMinutes(-5) },
					new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnecting, UserId = "bob", OccurredOn = _now.AddSeconds(2) }
				}
			});
			ReleaseObservedReturns();
			_voiceProvider.Setup(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>())).ReturnsAsync((string)null);

			await Voice().ConnectAsync(Dept, ItemId, Me, "+15557770000");

			_routing.Verify(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Answer_the_provider_refuses_still_gives_back_work_after_a_failed_history_read()
		{
			AddItem(DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Offered, Me);
			AcceptTakesIt();
			ReleaseObservedReturns();
			var reads = 0;
			_routing.Setup(x => x.GetItemDetailAsync(Dept, ItemId, It.IsAny<string>())).ReturnsAsync(() =>
			{
				// RingDispatcherAsync's own read, then a blip on the failed dial's first look.
				if (++reads == 2)
					throw new InvalidOperationException("db blip");
				return new DispatcherQueueItemDetail { Item = _store.Items.Single() };
			});
			_voiceProvider.Setup(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>())).ReturnsAsync((string)null);

			await Voice().ConnectAsync(Dept, ItemId, Me, "+15557770000");

			_routing.Verify(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()), Times.Once,
				"the item must not stay held by a dispatcher with no leg");
		}

		[Test]
		public async Task Answer_the_provider_refuses_keeps_work_when_another_ring_lands_after_its_read()
		{
			AddItem(DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Offered, Me);
			AcceptTakesIt();
			var events = new List<DispatcherQueueEvent>();
			_routing.Setup(x => x.GetItemDetailAsync(Dept, ItemId, It.IsAny<string>()))
				.ReturnsAsync(() => new DispatcherQueueItemDetail { Item = _store.Items.Single(), Events = events });
			// A second tap's leg records its VoiceConnecting after the failed dial read the row: the touch moves the Version, so the fenced
			// release fails, and the next look sees that ring.
			_routing.Setup(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>())).ReturnsAsync(() =>
			{
				events.Add(new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnecting, UserId = Me, OccurredOn = _now.AddSeconds(1) });
				return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable);
			});
			_voiceProvider.Setup(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>())).ReturnsAsync((string)null);

			await Voice().ConnectAsync(Dept, ItemId, Me, "+15557770000");

			_routing.Verify(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()), Times.Once);
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private void ReleaseObservedReturns() =>
			_routing.Setup(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => DispatcherDeskResult<DispatcherQueueItem>.Ok(_store.Items.Single()));

		[Test]
		public async Task Answer_the_provider_refuses_keeps_work_the_dispatcher_already_held()
		{
			AddItem(DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Accepted, Me);
			_voiceProvider.Setup(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("twilio down"));

			(await Voice().CallBackAsync(Dept, ItemId, Me, "+15557770000")).Error.Should().Be(DispatcherDeskErrors.ChannelUnavailable);

			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(DispatcherQueueItemKinds.Chat, "700001122")]
		[TestCase(DispatcherQueueItemKinds.Chat, "1122334455")]
		[TestCase(DispatcherQueueItemKinds.Other, "+15551234567")]
		public async Task Call_back_never_dials_a_chat_apps_user_id_as_a_phone_number(DispatcherQueueItemKinds kind, string sender)
		{
			// A Telegram user id is all digits: 9 digits passes for an international number, 10 for a North American one.
			AddItem(kind, DispatcherQueueItemStates.Accepted, Me).SourceAddress = sender;

			var result = await Voice().CallBackAsync(Dept, ItemId, Me, "+15557770000");

			result.Error.Should().Be(DispatcherDeskErrors.ChannelUnavailable);
			_routing.Verify(x => x.AcceptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_voiceProvider.Verify(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Call_back_rings_a_texter()
		{
			AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, Me);
			_voiceProvider.Setup(x => x.PlaceCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>())).ReturnsAsync("CA9");

			(await Voice().CallBackAsync(Dept, ItemId, Me, "+15557770000")).Success.Should().BeTrue();

			_voiceProvider.Verify(x => x.PlaceCallAsync("+15557770000", "+15559990000", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Answer_names_its_ring_attempt_in_the_status_callback()
		{
			AddItem(DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Offered, Me);
			AcceptTakesIt();
			string statusUrl = null;
			_voiceProvider.Setup(x => x.PlaceCallAsync("+15557770000", "+15559990000", It.IsAny<string>(), It.IsAny<string>(), DispatcherVoiceService.RingTimeoutSeconds,
					It.IsAny<CancellationToken>()))
				.Callback((string to, string from, string twiml, string status, int timeout, CancellationToken c) => statusUrl = status)
				.ReturnsAsync("CA-leg");

			(await Voice().ConnectAsync(Dept, ItemId, Me, "+15557770000")).Success.Should().BeTrue();

			statusUrl.Should().EndWith($"/api/Twilio/DeskLegStatus?itemId={ItemId}&userId={Me}&leg=dispatcher&attempt={_now.Ticks}");
			_routing.Verify(x => x.RecordVoiceEventAsync(It.IsAny<DispatcherQueueItem>(), DispatcherQueueEventTypes.VoiceConnecting, Me, It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		// ------------------------------------------------------------------ conversations

		private DispatcherConversationService Conversations(IDispatcherConversationMessagesRepository messages = null) =>
			new DispatcherConversationService(messages ?? _store.MessagesRepository, _store.ItemsRepository, _store.EventsRepository, _presence.Object,
				new Mock<IDispatcherDeskNotifier>().Object, _texts.Object, new Mock<IEmailSender>().Object, _settings.Object) { UtcNow = () => _now };

		[Test]
		public async Task A_note_is_for_the_holder_or_a_supervisor_on_open_work()
		{
			AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, "bob");
			var service = Conversations();

			(await service.AddNoteAsync(Dept, ItemId, Me, "checked the map")).Error.Should().Be(DispatcherDeskErrors.NotAssignedToYou);
			(await service.AddNoteAsync(Dept, ItemId, "bob", "caller is on the north side")).Success.Should().BeTrue();

			_supervisors.Add("sue");
			(await service.AddNoteAsync(Dept, ItemId, "sue", "supervisor note")).Success.Should().BeTrue();

			_store.Messages.Select(x => x.UserId).Should().Equal(new[] { "bob", "sue" });
		}

		[Test]
		public async Task A_note_is_not_added_to_finished_work()
		{
			AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Completed, "bob");
			_supervisors.Add("sue");

			(await Conversations().AddNoteAsync(Dept, ItemId, "sue", "late note")).Error.Should().Be(DispatcherDeskErrors.ItemNotAvailable);
			_store.Messages.Should().BeEmpty();
		}

		[Test]
		public async Task A_provider_message_delivered_twice_is_stored_once()
		{
			var item = AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, Me);
			var service = Conversations();

			var first = await service.RecordInboundAsync(item, "+15551234567", "+15559990000", "There is a fire", "SM1");
			var again = await service.RecordInboundAsync(item, "+15551234567", "+15559990000", "There is a fire", "sm1");

			first.Should().NotBeNull();
			again.Should().BeNull("a redelivery stores nothing, so nothing is announced again");
			_store.Messages.Should().ContainSingle();
		}

		[Test]
		public async Task An_overlapping_delivery_that_loses_the_insert_stores_nothing()
		{
			var item = AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, Me);
			// Both deliveries found no stored row; the table's unique provider id refuses the second insert.
			var messages = new Mock<IDispatcherConversationMessagesRepository>();
			messages.Setup(x => x.GetByProviderMessageIdAsync(It.IsAny<string>())).ReturnsAsync((DispatcherConversationMessage)null);
			messages.Setup(x => x.TryInsertAsync(It.IsAny<DispatcherConversationMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			(await Conversations(messages.Object).RecordInboundAsync(item, "+15551234567", "+15559990000", "There is a fire", "SM1")).Should().BeNull();
		}

		[Test]
		public async Task A_reply_is_recorded_before_it_is_sent()
		{
			AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, Me);
			var storedWhenSent = new List<int>();
			_texts.Setup(x => x.SendTextMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(), It.IsAny<int>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()))
				.Callback(() => storedWhenSent.AddRange(_store.Messages.Select(x => x.DeliveryState)))
				.ReturnsAsync(true);

			var result = await Conversations().SendReplyAsync(Dept, ItemId, Me, "On our way");

			result.Success.Should().BeTrue();
			storedWhenSent.Should().Equal(new[] { (int)DispatcherMessageDeliveryStates.Queued });
			_store.Messages.Should().ContainSingle(x => x.DeliveryState == (int)DispatcherMessageDeliveryStates.Sent && x.FromAddress == "+15559990000");
		}

		[Test]
		public async Task A_reply_the_provider_took_is_reported_sent_even_if_recording_the_result_fails()
		{
			AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, Me);
			var messages = new Mock<IDispatcherConversationMessagesRepository>();
			messages.Setup(x => x.InsertAsync(It.IsAny<DispatcherConversationMessage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DispatcherConversationMessage m, CancellationToken c, bool f) => m);
			messages.Setup(x => x.UpdateAsync(It.IsAny<DispatcherConversationMessage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("db down"));

			var result = await Conversations(messages.Object).SendReplyAsync(Dept, ItemId, Me, "On our way");

			result.Success.Should().BeTrue("the text went out; a refusal would invite a second send");
			_texts.Verify(x => x.SendTextMessage(It.IsAny<string>(), "On our way", It.IsAny<string>(), It.IsAny<MobileCarriers>(), Dept,
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
		}

		[Test]
		public async Task A_reply_s_delivery_result_is_written_again_on_its_own_when_the_first_write_fails()
		{
			AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, Me);
			var messages = new Mock<IDispatcherConversationMessagesRepository>();
			messages.Setup(x => x.InsertAsync(It.IsAny<DispatcherConversationMessage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DispatcherConversationMessage m, CancellationToken c, bool f) => m);
			var written = new List<int>();
			var attempts = 0;
			messages.Setup(x => x.UpdateAsync(It.IsAny<DispatcherConversationMessage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Returns((DispatcherConversationMessage m, CancellationToken c, bool f) =>
				{
					written.Add(m.DeliveryState);
					if (++attempts == 1)
						throw new InvalidOperationException("db blip");
					return Task.FromResult(m);
				});

			var result = await Conversations(messages.Object).SendReplyAsync(Dept, ItemId, Me, "On our way");

			result.Success.Should().BeTrue();
			written.Should().Equal(new[] { (int)DispatcherMessageDeliveryStates.Sent, (int)DispatcherMessageDeliveryStates.Sent },
				"the provider's answer is written again; the text is never sent again for it");
			_texts.Verify(x => x.SendTextMessage(It.IsAny<string>(), "On our way", It.IsAny<string>(), It.IsAny<MobileCarriers>(), Dept,
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
		}

		[Test]
		public async Task A_reply_is_not_sent_when_it_cannot_be_recorded()
		{
			AddItem(DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, Me);
			var messages = new Mock<IDispatcherConversationMessagesRepository>();
			messages.Setup(x => x.InsertAsync(It.IsAny<DispatcherConversationMessage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("db down"));

			Func<Task> act = () => Conversations(messages.Object).SendReplyAsync(Dept, ItemId, Me, "On our way");

			await act.Should().ThrowAsync<InvalidOperationException>();
			_texts.Verify(x => x.SendTextMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(), It.IsAny<int>(),
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Never);
		}
	}
}
