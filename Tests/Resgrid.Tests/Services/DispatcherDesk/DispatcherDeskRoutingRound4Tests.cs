using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// PR #589 review, round 4: an intake connect's compensation against a reconnect of the same dispatcher and an activity-only
	/// version change, and a stalled overflow pass whose call the newer claim may reuse.
	/// </summary>
	public partial class DispatcherDeskServiceTests
	{
		[Test]
		public async Task A_connect_compensation_that_loses_to_a_reconnect_of_the_same_dispatcher_takes_the_item_back_for_them()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(342);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			conversation.AssignedUserId = "carol";

			// carol's release commits in intake while her connect callback writes the accept; she connects again while the callback puts
			// the item back (that connect's own callback found the item still accepted for her and left it).
			var step = 0;
			h.Store.BeforeItemUpdate = incoming =>
			{
				if (step == 0 && incoming.State == (int)DispatcherQueueItemStates.Accepted && incoming.AssignedUserId == "carol")
				{
					step = 1;
					conversation.AssignedUserId = null;
				}
				else if (step == 1 && incoming.State == (int)DispatcherQueueItemStates.Queued)
				{
					step = 2;
					conversation.AssignedUserId = "carol";
				}
			};

			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "carol");

			var stored = h.Item(item.DispatcherQueueItemId);
			step.Should().Be(2);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Accepted, "intake has carol on the thread again");
			stored.AssignedUserId.Should().Be("carol");
		}

		[Test]
		public async Task A_connect_compensation_reads_through_a_message_that_touched_the_item_and_still_returns_it()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(343);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			conversation.AssignedUserId = "carol";

			var step = 0;
			h.Store.BeforeItemUpdate = incoming =>
			{
				if (step == 0 && incoming.State == (int)DispatcherQueueItemStates.Accepted && incoming.AssignedUserId == "carol")
				{
					step = 1;
					conversation.AssignedUserId = null;
				}
				else if (step == 1 && incoming.State == (int)DispatcherQueueItemStates.Queued)
				{
					// The texter's message lands first: only the item's activity (and version) changes.
					step = 2;
					var row = h.Item(incoming.DispatcherQueueItemId);
					row.LastActivityOn = h.Now;
					row.Version++;
				}
			};

			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "carol");

			var stored = h.Item(item.DispatcherQueueItemId);
			step.Should().Be(2);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Queued, "intake has nobody on the thread");
			stored.AssignedUserId.Should().BeNull();
			h.Store.Events.Should().Contain(x => x.DispatcherQueueItemId == item.DispatcherQueueItemId && x.EventType == (int)DispatcherQueueEventTypes.Released);
		}

		[Test]
		public async Task A_connect_compensation_never_returns_a_newer_accept_of_the_item()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(344);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			conversation.AssignedUserId = "carol";
			var newerAccept = h.Now.AddSeconds(30);

			var step = 0;
			h.Store.BeforeItemUpdate = incoming =>
			{
				if (step == 0 && incoming.State == (int)DispatcherQueueItemStates.Accepted && incoming.AssignedUserId == "carol")
				{
					step = 1;
					conversation.AssignedUserId = null;
				}
				else if (step == 1 && incoming.State == (int)DispatcherQueueItemStates.Queued)
				{
					// carol takes the item again on the desk before the return lands (a new accept, not the callback's).
					step = 2;
					var row = h.Item(incoming.DispatcherQueueItemId);
					row.AcceptedOn = newerAccept;
					row.Version++;
				}
			};

			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "carol");

			var stored = h.Item(item.DispatcherQueueItemId);
			step.Should().Be(2);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			stored.AssignedUserId.Should().Be("carol");
			stored.AcceptedOn.Should().Be(newerAccept);
			h.Store.Events.Should().NotContain(x => x.DispatcherQueueItemId == item.DispatcherQueueItemId && x.EventType == (int)DispatcherQueueEventTypes.Released);
		}

		[Test]
		public async Task A_stalled_overflow_pass_that_lost_its_claim_to_a_pass_with_no_call_yet_leaves_its_call_for_that_pass()
		{
			var h = new Harness();
			var stalled = true;

			// While the first pass saves, the worker gave its claim back and a second pass claimed the item: it has no call linked yet.
			h.BeforeCallSave = saving =>
			{
				if (!stalled || saving.SourceIdentifier == null || !saving.SourceIdentifier.StartsWith("DQ:") || saving.CallId != 0)
					return;
				stalled = false;
				var row = h.Item(ItemIdOf(saving));
				row.OverflowedOn = h.Now.AddMinutes(-(DispatcherRoutingService.StrandedOverflowClaimMinutes + 1));
				row.Version++;
			};

			var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230040", TargetAddress = "+15550000000", Body = "Gas smell", ProviderMessageId = "SM40"
			});

			var itemId = result.Item.DispatcherQueueItemId;
			var source = "DQ:" + itemId;
			var saved = h.Calls.Values.Single(x => x.SourceIdentifier == source);
			saved.IsDeleted.Should().BeFalse("the newer claim may have found this call and be linking it");
			h.Item(itemId).CallId.Should().BeNull();

			// The newer pass (here the worker's recovery of its claim) links the one call there is.
			await h.Routing.ProcessTimeoutsAsync(h.Now);

			h.Item(itemId).CallId.Should().Be(saved.CallId);
			saved.IsDeleted.Should().BeFalse();
		}

		[Test]
		public async Task The_call_an_overflow_links_takes_back_a_duplicate_a_stalled_pass_left_for_it()
		{
			var h = new Harness();
			var stalled = true;
			h.BeforeCallSave = saving =>
			{
				if (!stalled || saving.SourceIdentifier == null || !saving.SourceIdentifier.StartsWith("DQ:") || saving.CallId != 0)
					return;
				stalled = false;
				var row = h.Item(ItemIdOf(saving));
				row.OverflowedOn = h.Now.AddMinutes(-(DispatcherRoutingService.StrandedOverflowClaimMinutes + 1));
				row.Version++;
			};

			var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230041", TargetAddress = "+15550000000", Body = "Tree on car", ProviderMessageId = "SM41"
			});

			var itemId = result.Item.DispatcherQueueItemId;
			var source = "DQ:" + itemId;
			var left = h.Calls.Values.Single(x => x.SourceIdentifier == source);

			// The newer pass had saved its own call before the stalled one committed: that call is the one linked.
			h.Calls[4000] = new Call { CallId = 4000, DepartmentId = Dept, State = (int)CallStates.Pending, SourceIdentifier = source, LoggedOn = h.Now };

			await h.Routing.ProcessTimeoutsAsync(h.Now);

			h.Item(itemId).CallId.Should().Be(4000);
			left.IsDeleted.Should().BeTrue("the stalled pass's call is the duplicate");
			h.Calls[4000].IsDeleted.Should().BeFalse();
		}
	}
}
