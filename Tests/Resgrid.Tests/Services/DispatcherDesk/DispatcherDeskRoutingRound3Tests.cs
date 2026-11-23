using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// PR #589 review, round 3: an overflow pass that stalls past the stranded-claim window and resumes after another pass overflowed
	/// the item, overflow calls left unlinked on an item a dispatcher took, an intake connect that loses to a release, and an emailed call
	/// put on the desk before its sender was known.
	/// </summary>
	public partial class DispatcherDeskServiceTests
	{
		private static string ItemIdOf(Call call) => call.SourceIdentifier.Substring("DQ:".Length);

		[Test]
		public async Task A_stalled_overflow_pass_that_resumes_after_another_pass_linked_its_call_takes_its_own_call_back()
		{
			var h = new Harness();
			var stalled = true;

			// While the first pass saves, the worker gave its claim back and a second pass saved and linked the item's call.
			h.BeforeCallSave = saving =>
			{
				if (!stalled || saving.SourceIdentifier == null || !saving.SourceIdentifier.StartsWith("DQ:") || saving.CallId != 0)
					return;
				stalled = false;
				var row = h.Item(ItemIdOf(saving));
				h.Calls[7000] = new Call { CallId = 7000, DepartmentId = Dept, State = (int)CallStates.Pending, SourceIdentifier = saving.SourceIdentifier, LoggedOn = h.Now };
				row.CallId = 7000;
				row.OverflowedOn = h.Now.AddMinutes(DispatcherRoutingService.StrandedOverflowClaimMinutes + 1);
				row.Version++;
			};

			var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230030", TargetAddress = "+15550000000", Body = "Wires down", ProviderMessageId = "SM30"
			});

			var source = "DQ:" + result.Item.DispatcherQueueItemId;
			h.Item(result.Item.DispatcherQueueItemId).CallId.Should().Be(7000, "the other pass's call is the item's");
			h.Calls.Values.Where(x => x.SourceIdentifier == source && !x.IsDeleted).Should().ContainSingle().Which.CallId.Should().Be(7000);
			h.Calls.Values.Should().Contain(x => x.SourceIdentifier == source && x.CallId != 7000 && x.IsDeleted, "the stalled pass's duplicate is taken back");
		}

		[Test]
		public async Task A_stalled_overflow_pass_whose_save_fails_never_gives_back_a_newer_passs_claim()
		{
			var h = new Harness();
			h.FailCallSaves = 1;
			var newerClaim = h.Now.AddMinutes(DispatcherRoutingService.StrandedOverflowClaimMinutes + 1);

			// The first pass's claim was given back while it saved, and a second pass claimed the item (it is saving its own call).
			h.BeforeCallSave = saving =>
			{
				if (h.FailCallSaves == 0 || saving.SourceIdentifier == null || !saving.SourceIdentifier.StartsWith("DQ:"))
					return;
				var row = h.Item(ItemIdOf(saving));
				row.OverflowedOn = newerClaim;
				row.Version++;
			};

			var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230031", TargetAddress = "+15550000000", Body = "Smoke", ProviderMessageId = "SM31"
			});

			var stored = h.Item(result.Item.DispatcherQueueItemId);
			stored.OverflowedOn.Should().Be(newerClaim, "only the failed pass's own claim is given back");
			stored.CallId.Should().BeNull();
		}

		[Test]
		public async Task An_overflow_call_left_unlinked_on_an_item_a_dispatcher_took_is_linked_without_taking_it_from_them()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			var text = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			text.OverflowedOn = h.Now.AddMinutes(-(DispatcherRoutingService.StrandedOverflowClaimMinutes + 5));
			text.OverflowNote = "No dispatcher was free. Saving it as a pending call.";
			h.Calls[7001] = new Call { CallId = 7001, DepartmentId = Dept, State = (int)CallStates.Pending, SourceIdentifier = "DQ:" + text.DispatcherQueueItemId, LoggedOn = h.Now };

			await h.Routing.ProcessTimeoutsAsync(h.Now);

			var stored = h.Item(text.DispatcherQueueItemId);
			stored.CallId.Should().Be(7001);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			stored.AssignedUserId.Should().Be("ann");
		}

		[Test]
		public async Task A_stale_overflow_claim_on_an_item_a_dispatcher_took_with_no_call_saved_is_cleared_and_never_overflows()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			var text = SeedItem(h, DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Accepted, assignedTo: "ann");
			text.OverflowedOn = h.Now.AddMinutes(-(DispatcherRoutingService.StrandedOverflowClaimMinutes + 5));

			await h.Routing.ProcessTimeoutsAsync(h.Now);
			await h.Routing.ProcessTimeoutsAsync(h.Now);

			var stored = h.Item(text.DispatcherQueueItemId);
			stored.OverflowedOn.Should().BeNull();
			stored.CallId.Should().BeNull();
			stored.State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			stored.AssignedUserId.Should().Be("ann");
			h.Calls.Values.Should().NotContain(x => x.SourceIdentifier == "DQ:" + text.DispatcherQueueItemId, "an item a dispatcher has is never overflowed");
		}

		[Test]
		public async Task A_connect_whose_thread_intake_released_before_the_desk_write_goes_back_to_the_queue()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(340);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			conversation.AssignedUserId = "carol";

			// carol's release commits in intake while her connect callback is writing; the release callback found the item queued.
			var released = false;
			h.Store.BeforeItemUpdate = incoming =>
			{
				if (released || incoming.State != (int)DispatcherQueueItemStates.Accepted || incoming.AssignedUserId != "carol")
					return;
				released = true;
				conversation.AssignedUserId = null;
			};

			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "carol");

			var stored = h.Item(item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Queued, "intake has nobody on the thread");
			stored.AssignedUserId.Should().BeNull();
			h.Store.Events.Should().Contain(x => x.DispatcherQueueItemId == item.DispatcherQueueItemId && x.EventType == (int)DispatcherQueueEventTypes.Released);
		}

		[Test]
		public async Task A_connect_intake_still_holds_after_the_write_stays_accepted()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(341);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			conversation.AssignedUserId = "carol";

			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "carol");

			var stored = h.Item(item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			stored.AssignedUserId.Should().Be("carol");
			h.Store.Events.Should().NotContain(x => x.DispatcherQueueItemId == item.DispatcherQueueItemId && x.EventType == (int)DispatcherQueueEventTypes.Released);
		}

		[Test]
		public async Task An_emailed_call_the_call_event_put_on_the_desk_first_gets_its_sender_from_the_email_hand_off()
		{
			var h = new Harness();
			h.PendingCall(350).CallSource = (int)CallSources.EmailImport;

			// The call-event hand-off (or the sweep) knows only the call.
			var first = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Email, CallId = 350 });
			h.Item(first.Item.DispatcherQueueItemId).SourceAddress.Should().BeNull();

			var handOff = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Email, CallId = 350, SourceAddress = " cad@county.example ", Subject = "ACTIVE 9-1-1"
			});

			handOff.Outcome.Should().Be(DispatcherRouteOutcomes.Duplicate);
			var stored = h.Item(first.Item.DispatcherQueueItemId);
			stored.SourceAddress.Should().Be("cad@county.example");
			stored.ConversationKey.Should().NotBeNullOrWhiteSpace("the sender's answer joins the item");
			DispatcherConversationService.CanReply(stored).Should().BeTrue();
		}

		[Test]
		public async Task An_email_item_that_already_has_a_sender_keeps_it()
		{
			var h = new Harness();
			h.PendingCall(351).CallSource = (int)CallSources.EmailImport;
			var first = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Email, CallId = 351, SourceAddress = "cad@county.example"
			});

			await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Email, CallId = 351, SourceAddress = "other@county.example" });

			h.Item(first.Item.DispatcherQueueItemId).SourceAddress.Should().Be("cad@county.example");
		}
	}
}
