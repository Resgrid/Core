using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// PR #589 review, round 2: a new caller's overflow that loses to an accept, call items offered for a call that no longer waits, a
	/// call link that must not close the call's own item on its own, overflow calls saved but never linked, and stale intake connects.
	/// </summary>
	public partial class DispatcherDeskServiceTests
	{
		[Test]
		public async Task A_new_caller_a_dispatcher_takes_while_the_overflow_is_written_is_held_for_that_dispatcher()
		{
			var h = new Harness();
			h.Config.VoiceHoldSeconds = 0;

			// The accept lands between the inbound path publishing the queued caller and its overflow write.
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

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Voice, SourceAddress = "+15551230020", TargetAddress = "+15550000000", ExternalReference = "CA-new"
			});

			result.Outcome.Should().NotBe(DispatcherRouteOutcomes.Overflow, "the webhook holds the caller for the dispatcher who took them");
			result.UserId.Should().Be("ann");
			var stored = h.Item(result.Item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			stored.AssignedUserId.Should().Be("ann");
		}

		[Test]
		public async Task A_new_caller_nobody_can_take_still_goes_to_the_overflow()
		{
			var h = new Harness();
			h.Config.VoiceHoldSeconds = 0;

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Voice, SourceAddress = "+15551230021", TargetAddress = "+15550000000", ExternalReference = "CA-alone"
			});

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Overflow);
			h.Item(result.Item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Overflowed);
		}

		[TestCase(DispatcherQueueItemKinds.Chat)]
		[TestCase(DispatcherQueueItemKinds.Email)]
		public async Task A_chat_or_email_whose_call_no_longer_waits_is_not_put_on_the_desk(DispatcherQueueItemKinds kind)
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.PendingCall(600).State = (int)CallStates.Active;
			h.PendingCall(601).State = (int)CallStates.Closed;

			(await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = kind, CallId = 600 })).Outcome.Should().Be(DispatcherRouteOutcomes.NotRouted);
			(await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = kind, CallId = 601 })).Outcome.Should().Be(DispatcherRouteOutcomes.NotRouted);
			h.Store.Items.Should().BeEmpty();

			h.PendingCall(602);
			(await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = kind, CallId = 602 })).Outcome.Should().Be(DispatcherRouteOutcomes.Offered);
		}

		[Test]
		public async Task A_call_link_that_loses_to_a_concurrent_write_rolls_back_closing_the_calls_own_item()
		{
			var h = new Harness();
			var held = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			h.PendingCall(400);
			var callsOwn = SeedItem(h, DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Queued, 400);

			// A message touches the item being linked between its read and the link write.
			h.Store.BeforeStoredItemUpdate = stored =>
			{
				if (stored.DispatcherQueueItemId == held.DispatcherQueueItemId)
					stored.Version++;
			};

			var result = await h.Routing.LinkCallAsync(Dept, held.DispatcherQueueItemId, "ann", 400);

			result.Success.Should().BeFalse();
			result.Error.Should().Be(DispatcherDeskErrors.ItemNotAvailable);
			h.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Once, "the call's own item closing is rolled back with the failed link");
			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Never);
			h.Store.Events.Should().NotContain(x => x.DispatcherQueueItemId == callsOwn.DispatcherQueueItemId && x.EventType == (int)DispatcherQueueEventTypes.Completed,
				"nothing is announced for a change that did not commit");
		}

		[Test]
		public async Task A_call_link_commits_the_covered_item_and_the_link_together_and_announces_both_after()
		{
			var h = new Harness();
			var held = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			h.PendingCall(401);
			var callsOwn = SeedItem(h, DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Queued, 401);

			(await h.Routing.LinkCallAsync(Dept, held.DispatcherQueueItemId, "ann", 401)).Success.Should().BeTrue();

			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Once);
			h.Item(held.DispatcherQueueItemId).CallId.Should().Be(401);
			h.Item(callsOwn.DispatcherQueueItemId).Outcome.Should().Be((int)DispatcherQueueItemOutcomes.Duplicate);
			h.Store.Events.Should().Contain(x => x.DispatcherQueueItemId == callsOwn.DispatcherQueueItemId && x.EventType == (int)DispatcherQueueEventTypes.Completed);
		}

		[Test]
		public async Task A_pending_call_saved_before_its_save_failed_is_linked_on_the_retry_not_saved_again()
		{
			var h = new Harness();
			h.FailAfterCallSaves = 1;

			var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230022", TargetAddress = "+15550000000", Body = "Car in ditch", ProviderMessageId = "SM20"
			});

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Queued, "the save reported a failure");
			var stored = h.Item(result.Item.DispatcherQueueItemId);
			stored.CallId.Should().BeNull();
			stored.OverflowedOn.Should().BeNull("the claim was given back");

			await h.Routing.ProcessTimeoutsAsync(h.Now);

			var source = "DQ:" + stored.DispatcherQueueItemId;
			h.Calls.Values.Where(x => x.SourceIdentifier == source).Should().ContainSingle("the retry found the call the failed save had committed");
			h.Item(stored.DispatcherQueueItemId).CallId.Should().Be(h.Calls.Values.Single(x => x.SourceIdentifier == source).CallId);
		}

		[Test]
		public async Task An_overflow_call_whose_link_kept_losing_is_linked_by_a_later_worker_pass()
		{
			var h = new Harness();

			// Every link write loses to a touch (a new message each time), so the overflow saves its call and cannot link it.
			var losing = true;
			h.Store.BeforeStoredItemUpdate = stored =>
			{
				if (losing && h.Calls.Values.Any(x => x.SourceIdentifier == "DQ:" + stored.DispatcherQueueItemId))
					stored.Version++;
			};

			var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230023", TargetAddress = "+15550000000", Body = "Tree on wires", ProviderMessageId = "SM21"
			});

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Overflow, "the pending call exists: the texter is told it was passed on");
			var stored = h.Item(result.Item.DispatcherQueueItemId);
			stored.CallId.Should().BeNull();
			stored.OverflowedOn.Should().HaveValue("the claim stays, so no second call is saved");

			losing = false;
			await h.Routing.ProcessTimeoutsAsync(h.Now.AddMinutes(1));
			h.Item(stored.DispatcherQueueItemId).CallId.Should().BeNull("a claim this young may still be an overflow in progress");

			await h.Routing.ProcessTimeoutsAsync(h.Now.AddMinutes(DispatcherRoutingService.StrandedOverflowClaimMinutes + 1));

			var calls = h.Calls.Values.Where(x => x.SourceIdentifier == "DQ:" + stored.DispatcherQueueItemId).ToList();
			calls.Should().ContainSingle();
			h.Item(stored.DispatcherQueueItemId).CallId.Should().Be(calls[0].CallId);
		}

		[Test]
		public async Task A_stranded_overflow_claim_with_no_call_saved_is_given_back_so_the_overflow_runs_again()
		{
			var h = new Harness();
			var text = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Queued);
			text.OverflowedOn = h.Now.AddMinutes(-(DispatcherRoutingService.StrandedOverflowClaimMinutes + 5));
			text.OverflowNote = "No dispatcher was free. Saving it as a pending call.";

			await h.Routing.ProcessTimeoutsAsync(h.Now);
			h.Item(text.DispatcherQueueItemId).OverflowedOn.Should().BeNull();

			await h.Routing.ProcessTimeoutsAsync(h.Now);
			h.Item(text.DispatcherQueueItemId).CallId.Should().HaveValue("the next pass made the pending call");
			h.Calls.Values.Where(x => x.SourceIdentifier == "DQ:" + text.DispatcherQueueItemId).Should().ContainSingle();
		}

		[Test]
		public async Task An_insert_race_finds_its_departments_winner_whatever_another_department_holds_under_the_same_reference()
		{
			var h = new Harness();
			var winner = SeedItem(h, DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Queued);
			winner.ExternalReference = "REF-1";
			var elsewhere = SeedItem(h, DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Completed);
			elsewhere.DepartmentId = Dept + 1;
			elsewhere.ExternalReference = "REF-1";
			elsewhere.CreatedOn = h.Now.AddMinutes(1);

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Voice, SourceAddress = "+15551230024", TargetAddress = "+15550000000", ExternalReference = "REF-1"
			});

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Duplicate);
			result.Item.DispatcherQueueItemId.Should().Be(winner.DispatcherQueueItemId);
		}

		[Test]
		public async Task A_connect_callback_that_arrives_after_a_newer_takeover_does_not_name_the_older_dispatcher()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(330);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;

			// carol's connect committed in intake first; dave took the thread over and his callback reached the desk before carol's.
			conversation.AssignedUserId = "dave";
			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "dave");
			h.Item(item.DispatcherQueueItemId).AssignedUserId.Should().Be("dave");

			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "carol");

			h.Item(item.DispatcherQueueItemId).AssignedUserId.Should().Be("dave", "intake routes the thread to dave");
			h.Item(item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Accepted);
		}
	}
}
