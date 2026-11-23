using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// PR #589 review: the routing service's races and recoveries. Two writers enqueuing or overflowing the same work, a dispatcher
	/// accepting a caller the worker is moving, a failed pending-call save or redirect, and stale snapshots written back.
	/// </summary>
	public partial class DispatcherDeskServiceTests
	{
		private static DispatcherQueueItem SeedItem(Harness h, DispatcherQueueItemKinds kind, DispatcherQueueItemStates state, int? callId = null,
			string assignedTo = null)
		{
			var item = new DispatcherQueueItem
			{
				DispatcherQueueItemId = Guid.NewGuid().ToString(), DepartmentId = Dept, Kind = (int)kind, State = (int)state, CallId = callId,
				AssignedUserId = assignedTo, SourceAddress = "+15551230001", CreatedOn = h.Now, LastActivityOn = h.Now
			};
			h.Store.Items.Add(item);
			return item;
		}

		[Test]
		public async Task The_sweep_limit_applies_after_calls_already_on_the_desk_are_left_out()
		{
			var h = new Harness();
			for (var i = 0; i <= DispatcherRoutingService.MaxSweepPerTick; i++)
			{
				var call = h.PendingCall(1000 + i);
				call.LoggedOn = h.Now.AddMinutes(-200 + i);
				// The oldest ones were handled on the desk already (their items are closed; the calls are still pending).
				if (i < DispatcherRoutingService.MaxSweepPerTick)
					SeedItem(h, DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Completed, call.CallId);
			}

			(await h.Routing.SyncCallsAsync(Dept, h.Now)).Should().Be(1);

			h.Store.Items.Where(x => x.IsOpen()).Select(x => x.CallId).Should().Equal(1000 + DispatcherRoutingService.MaxSweepPerTick);
		}

		[Test]
		public async Task A_pending_call_dispatched_before_its_event_was_handled_is_not_offered()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.PendingCall(100).State = (int)CallStates.Active;

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });

			result.Outcome.Should().Be(DispatcherRouteOutcomes.NotRouted);
			h.Store.Items.Should().BeEmpty();
			h.Pushes.Should().BeEmpty();
		}

		[Test]
		public async Task Two_writers_enqueuing_one_call_leave_one_open_item_and_the_loser_reads_the_winner()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.PendingCall(100);

			// The sweep's insert lands between the call listener's duplicate check and its own insert.
			DispatcherQueueItem winner = null;
			h.Store.BeforeItemInsert = item =>
			{
				if (winner != null)
					return;
				winner = SeedItem(h, DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Queued, 100);
			};

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Duplicate);
			result.Item.DispatcherQueueItemId.Should().Be(winner.DispatcherQueueItemId);
			h.Store.Items.Should().ContainSingle();
			h.Pushes.Should().BeEmpty("the losing insert offers nothing");
		}

		[Test]
		public async Task A_pending_call_an_overflow_made_is_its_items_call_even_before_the_two_are_linked()
		{
			var h = new Harness();
			var text = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Queued);
			var call = h.PendingCall(300);
			call.SourceIdentifier = "DQ:" + text.DispatcherQueueItemId;

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 300 });

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Duplicate);
			result.Item.DispatcherQueueItemId.Should().Be(text.DispatcherQueueItemId);
			h.Store.Items.Should().ContainSingle();
		}

		[Test]
		public async Task A_failed_pending_call_save_is_not_acknowledged_as_passed_on_and_the_worker_retries_it()
		{
			var h = new Harness();
			h.FailCallSaves = 1;

			var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230002", TargetAddress = "+15550000000", Body = "Smoke on Elm", ProviderMessageId = "SM9"
			});

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Queued);
			var stored = h.Item(result.Item.DispatcherQueueItemId);
			stored.CallId.Should().BeNull();
			stored.OverflowedOn.Should().BeNull("the claim is given back so the overflow can run again");
			h.Store.Messages.Where(x => x.Direction == (int)DispatcherMessageDirections.Outbound).Select(x => x.Body)
				.Should().ContainSingle().Which.Should().NotContain("passed it to the dispatchers");

			await h.Routing.ProcessTimeoutsAsync(h.Now);

			stored = h.Item(result.Item.DispatcherQueueItemId);
			stored.CallId.Should().HaveValue();
			h.Calls.Values.Where(x => x.SourceIdentifier == "DQ:" + stored.DispatcherQueueItemId).Should().ContainSingle();
		}

		[Test]
		public async Task An_overflow_pass_that_loses_the_claim_saves_no_second_pending_call()
		{
			var h = new Harness();

			// Another pass (the worker's timeout pass) claims the text first.
			var claimedElsewhere = false;
			h.Store.BeforeItemUpdate = incoming =>
			{
				if (claimedElsewhere || incoming.OverflowedOn == null || incoming.CallId.HasValue)
					return;
				claimedElsewhere = true;
				var row = h.Item(incoming.DispatcherQueueItemId);
				row.OverflowedOn = h.Now;
				row.Version++;
			};

			var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230003", TargetAddress = "+15550000000", Body = "Water rising", ProviderMessageId = "SM10"
			});

			claimedElsewhere.Should().BeTrue();
			result.Outcome.Should().Be(DispatcherRouteOutcomes.Queued);
			h.Calls.Values.Should().NotContain(x => x.SourceIdentifier != null && x.SourceIdentifier.StartsWith("DQ:"));
		}

		private static async Task<DispatcherQueueItem> HeldCallerAsync(Harness h)
		{
			h.Config.VoiceHoldSeconds = 60;
			var held = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Voice, SourceAddress = "+15551230004", TargetAddress = "+15550000000", ExternalReference = "CA-held"
			});
			held.Outcome.Should().Be(DispatcherRouteOutcomes.Queued);
			h.Now = h.Now.AddMinutes(2);
			return held.Item;
		}

		[Test]
		public async Task A_held_caller_the_provider_could_not_move_stays_on_the_desk_and_is_not_recorded_as_gone()
		{
			var h = new Harness();
			var item = await HeldCallerAsync(h);

			await h.Routing.ProcessTimeoutsAsync(h.Now);

			var stored = h.Item(item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Queued, "a dispatcher can still take it or call back");
			stored.OverflowNote.Should().Contain("could not be sent").And.NotContain("had left");
			h.Store.Events.Should().Contain(x => x.DispatcherQueueItemId == item.DispatcherQueueItemId && x.EventType == (int)DispatcherQueueEventTypes.Reopened);

			await h.Routing.ProcessTimeoutsAsync(h.Now.AddMinutes(1));
			h.Voice.Verify(x => x.RedirectCallAsync("CA-held", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
				"the worker does not keep redirecting; the caller's own hold loop takes a message");
		}

		[Test]
		public async Task A_held_caller_sent_to_the_voice_overflow_leaves_the_desk()
		{
			var h = new Harness();
			var item = await HeldCallerAsync(h);
			h.Voice.Setup(x => x.RedirectCallAsync("CA-held", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			await h.Routing.ProcessTimeoutsAsync(h.Now);

			var stored = h.Item(item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Overflowed);
			h.Store.Events.Should().Contain(x => x.DispatcherQueueItemId == item.DispatcherQueueItemId && x.EventType == (int)DispatcherQueueEventTypes.Overflowed &&
												  x.Detail.Contains("Caller sent to the voice overflow"));
		}

		[Test]
		public async Task A_caller_a_dispatcher_accepted_before_the_overflow_claim_is_never_redirected()
		{
			var h = new Harness();
			var item = await HeldCallerAsync(h);

			// The accept lands between the worker's read and its claim.
			h.Store.BeforeItemUpdate = incoming =>
			{
				if (incoming.State != (int)DispatcherQueueItemStates.Overflowed)
					return;
				var row = h.Item(incoming.DispatcherQueueItemId);
				if (row.State != (int)DispatcherQueueItemStates.Queued)
					return;
				row.State = (int)DispatcherQueueItemStates.Accepted;
				row.AssignedUserId = "ann";
				row.Version++;
			};

			await h.Routing.ProcessTimeoutsAsync(h.Now);

			h.Voice.Verify(x => x.RedirectCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			var stored = h.Item(item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			stored.AssignedUserId.Should().Be("ann");
		}

		[Test]
		public async Task A_late_activity_write_never_moves_the_last_activity_back()
		{
			var h = new Harness();
			var item = SeedItem(h, DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			item.LastActivityOn = h.Now.AddMinutes(5);

			await h.Routing.RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceConnected, "ann", "Connected");

			h.Item(item.DispatcherQueueItemId).LastActivityOn.Should().Be(h.Now.AddMinutes(5));
		}

		[Test]
		public async Task Only_the_holder_a_supervisor_or_anyone_on_queued_work_links_a_call_and_the_calls_own_item_is_covered()
		{
			var h = new Harness();
			var held = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			h.PendingCall(400);
			var callsOwn = SeedItem(h, DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Offered, 400, "bob");

			(await h.Routing.LinkCallAsync(Dept, held.DispatcherQueueItemId, "bob", 400)).Error.Should().Be(DispatcherDeskErrors.NotAssignedToYou);
			h.Item(held.DispatcherQueueItemId).CallId.Should().BeNull();

			(await h.Routing.LinkCallAsync(Dept, held.DispatcherQueueItemId, "ann", 400)).Success.Should().BeTrue();
			h.Item(held.DispatcherQueueItemId).CallId.Should().Be(400);
			h.Item(callsOwn.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Completed);
			h.Item(callsOwn.DispatcherQueueItemId).Outcome.Should().Be((int)DispatcherQueueItemOutcomes.Duplicate);

			var queued = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Queued);
			h.PendingCall(401);
			(await h.Routing.LinkCallAsync(Dept, queued.DispatcherQueueItemId, "bob", 401)).Success.Should().BeTrue();

			h.Supervisors.Add("sup");
			var other = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			h.PendingCall(402);
			(await h.Routing.LinkCallAsync(Dept, other.DispatcherQueueItemId, "sup", 402)).Success.Should().BeTrue();
		}

		[Test]
		public async Task A_call_someone_else_is_working_on_the_desk_is_not_linked_to_a_second_item()
		{
			var h = new Harness();
			var held = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			h.PendingCall(400);
			SeedItem(h, DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Accepted, 400, "bob");

			var result = await h.Routing.LinkCallAsync(Dept, held.DispatcherQueueItemId, "ann", 400);

			result.Error.Should().Be(DispatcherDeskErrors.InvalidInput);
			h.Item(held.DispatcherQueueItemId).CallId.Should().BeNull();
		}

		[Test]
		public async Task A_fenced_overflow_writes_only_the_item_the_caller_read()
		{
			var h = new Harness();
			var seen = SeedItem(h, DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Queued);
			var snapshot = h.Item(seen.DispatcherQueueItemId);
			var callerRead = new DispatcherQueueItem { DispatcherQueueItemId = snapshot.DispatcherQueueItemId, State = snapshot.State, Version = snapshot.Version };

			// A dispatcher accepts after the hold loop read the item.
			snapshot.State = (int)DispatcherQueueItemStates.Accepted;
			snapshot.AssignedUserId = "ann";
			snapshot.Version++;

			(await h.Routing.TryMarkOverflowedAsync(callerRead, "Not connected after 600 seconds on hold: voicemail")).Should().BeFalse();
			h.Item(seen.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			h.Item(seen.DispatcherQueueItemId).AssignedUserId.Should().Be("ann");
			callerRead.State.Should().Be((int)DispatcherQueueItemStates.Accepted, "the caller's copy is refreshed");

			// Unchanged since the read: overflowed.
			(await h.Routing.TryMarkOverflowedAsync(callerRead, "Hold over: voicemail")).Should().BeTrue();
			h.Item(seen.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Overflowed);
		}

		private DispatcherRoutingService RoutingWithConversations(Harness h, IDispatcherConversationService conversations, Mock<IDispatcherDeskNotifier> notifier)
		{
			var config = new Mock<IDispatcherDeskConfigService>();
			config.Setup(x => x.GetConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => h.Config);
			return new DispatcherRoutingService(h.Store.ItemsRepository, h.Store.EventsRepository, h.Store.MessagesRepository, h.Store.PresencesRepository,
				h.Store.BreaksRepository, h.Presence, config.Object, notifier.Object, h.Voice.Object, new Mock<IDepartmentsService>().Object,
				new Mock<IUserProfileService>().Object, h.Queue.Object, new Lazy<ICallsService>(() => new Mock<ICallsService>().Object),
				new Lazy<IDispatcherConversationService>(() => conversations), new Lazy<IAiDispatchEnrichmentService>(() => h.Ai.Object))
			{ UtcNow = () => h.Now };
		}

		[Test]
		public async Task A_concurrent_duplicate_delivery_joining_a_conversation_is_announced_once()
		{
			var h = new Harness();
			var open = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			open.ConversationKey = DispatcherRoutingPolicy.ConversationKey(DispatcherQueueItemKinds.Text, "+15551230007");

			// The other delivery stored the message first: nothing is recorded for this one.
			var conversations = new Mock<IDispatcherConversationService>();
			conversations.Setup(x => x.RecordInboundAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DispatcherConversationMessage)null);
			var notifier = new Mock<IDispatcherDeskNotifier>();
			var routing = RoutingWithConversations(h, conversations.Object, notifier);

			var result = await routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230007", TargetAddress = "+15550000000", Body = "Still here", ProviderMessageId = "SM-race"
			});

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Duplicate);
			result.Item.DispatcherQueueItemId.Should().Be(open.DispatcherQueueItemId);
			h.Store.Events.Should().NotContain(x => x.EventType == (int)DispatcherQueueEventTypes.MessageReceived);
			notifier.Verify(x => x.NotifyMessageAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_concurrent_duplicate_first_delivery_closes_its_own_new_item()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var original = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Queued);
			// The other delivery stores the message after this one's provider-id check, before this one records it.
			h.Store.BeforeItemInsert = item => h.Store.Messages.Add(new DispatcherConversationMessage
			{
				DispatcherConversationMessageId = Guid.NewGuid().ToString(), DepartmentId = Dept, DispatcherQueueItemId = original.DispatcherQueueItemId,
				Direction = (int)DispatcherMessageDirections.Inbound, Body = "Fire", ProviderMessageId = "SM-first", CreatedOn = h.Now
			});

			var conversations = new Mock<IDispatcherConversationService>();
			conversations.Setup(x => x.RecordInboundAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DispatcherConversationMessage)null);
			var notifier = new Mock<IDispatcherDeskNotifier>();
			var routing = RoutingWithConversations(h, conversations.Object, notifier);

			// Both deliveries passed the provider-id check before either stored the message (EnqueueAsync is where that one lands).
			var result = await routing.EnqueueAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230008", TargetAddress = "+15550000000", Body = "Fire", ProviderMessageId = "SM-first"
			});

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Duplicate);
			result.Item.DispatcherQueueItemId.Should().Be(original.DispatcherQueueItemId);
			h.Store.Items.Where(x => x.IsOpen()).Should().ContainSingle(x => x.DispatcherQueueItemId == original.DispatcherQueueItemId);
			notifier.Verify(x => x.NotifyOfferAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_offer_does_not_undo_a_break_started_after_the_routing_pass_read_the_dispatcher()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.PendingCall(100);

			// Ann starts a break while the offer is being written.
			h.Store.BeforeItemUpdate = incoming =>
			{
				if (incoming.State == (int)DispatcherQueueItemStates.Offered)
					h.PresenceOf("ann").State = (int)DispatcherPresenceStates.OnBreak;
			};

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Offered);
			h.PresenceOf("ann").State.Should().Be((int)DispatcherPresenceStates.OnBreak);
			h.PresenceOf("ann").LastOfferedOn.Should().Be(h.Now);
		}
	}
}
