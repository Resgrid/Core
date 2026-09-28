extern alias Eventing;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using EventingHubs = Eventing::Resgrid.Web.Eventing.Hubs;
using ServicesHubs = Resgrid.Web.Services.Hubs;

namespace Resgrid.Tests.Web.Eventing
{
	/// <summary>
	/// SignalR does not wait for in-flight hub calls before disconnecting, and on the Redis backplane a group
	/// change for a connection no server holds waits 30 seconds for an ack and then throws. A hub call that
	/// finds its connection closed skips the group change.
	/// </summary>
	[TestFixture]
	public class ClosedConnectionHubTests
	{
		private const int DepartmentId = 7;
		private const int LinkId = 3;
		private const int CallId = 5;
		private const string ChannelId = "ch-1";
		private const string UserId = "b0000000-0000-0000-0000-000000000001";

		private static readonly Func<Harness, Task> NoArrange = _ => Task.CompletedTask;

		private static readonly TestCaseData[] GroupChangingCalls =
		{
			Case("Eventing.Connect", h => h.Eventing.Connect(DepartmentId)),
			Case("Eventing.SubscribeToDepartmentLink", h => h.Eventing.SubscribeToDepartmentLink(LinkId)),
			Case("Eventing.UnsubscribeToDepartmentLink", h => h.Eventing.UnsubscribeToDepartmentLink(LinkId)),
			Case("Eventing.SubscribeToCall", h => h.Eventing.SubscribeToCall(CallId)),
			Case("Eventing.UnsubscribeToCall", h => h.Eventing.UnsubscribeToCall(CallId)),
			Case("Services.Connect", h => h.Services.Connect(DepartmentId)),
			Case("Services.SubscribeToDepartmentLink", h => h.Services.SubscribeToDepartmentLink(LinkId)),
			Case("Services.UnsubscribeToDepartmentLink", h => h.Services.UnsubscribeToDepartmentLink(LinkId)),
			Case("Chat.Connect", h => h.Chat.Connect()),
			Case("Chat.JoinChannel", h => h.Chat.JoinChannel(ChannelId)),
			Case("Chat.LeaveChannel", h => h.Chat.LeaveChannel(ChannelId), arrange: h => h.Chat.JoinChannel(ChannelId))
		};

		[TestCaseSource(nameof(GroupChangingCalls))]
		public async Task Open_connection_changes_groups(Func<Harness, Task> arrange, Func<Harness, Task> act)
		{
			var harness = new Harness();
			await arrange(harness);
			harness.Groups.Invocations.Clear();

			await act(harness);

			harness.Groups.Invocations.Should().NotBeEmpty("otherwise the closed-connection case proves nothing");
		}

		[TestCaseSource(nameof(GroupChangingCalls))]
		public async Task Closed_connection_leaves_groups_alone(Func<Harness, Task> arrange, Func<Harness, Task> act)
		{
			var harness = new Harness();
			await arrange(harness);
			harness.Groups.Invocations.Clear();
			harness.CloseConnection();

			await act(harness);

			harness.Groups.Invocations.Should().BeEmpty();
		}

		private static TestCaseData Case(string name, Func<Harness, Task> act, Func<Harness, Task> arrange = null) =>
			new TestCaseData(arrange ?? NoArrange, act).SetArgDisplayNames(name);

		public sealed class Harness
		{
			private readonly CancellationTokenSource _connectionAborted = new CancellationTokenSource();

			public Harness()
			{
				var context = new Mock<HubCallerContext>();
				context.SetupGet(x => x.ConnectionId).Returns("conn-1");
				context.SetupGet(x => x.ConnectionAborted).Returns(_connectionAborted.Token);
				context.SetupGet(x => x.Items).Returns(new Dictionary<object, object>());
				context.SetupGet(x => x.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test")));

				var clients = new Mock<IHubCallerClients>();
				clients.SetupGet(x => x.Caller).Returns(Mock.Of<ISingleClientProxy>());
				clients.Setup(x => x.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());

				var links = new Mock<IDepartmentLinksService>();
				links.Setup(x => x.GetLinkByIdAsync(LinkId))
					.ReturnsAsync(new DepartmentLink { DepartmentId = DepartmentId, LinkedDepartmentId = 8, LinkEnabled = true });

				var calls = new Mock<ICallsService>();
				calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { DepartmentId = DepartmentId });

				var channels = new Mock<IChatChannelService>();
				channels.Setup(x => x.GetChannelByIdAsync(ChannelId))
					.ReturnsAsync(new ChatChannel { ChatChannelId = ChannelId, DepartmentId = DepartmentId });

				var permissions = new Mock<IChatPermissionService>();
				permissions.Setup(x => x.IsActiveDepartmentUserAsync(DepartmentId, UserId)).ReturnsAsync(true);
				permissions.Setup(x => x.CanAccessChannelAsync(It.IsAny<ChatChannel>(), UserId, null)).ReturnsAsync(true);
				permissions.Setup(x => x.GetChannelAccessVersionAsync(ChannelId)).ReturnsAsync("v1");

				Eventing = Attach(new EventingHubs.EventingHub(links.Object, calls.Object), context.Object, clients.Object);
				Services = Attach(new ServicesHubs.EventingHub(links.Object), context.Object, clients.Object);
				Chat = Attach(new EventingHubs.ChatHub(channels.Object, permissions.Object, Mock.Of<IChatMessageService>(),
					Mock.Of<IChatPresenceService>()), context.Object, clients.Object);
			}

			public Mock<IGroupManager> Groups { get; } = new Mock<IGroupManager>();
			public EventingHubs.EventingHub Eventing { get; }
			public ServicesHubs.EventingHub Services { get; }
			public EventingHubs.ChatHub Chat { get; }

			public void CloseConnection() => _connectionAborted.Cancel();

			private T Attach<T>(T hub, HubCallerContext context, IHubCallerClients clients) where T : Hub
			{
				hub.Context = context;
				hub.Groups = Groups.Object;
				hub.Clients = clients;
				return hub;
			}
		}
	}
}
