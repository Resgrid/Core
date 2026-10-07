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
				.ReturnsAsync(true);
			_callsService.Setup(x => x.ReleaseCallDispatchClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
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
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(42, 7, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once,
				"the claim is given back so the call can be dispatched once it has recipients");
		}

		[Test]
		public async Task A_call_another_dispatcher_or_the_scheduler_already_claimed_is_not_sent_again()
		{
			_callsService.Setup(x => x.TryClaimCallForDispatchAsync(42, 7, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			var call = PendingCall();

			var outcome = await _service.DispatchNowAsync(call, "dispatcher");

			outcome.Should().Be(DispatchNowOutcome.NotWaiting);
			call.State.Should().Be((int)CallStates.Pending);
			_queueService.Verify(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
			_callsService.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never,
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
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(42, 7, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once);
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
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(42, 7, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once);
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
