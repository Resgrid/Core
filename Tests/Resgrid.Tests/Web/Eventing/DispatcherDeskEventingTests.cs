extern alias Eventing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Services;
using EventingHubs = Eventing::Resgrid.Web.Eventing.Hubs;
using EventingServices = Eventing::Resgrid.Web.Eventing.Services;
using EventingWorker = Eventing::Resgrid.Web.Eventing.Worker;

namespace Resgrid.Tests.Web.Eventing
{
	/// <summary>
	/// Dispatcher desk realtime (M0324, plan D7): DispatcherDeskUpdatedEvent (EventingTypes 19) and DispatcherQueueUpdatedEvent (20)
	/// ride the eventing rail (OutboundEventProvider -> RabbitTopicProvider -> EventingTopic -> RabbitInboundEventProvider ->
	/// Eventing Worker) and reach the department's SignalR group as <c>dispatcherDeskUpdated</c> (user id, payload) and
	/// <c>dispatcherQueueUpdated</c> (item id, payload).
	/// </summary>
	[TestFixture]
	public class DispatcherDeskEventingTests
	{
		private const int DepartmentId = 42;

		[Test]
		public void Eventing_type_values_are_pinned()
		{
			((int)EventingTypes.DispatcherDeskUpdated).Should().Be(19);
			((int)EventingTypes.DispatcherQueueUpdated).Should().Be(20);
		}

		[Test]
		public void Outbound_provider_listens_for_both_desk_events()
		{
			var aggregator = new Mock<IEventAggregator>();

			_ = new OutboundEventProvider(aggregator.Object, Mock.Of<IOutboundQueueProvider>(), Mock.Of<ISignalrProvider>());

			aggregator.Verify(x => x.AddAsyncListener(It.IsAny<Func<DispatcherDeskUpdatedEvent, Task>>(), It.IsAny<Action<Exception>>()), Times.Once);
			aggregator.Verify(x => x.AddAsyncListener(It.IsAny<Func<DispatcherQueueUpdatedEvent, Task>>(), It.IsAny<Action<Exception>>()), Times.Once);
		}

		[Test]
		public async Task Desk_event_goes_to_the_department_group_with_user_id_and_payload()
		{
			using var harness = await Harness.StartAsync();

			harness.DeskHandler.Should().NotBeNull("the eventing worker must register for dispatcher desk events");
			await harness.DeskHandler(DepartmentId, "user-1", "{\"userId\":\"user-1\",\"state\":3}");

			harness.GroupNames.Should().Contain(DepartmentId.ToString());
			harness.Sent.Should().ContainSingle(x => x.Method == "dispatcherDeskUpdated");
			harness.Sent.Single().Args.Should().Equal("user-1", "{\"userId\":\"user-1\",\"state\":3}");
		}

		[Test]
		public async Task Desk_event_about_nobody_sends_an_empty_user_id()
		{
			using var harness = await Harness.StartAsync();

			await harness.DeskHandler(DepartmentId, null, "{}");

			harness.Sent.Should().ContainSingle(x => x.Method == "dispatcherDeskUpdated");
			harness.Sent.Single().Args[0].Should().Be("", "clients key on a string, never null");
		}

		[Test]
		public async Task Queue_event_goes_to_the_department_group_with_item_id_and_payload()
		{
			using var harness = await Harness.StartAsync();

			harness.QueueHandler.Should().NotBeNull("the eventing worker must register for dispatcher queue events");
			await harness.QueueHandler(DepartmentId, "item-1", "{\"itemId\":\"item-1\",\"state\":1}");

			harness.GroupNames.Should().Contain(DepartmentId.ToString());
			harness.Sent.Should().ContainSingle(x => x.Method == "dispatcherQueueUpdated");
			harness.Sent.Single().Args.Should().Equal("item-1", "{\"itemId\":\"item-1\",\"state\":1}");
		}

		private sealed class Harness : IDisposable
		{
			private readonly CancellationTokenSource _stop = new CancellationTokenSource();
			private EventingWorker _worker;
			private ServiceProvider _services;

			public Func<int, string, string, Task> DeskHandler { get; private set; }
			public Func<int, string, string, Task> QueueHandler { get; private set; }
			public List<string> GroupNames { get; } = new List<string>();
			public List<(string Method, object[] Args)> Sent { get; } = new List<(string, object[])>();

			public static async Task<Harness> StartAsync()
			{
				var harness = new Harness();

				var inbound = new Mock<IRabbitInboundEventProvider>();
				inbound.Setup(x => x.RegisterForDispatcherDeskEvents(It.IsAny<Func<int, string, string, Task>>()))
					.Callback((Func<int, string, string, Task> handler) => harness.DeskHandler = handler);
				inbound.Setup(x => x.RegisterForDispatcherQueueEvents(It.IsAny<Func<int, string, string, Task>>()))
					.Callback((Func<int, string, string, Task> handler) => harness.QueueHandler = handler);
				inbound.Setup(x => x.Start(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
				inbound.Setup(x => x.IsConnected()).Returns(true);

				var proxy = new Mock<IClientProxy>();
				proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
					.Callback((string method, object[] args, CancellationToken _) => harness.Sent.Add((method, args)))
					.Returns(Task.CompletedTask);

				var clients = new Mock<IHubClients>();
				clients.Setup(x => x.Group(It.IsAny<string>()))
					.Callback((string group) => harness.GroupNames.Add(group))
					.Returns(proxy.Object);

				var eventingHub = new Mock<IHubContext<EventingHubs.EventingHub>>();
				eventingHub.Setup(x => x.Clients).Returns(clients.Object);

				harness._services = new ServiceCollection().AddSingleton(inbound.Object).BuildServiceProvider();
				harness._worker = new EventingWorker(harness._services, eventingHub.Object, Mock.Of<IHubContext<EventingHubs.GeolocationHub>>(),
					Mock.Of<IHubContext<EventingHubs.ChatHub>>(), new EventingServices.GeolocationBroadcaster(Mock.Of<ILocationVisibilityService>()),
					new SessionConnectionRegistry());

				await harness._worker.StartAsync(harness._stop.Token);

				return harness;
			}

			public void Dispose()
			{
				_stop.Cancel();
				try
				{
					_worker?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
				}
				catch (OperationCanceledException)
				{
				}

				_worker?.Dispose();
				_services?.Dispose();
				_stop.Dispose();
			}
		}
	}
}
