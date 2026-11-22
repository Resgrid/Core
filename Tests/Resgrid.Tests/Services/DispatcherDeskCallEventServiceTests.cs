using System;
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

			var builder = new ContainerBuilder();
			builder.RegisterInstance(deskConfig.Object).As<IDispatcherDeskConfigService>();
			builder.RegisterInstance(_routing.Object).As<IDispatcherRoutingService>();
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
		public async Task Departments_without_the_desk_or_without_pending_call_routing_are_left_alone()
		{
			_desk.Enabled = false;
			await _service.OnCallChangedAsync(DepartmentId, PendingCall());
			var closed = PendingCall();
			closed.State = (int)CallStates.Closed;
			await _service.OnCallChangedAsync(DepartmentId, closed);

			_desk.Enabled = true;
			_desk.RoutePendingCalls = false;
			await _service.OnCallChangedAsync(DepartmentId, PendingCall());

			_routing.Verify(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(r => r.OnCallChangedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
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
