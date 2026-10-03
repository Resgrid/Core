extern alias Eventing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using EventingServices = Eventing::Resgrid.Web.Eventing.Services;
using EventingMiddleware = Eventing::Resgrid.Web.Eventing.Middleware;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 16: realtime connections follow session state. A connection is tracked by its
	/// session; the host's sweep closes those whose session ended, locked or went idle; a lock or revocation closes them at
	/// once; and session events reach only that session's own connections.
	/// </summary>
	[TestFixture]
	public class RealtimeSessionTests
	{
		private static DateTime Now => DateTime.UtcNow;

		// ---- The registry and its sweep -----------------------------------------------------------------------------------

		[Test]
		public async Task The_sweep_closes_only_connections_whose_session_can_no_longer_be_used()
		{
			var registry = new SessionConnectionRegistry();
			var closed = new List<string>();
			void Track(string connection, string session) => registry.Register(connection, session, () => closed.Add(connection));
			Track("c1", "active");
			Track("c2", "ended");
			Track("c3", "ended");
			Track("c4", "locked");
			registry.Register("c5", "active", () => throw new InvalidOperationException("already gone"));

			var sessions = new Mock<IUserSessionService>();
			IReadOnlyCollection<string> asked = null;
			sessions.Setup(s => s.GetUnusableSessionIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
				.Callback((IReadOnlyCollection<string> ids, CancellationToken _) => asked = ids)
				.ReturnsAsync(new HashSet<string> { "ended", "locked" });

			(await registry.SweepAsync(sessions.Object)).Should().Be(3);
			closed.Should().BeEquivalentTo("c2", "c3", "c4");
			asked.Should().BeEquivalentTo(new[] { "active", "ended", "locked" }, "one batched check per distinct session");
			registry.Count.Should().Be(2);

			(await registry.SweepAsync(sessions.Object)).Should().Be(0, "a closed connection is not closed twice");

			registry.Unregister("c1");
			registry.Count.Should().Be(1);
			registry.CloseSession("active").Should().Be(1, "a connection that throws while closing still leaves the registry");
			registry.Count.Should().Be(0);
			(await registry.SweepAsync(sessions.Object)).Should().Be(0);
			sessions.Verify(s => s.GetUnusableSessionIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(2),
				"nothing to check when nothing is connected");
		}

		[Test]
		public async Task A_connection_closed_meanwhile_by_another_closer_is_not_closed_again()
		{
			// A session-closed event can arrive while a sweep is running over the same connections.
			var registry = new SessionConnectionRegistry();
			var aborts = new List<string>();
			registry.Register("c1", "ended", () => { aborts.Add("c1"); registry.CloseSession("ended"); });
			registry.Register("c2", "ended", () => aborts.Add("c2"));
			var sessions = new Mock<IUserSessionService>();
			sessions.Setup(s => s.GetUnusableSessionIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new HashSet<string> { "ended" });

			var closed = await registry.SweepAsync(sessions.Object);

			aborts.Should().OnlyHaveUniqueItems().And.HaveCount(2);
			closed.Should().BeLessThan(2, "the connection the other closer took is not counted twice");
		}

		[Test]
		public void Closing_a_session_closes_its_connections_on_this_host_only()
		{
			var registry = new SessionConnectionRegistry();
			var closed = new List<string>();
			registry.Register("a1", "session-a", () => closed.Add("a1"));
			registry.Register("a2", "session-a", () => closed.Add("a2"));
			registry.Register("b1", "session-b", () => closed.Add("b1"));
			registry.Register(null, "session-c", () => closed.Add("never"));
			registry.Register("c1", "", () => closed.Add("never"));

			registry.CloseSession("session-a").Should().Be(2);
			closed.Should().BeEquivalentTo("a1", "a2");
			registry.Count.Should().Be(1);
		}

		// ---- Relaying session events -------------------------------------------------------------------------------------

		[Test]
		public async Task A_session_event_reaches_only_that_session_and_only_in_its_known_shape()
		{
			var clients = new Mock<IHubClients>();
			var group = new Mock<IClientProxy>();
			clients.Setup(c => c.Group(SessionEvents.GroupFor("session-9"))).Returns(group.Object);
			object[] sent = null;
			group.Setup(g => g.SendCoreAsync(SessionEvents.MfaApprovalChanged, It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
				.Callback((string _, object[] args, CancellationToken _) => sent = args).Returns(Task.CompletedTask);
			var registry = new SessionConnectionRegistry();

			await EventingServices.SessionEventRelay.RelayAsync(clients.Object, registry, "session-9",
				"{\"Name\":\"mfaApprovalChanged\",\"ApprovalRequestId\":\"ar-1\",\"State\":\"approved\"}");
			sent.Should().ContainSingle();
			sent[0].Should().BeEquivalentTo(new { approvalRequestId = "ar-1", state = "approved" });

			await EventingServices.SessionEventRelay.RelayAsync(clients.Object, registry, "session-9", "{\"Name\":\"logout\"}");
			await EventingServices.SessionEventRelay.RelayAsync(clients.Object, registry, "session-9", "not json");
			await EventingServices.SessionEventRelay.RelayAsync(clients.Object, registry, "", "{\"Name\":\"mfaApprovalChanged\"}");
			group.Verify(g => g.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once,
				"unknown, malformed or unaddressed events are dropped");
		}

		[Test]
		public async Task A_session_closed_event_closes_connections_and_tells_the_client_nothing()
		{
			var clients = new Mock<IHubClients>(MockBehavior.Strict);
			var registry = new SessionConnectionRegistry();
			var closed = false;
			registry.Register("c1", "session-9", () => closed = true);

			await EventingServices.SessionEventRelay.RelayAsync(clients.Object, registry, "session-9", "{\"Name\":\"sessionClosed\"}");

			closed.Should().BeTrue();
			registry.Count.Should().Be(0);
		}

		// ---- The hub filter ------------------------------------------------------------------------------------------------

		private sealed class TestHub : Hub
		{
		}

		private static (HubLifetimeContext Context, Mock<IGroupManager> Groups, Mock<HubCallerContext> Caller) Connection(params Claim[] claims)
		{
			var caller = new Mock<HubCallerContext>();
			caller.SetupGet(c => c.ConnectionId).Returns("conn-1");
			caller.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length == 0 ? null : "test")));
			var groups = new Mock<IGroupManager>();
			var hub = new TestHub { Groups = groups.Object };
			return (new HubLifetimeContext(caller.Object, Mock.Of<IServiceProvider>(), hub), groups, caller);
		}

		[TestCase(typeof(EventingMiddleware.SessionValidationHubFilter))]
		[TestCase(typeof(Resgrid.Web.Services.Middleware.SessionValidationHubFilter))]
		public async Task A_connection_with_a_session_joins_its_group_and_is_tracked_until_it_disconnects(Type filterType)
		{
			var registry = new SessionConnectionRegistry();
			var filter = (IHubFilter)Activator.CreateInstance(filterType, Mock.Of<IUserSessionService>(), registry);
			var (context, groups, caller) = Connection(new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(SessionClaimTypes.SessionId, "session-9"));

			await filter.OnConnectedAsync(context, _ => Task.CompletedTask);
			groups.Verify(g => g.AddToGroupAsync("conn-1", "session:session-9", It.IsAny<CancellationToken>()), Times.Once);
			registry.CloseSession("session-9").Should().Be(1);
			caller.Verify(c => c.Abort(), Times.Once, "the registry closes the real connection");

			await filter.OnConnectedAsync(context, _ => Task.CompletedTask);
			await filter.OnDisconnectedAsync(context, null, (_, _) => Task.CompletedTask);
			registry.Count.Should().Be(0);

			foreach (var claims in new[]
			{
				new[] { new Claim(ClaimTypes.NameIdentifier, "dept_22"), new Claim(SessionClaimTypes.SessionId, "s") },
				new[] { new Claim(ClaimTypes.NameIdentifier, "user-1") },
				Array.Empty<Claim>()
			})
			{
				var (other, otherGroups, _) = Connection(claims);
				await filter.OnConnectedAsync(other, _ => Task.CompletedTask);
				otherGroups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
					"workloads, pre-session tokens and anonymous connections have no session group");
			}

			registry.Count.Should().Be(0);
		}

		[TestCase(typeof(EventingMiddleware.SessionValidationHubFilter))]
		[TestCase(typeof(Resgrid.Web.Services.Middleware.SessionValidationHubFilter))]
		public async Task A_connection_that_fails_to_connect_is_not_left_tracked(Type filterType)
		{
			// SignalR skips OnDisconnectedAsync when OnConnectedAsync throws, so the filter has to remove the entry itself.
			var registry = new SessionConnectionRegistry();
			var filter = (IHubFilter)Activator.CreateInstance(filterType, Mock.Of<IUserSessionService>(), registry);
			var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(SessionClaimTypes.SessionId, "session-9") };

			var (joinFails, groups, _) = Connection(claims);
			groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("backplane unavailable"));
			await FluentActions.Awaiting(() => filter.OnConnectedAsync(joinFails, _ => Task.CompletedTask)).Should().ThrowAsync<InvalidOperationException>();
			registry.Count.Should().Be(0, "a failed group join leaves nothing for the sweep");

			var (hubFails, _, _) = Connection(claims);
			await FluentActions.Awaiting(() => filter.OnConnectedAsync(hubFails, _ => throw new InvalidOperationException("hub refused")))
				.Should().ThrowAsync<InvalidOperationException>();
			registry.Count.Should().Be(0, "a hub that refuses the connection leaves nothing for the sweep");
		}
	}
}
