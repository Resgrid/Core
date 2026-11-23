using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Calls changed anywhere reach the dispatcher desk: a call saved as pending is put on it at once, and a pending or scheduled call
	/// that was dispatched, closed or deleted has its desk items settled (IDispatcherRoutingService.OnCallChangedAsync).
	/// </summary>
	[TestFixture]
	public class DispatcherDeskCallEventServiceTests
	{
		private const int DepartmentId = 21;

		private DispatcherDeskConfig _desk;
		private Mock<IDispatcherRoutingService> _routing;
		private IContainer _container;
		private EventAggregator _events;
		private DispatcherDeskCallEventService _service;
		private TaskCompletionSource<bool> _routed;
		private Mock<IIntakeService> _intake;

		[SetUp]
		public void SetUp()
		{
			_desk = new DispatcherDeskConfig { Enabled = true };
			_routed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var deskConfig = new Mock<IDispatcherDeskConfigService>();
			deskConfig.Setup(d => d.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _desk);
			_routing = new Mock<IDispatcherRoutingService>();
			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.Callback(() => _routed.TrySetResult(true)).ReturnsAsync(DispatcherRouteResult.NotRouted());
			_routing.Setup(r => r.OnCallChangedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.Callback(() => _routed.TrySetResult(true)).Returns(Task.CompletedTask);

			_intake = new Mock<IIntakeService>();
			_intake.Setup(i => i.GetCallSourcesAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new Dictionary<int, IntakeCallSource>());

			var builder = new ContainerBuilder();
			builder.RegisterInstance(deskConfig.Object).As<IDispatcherDeskConfigService>();
			builder.RegisterInstance(_routing.Object).As<IDispatcherRoutingService>();
			builder.RegisterInstance(_intake.Object).As<IIntakeService>();
			_container = builder.Build();
			_events = new EventAggregator();
			_service = new DispatcherDeskCallEventService(_events, _container);
		}

		[TearDown]
		public void TearDown()
		{
			_container?.Dispose();
		}

		private static Call PendingCall() => new Call
		{
			CallId = 500, DepartmentId = DepartmentId, State = (int)CallStates.Pending, Name = "Lift assist", NatureOfCall = "Fall, no injuries", ReportingUserId = "dispatcher-1"
		};

		[Test]
		public async Task A_call_saved_as_pending_goes_on_the_desk_at_once()
		{
			await _service.OnCallChangedAsync(DepartmentId, PendingCall());

			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.DepartmentId == DepartmentId && q.Kind == DispatcherQueueItemKinds.PendingCall &&
				q.CallId == 500 && q.Subject == "Lift assist" && q.Body == "Fall, no injuries" && q.CreatedByUserId == "dispatcher-1"), It.IsAny<CancellationToken>()), Times.Once);
			// M0326: the desk also re-checks a waiting call, so an offer out for a call screened since comes back to the queue.
			_routing.Verify(r => r.OnCallChangedAsync(DepartmentId, 500, It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase((int)CallStates.Active, false)]
		[TestCase((int)CallStates.Closed, false)]
		[TestCase((int)CallStates.Cancelled, false)]
		[TestCase((int)CallStates.Pending, true)]
		public async Task A_dispatched_closed_or_deleted_call_settles_its_desk_items(int state, bool deleted)
		{
			var call = PendingCall();
			call.State = state;
			call.IsDeleted = deleted;

			await _service.OnCallChangedAsync(DepartmentId, call);

			_routing.Verify(r => r.OnCallChangedAsync(DepartmentId, 500, It.IsAny<CancellationToken>()), Times.Once);
			_routing.Verify(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Departments_without_the_desk_are_left_alone()
		{
			_desk.Enabled = false;
			await _service.OnCallChangedAsync(DepartmentId, PendingCall());
			var closed = PendingCall();
			closed.State = (int)CallStates.Closed;
			await _service.OnCallChangedAsync(DepartmentId, closed);

			_routing.Verify(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(r => r.OnCallChangedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Without_pending_call_routing_a_waiting_call_is_not_added_but_its_existing_items_are_still_reconciled()
		{
			_desk.RoutePendingCalls = false;

			await _service.OnCallChangedAsync(DepartmentId, PendingCall());

			_routing.Verify(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			// An intake text the desk took (Texts) and that was screened since: its offer must still come back (M0326).
			_routing.Verify(r => r.OnCallChangedAsync(DepartmentId, 500, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_chat_apps_waiting_call_goes_on_the_desk_as_a_chat_even_without_pending_call_routing()
		{
			_desk.RoutePendingCalls = false;
			var call = PendingCall();
			call.CallSource = (int)CallSources.Intake;
			call.ContactName = "Pat (Slack)";
			_intake.Setup(i => i.GetCallSourcesAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new Dictionary<int, IntakeCallSource>
			{
				[500] = new IntakeCallSource { CallId = 500, ChannelType = (int)IntakeChannelTypes.Slack }
			});

			await _service.OnCallChangedAsync(DepartmentId, call);

			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.Kind == DispatcherQueueItemKinds.Chat && q.CallId == 500 &&
				q.SourceName == "Pat (Slack)" && q.Subject == "Lift assist"), It.IsAny<CancellationToken>()), Times.Once);

			// Chats unticked: it falls back to a pending call, and with neither ticked it is not added.
			_desk.RouteChat = false;
			_desk.RoutePendingCalls = true;
			await _service.OnCallChangedAsync(DepartmentId, call);
			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.Kind == DispatcherQueueItemKinds.PendingCall), It.IsAny<CancellationToken>()), Times.Once);

			_desk.RoutePendingCalls = false;
			await _service.OnCallChangedAsync(DepartmentId, call);
			_routing.Verify(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task An_sms_intake_call_is_not_a_chat()
		{
			var call = PendingCall();
			call.CallSource = (int)CallSources.Intake;
			_intake.Setup(i => i.GetCallSourcesAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new Dictionary<int, IntakeCallSource>
			{
				[500] = new IntakeCallSource { CallId = 500, ChannelType = (int)IntakeChannelTypes.Sms }
			});

			await _service.OnCallChangedAsync(DepartmentId, call);

			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.Kind == DispatcherQueueItemKinds.PendingCall), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_emailed_waiting_call_goes_on_the_desk_as_an_email_but_the_desks_own_pending_calls_do_not()
		{
			_desk.RoutePendingCalls = false;
			var emailed = PendingCall();
			emailed.CallSource = (int)CallSources.EmailImport;

			await _service.OnCallChangedAsync(DepartmentId, emailed);

			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.Kind == DispatcherQueueItemKinds.Email && q.CallId == 500), It.IsAny<CancellationToken>()), Times.Once);

			// The desk's pending call for an overflowed text is EmailImport too ("DQ:{itemId}"): it is already the item's call, never an email.
			var overflowed = PendingCall();
			overflowed.CallSource = (int)CallSources.EmailImport;
			overflowed.SourceIdentifier = "DQ:item-1";
			await _service.OnCallChangedAsync(DepartmentId, overflowed);

			_routing.Verify(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_waiting_call_is_still_reconciled_when_its_intake_channel_cannot_be_read_or_the_enqueue_fails()
		{
			var call = PendingCall();
			call.CallSource = (int)CallSources.Intake;
			_intake.Setup(i => i.GetCallSourcesAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ThrowsAsync(new InvalidOperationException("intake down"));

			await _service.OnCallChangedAsync(DepartmentId, call);

			// The screened call's offer still comes back even though its kind could not be decided (the sweep adds it later).
			_routing.Verify(r => r.OnCallChangedAsync(DepartmentId, 500, It.IsAny<CancellationToken>()), Times.Once);
			_routing.Verify(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);

			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("insert failed"));
			await _service.OnCallChangedAsync(DepartmentId, PendingCall());

			_routing.Verify(r => r.OnCallChangedAsync(DepartmentId, 500, It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task A_desk_failure_never_reaches_the_publisher()
		{
			_routing.Setup(r => r.OnCallChangedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("desk down"));
			var closed = PendingCall();
			closed.State = (int)CallStates.Closed;

			await _service.Invoking(s => s.OnCallChangedAsync(DepartmentId, closed)).Should().NotThrowAsync();
			await _service.Invoking(s => s.OnCallChangedAsync(DepartmentId, null)).Should().NotThrowAsync();
			await _service.Invoking(s => s.OnCallChangedAsync(DepartmentId, new Call())).Should().NotThrowAsync();
		}

		[Test]
		public async Task Call_closed_events_raised_anywhere_reach_the_desk()
		{
			var call = PendingCall();
			call.State = (int)CallStates.Closed;

			_events.SendMessage(new CallClosedEvent { DepartmentId = DepartmentId, Call = call });

			(await Task.WhenAny(_routed.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(_routed.Task);
			_routing.Verify(r => r.OnCallChangedAsync(DepartmentId, 500, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Call_updated_events_for_a_pending_call_reach_the_desk()
		{
			_events.SendMessage(new CallUpdatedEvent { DepartmentId = DepartmentId, Call = PendingCall() });

			(await Task.WhenAny(_routed.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(_routed.Task);
			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.CallId == 500), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Call_added_events_for_a_dispatched_pending_call_reach_the_desk()
		{
			var call = PendingCall();
			call.State = (int)CallStates.Active;

			_events.SendMessage(new CallAddedEvent { DepartmentId = DepartmentId, Call = call });

			(await Task.WhenAny(_routed.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(_routed.Task);
			_routing.Verify(r => r.OnCallChangedAsync(DepartmentId, 500, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
