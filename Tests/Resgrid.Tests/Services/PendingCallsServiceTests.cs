using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class PendingCallsServiceTests
	{
		private Mock<ICallsService> _callsService;
		private Mock<IQueueService> _queueService;
		private Mock<IUserProfileService> _userProfileService;
		private Mock<ICallDispatchStatusService> _callDispatchStatusService;
		private Mock<IFeatureToggleService> _featureToggleService;
		private Mock<IDispatchRecommendationService> _dispatchRecommendationService;
		private Mock<IEventAggregator> _eventAggregator;
		private PendingCallsService _service;
		private static readonly DateTime ClaimedOn = new DateTime(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc);

		[SetUp]
		public void SetUp()
		{
			_callsService = new Mock<ICallsService>();
			_queueService = new Mock<IQueueService>();
			_userProfileService = new Mock<IUserProfileService>();
			_callDispatchStatusService = new Mock<ICallDispatchStatusService>();
			_featureToggleService = new Mock<IFeatureToggleService>();
			_dispatchRecommendationService = new Mock<IDispatchRecommendationService>();
			_eventAggregator = new Mock<IEventAggregator>();

			_callsService.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call c, CancellationToken _) => c);
			_callsService.Setup(x => x.TryClaimCallForDispatchAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ClaimedOn);
			_callsService.Setup(x => x.ReleaseCallDispatchClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_callsService.Setup(x => x.CompleteCallDispatchClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_queueService.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_userProfileService.Setup(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>()))
				.ReturnsAsync(new List<UserProfile>());
			_userProfileService.Setup(x => x.GetAllProfilesForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Dictionary<string, UserProfile>());

			_service = new PendingCallsService(_callsService.Object, _queueService.Object, _userProfileService.Object,
				_callDispatchStatusService.Object, _featureToggleService.Object, _dispatchRecommendationService.Object, _eventAggregator.Object);
		}

		private static Call PendingCall(bool withRecipient = true)
		{
			var call = new Call
			{
				CallId = 42,
				DepartmentId = 7,
				State = (int)CallStates.Pending,
				Dispatches = new Collection<CallDispatch>(),
				GroupDispatches = new List<CallDispatchGroup>(),
				RoleDispatches = new List<CallDispatchRole>(),
				UnitDispatches = new List<CallDispatchUnit>()
			};

			if (withRecipient)
				call.Dispatches.Add(new CallDispatch { CallId = 42, UserId = "user-1" });

			return call;
		}

		[Test]
		public void A_pending_call_and_an_unsent_scheduled_call_are_waiting_for_dispatch()
		{
			_service.IsWaitingForDispatch(PendingCall()).Should().BeTrue();
			_service.IsWaitingForDispatch(new Call { State = (int)CallStates.Active, DispatchOn = DateTime.UtcNow.AddHours(1), HasBeenDispatched = false })
				.Should().BeTrue();
		}

		[Test]
		public void A_live_closed_or_deleted_call_is_not_waiting_for_dispatch()
		{
			_service.IsWaitingForDispatch(new Call { State = (int)CallStates.Active }).Should().BeFalse("an ordinary active call has already gone out");
			_service.IsWaitingForDispatch(new Call { State = (int)CallStates.Active, DispatchOn = DateTime.UtcNow, HasBeenDispatched = true }).Should().BeFalse();
			_service.IsWaitingForDispatch(new Call { State = (int)CallStates.Cancelled, DispatchOn = DateTime.UtcNow.AddHours(1), HasBeenDispatched = false }).Should().BeFalse();
			_service.IsWaitingForDispatch(new Call { State = (int)CallStates.Pending, IsDeleted = true }).Should().BeFalse();
			_service.IsWaitingForDispatch(null).Should().BeFalse();
		}

		[Test]
		public async Task Dispatching_a_pending_call_makes_it_active_queues_the_broadcast_and_announces_it_as_added()
		{
			var call = PendingCall();
			var before = DateTime.UtcNow;

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.Dispatched);
			call.State.Should().Be((int)CallStates.Active);
			call.HasBeenDispatched.Should().BeTrue();
			call.DispatchOn.Should().NotBeNull().And.BeOnOrAfter(before, "the dispatch time is recorded separately from when the call was received");
			_queueService.Verify(x => x.EnqueueCallBroadcastAsync(It.Is<CallQueueItem>(q => q.Call == call), It.IsAny<CancellationToken>()), Times.Once);
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CallAddedEvent>()), Times.Once);
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async Task Dispatching_a_scheduled_call_early_announces_an_update_not_a_new_call()
		{
			var call = PendingCall();
			call.State = (int)CallStates.Active;
			call.DispatchOn = DateTime.UtcNow.AddHours(2);
			call.HasBeenDispatched = false;

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.Dispatched);
			call.DispatchOn.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once);
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CallAddedEvent>()), Times.Never);
		}

		[Test]
		public async Task A_call_with_nobody_to_send_to_stays_pending_and_nothing_is_queued()
		{
			var call = PendingCall(withRecipient: false);

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.NoRecipients);
			call.State.Should().Be((int)CallStates.Pending);
			call.DispatchOn.Should().BeNull();
			_queueService.Verify(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
			_callsService.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(42, 7, ClaimedOn, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once,
				"the claim is given back so the call can be dispatched once it has recipients");
		}

		[Test]
		public async Task A_call_another_dispatcher_or_the_scheduler_already_claimed_is_not_sent_again()
		{
			_callsService.Setup(x => x.TryClaimCallForDispatchAsync(42, 7, It.IsAny<CancellationToken>())).ReturnsAsync((DateTime?)null);
			var call = PendingCall();

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.NotWaiting);
			call.State.Should().Be((int)CallStates.Pending);
			_queueService.Verify(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
			_callsService.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never,
				"the claim belongs to whoever won it");
		}

		[Test]
		public async Task A_failure_before_the_broadcast_gives_the_claim_back()
		{
			_callsService.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
			var call = PendingCall();

			Func<Task> dispatch = () => _service.DispatchNowAsync(call, "dispatcher");

			await dispatch.Should().ThrowAsync<InvalidOperationException>();
			call.State.Should().Be((int)CallStates.Pending);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(42, 7, ClaimedOn, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once);
			_queueService.Verify(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_call_that_is_not_waiting_is_left_alone()
		{
			var call = new Call { CallId = 9, DepartmentId = 7, State = (int)CallStates.Closed };

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.NotWaiting);
			call.State.Should().Be((int)CallStates.Closed);
			_queueService.Verify(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task When_the_broadcast_cannot_be_queued_the_call_goes_back_to_pending()
		{
			_queueService.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);
			var call = PendingCall();

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.QueueFailed);
			call.State.Should().Be((int)CallStates.Pending);
			call.DispatchOn.Should().BeNull();
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CallAddedEvent>()), Times.Never);
			// Put back with a conditional write, not a second full save of this snapshot: a close that landed meanwhile stays.
			_callsService.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Once);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(42, 7, ClaimedOn, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_queue_that_throws_is_reported_as_a_queue_failure_and_the_claim_is_given_back()
		{
			// QueueService throws when the outbound queue refuses the call; it never returns false.
			_queueService.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("Failed to enqueue call broadcast for processing."));
			var call = PendingCall();

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.QueueFailed);
			call.State.Should().Be((int)CallStates.Pending);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(42, 7, ClaimedOn, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once);
			_callsService.Verify(x => x.CompleteCallDispatchClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CallAddedEvent>()), Times.Never);
		}

		[Test]
		public async Task A_sent_call_ends_its_claim_so_the_lease_never_sends_it_again()
		{
			var outcome = await _service.DispatchNowAsync(PendingCall(), "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.Dispatched);
			_callsService.Verify(x => x.CompleteCallDispatchClaimAsync(42, 7, ClaimedOn, It.IsAny<CancellationToken>()), Times.Once);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public void A_call_whose_dispatch_claim_was_abandoned_is_waiting_again_once_the_lease_runs_out()
		{
			var stale = DateTime.UtcNow - CallDispatchClaims.Lease - TimeSpan.FromMinutes(1);
			var abandoned = new Call { State = (int)CallStates.Active, DispatchOn = stale, HasBeenDispatched = true, DispatchClaimedOn = stale };
			var inFlight = new Call { State = (int)CallStates.Active, DispatchOn = DateTime.UtcNow, HasBeenDispatched = true, DispatchClaimedOn = DateTime.UtcNow };
			var sent = new Call { State = (int)CallStates.Active, DispatchOn = stale, HasBeenDispatched = true };

			_service.IsWaitingForDispatch(abandoned).Should().BeTrue("nobody was paged, so Dispatch Now must be offered again");
			_service.IsWaitingForDispatch(inFlight).Should().BeFalse("another dispatch of it is still running");
			_service.IsWaitingForDispatch(sent).Should().BeFalse();
		}

		[Test]
		public async Task Taking_over_an_abandoned_scheduled_claim_and_failing_puts_it_back_as_not_sent()
		{
			_queueService.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);
			var stale = DateTime.UtcNow - CallDispatchClaims.Lease - TimeSpan.FromMinutes(1);
			var call = PendingCall();
			call.State = (int)CallStates.Active;
			call.DispatchOn = stale;
			call.HasBeenDispatched = true;
			call.DispatchClaimedOn = stale;

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.QueueFailed);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(42, 7, ClaimedOn, (int)CallStates.Active, stale, false, It.IsAny<CancellationToken>()), Times.Once,
				"restoring the abandoned 'sent' flag would hide the call from every retry");
		}

		[Test]
		public async Task Unit_and_group_dispatches_get_their_dispatch_statuses_applied()
		{
			var call = PendingCall(withRecipient: false);
			call.UnitDispatches.Add(new CallDispatchUnit { CallId = 42, UnitId = 5 });
			call.GroupDispatches.Add(new CallDispatchGroup { CallId = 42, DepartmentGroupId = 3 });

			await _service.DispatchNowAsync(call, "dispatcher");

			_callDispatchStatusService.Verify(x => x.ApplyDispatchStatusesAsync(call,
				It.Is<IEnumerable<int>>(g => g.SequenceEqual(new[] { 3 })),
				It.Is<IEnumerable<int>>(u => u.SequenceEqual(new[] { 5 })),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void A_pending_call_is_exempt_from_the_dispatch_list_and_dispatch_time_requirements()
		{
			var policy = new NewCallFieldPolicy
			{
				Rules = new List<NewCallFieldRule>
				{
					new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = true, Required = true },
					new NewCallFieldRule { Key = NewCallFieldKeys.DispatchOn, Visible = true, Required = true },
					new NewCallFieldRule { Key = NewCallFieldKeys.Address, Visible = true, Required = true }
				}
			};

			var pending = NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { IsPending = true });
			pending.Select(v => v.Key).Should().BeEquivalentTo(new[] { NewCallFieldKeys.Address }, "the other required fields still apply to a pending call");

			var immediate = NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues());
			immediate.Select(v => v.Key).Should().BeEquivalentTo(new[] { NewCallFieldKeys.Address, NewCallFieldKeys.DispatchList, NewCallFieldKeys.DispatchOn });
		}
	}
}
