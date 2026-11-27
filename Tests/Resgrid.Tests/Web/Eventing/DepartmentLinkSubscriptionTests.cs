extern alias Eventing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Security.Live;
using EventingHubs = Eventing::Resgrid.Web.Eventing.Hubs;
using EventingWorker = Eventing::Resgrid.Web.Eventing.Worker;
using ServicesHubs = Resgrid.Web.Services.Hubs;

namespace Resgrid.Tests.Web.Eventing
{
	/// <summary>
	/// A department-link subscription used to put the caller in the other department's own SignalR group, so it received
	/// all of that department's events (personnel, units, calls, checklists, incident command, requirements...) whatever
	/// the link shared — and in either direction. It now joins one group per kind of data the other side shares toward
	/// the caller, and publishers send each kind's events to that kind's group as well as the department's own.
	/// <para>
	/// The share groups also carry the sharing department's access epoch. Disabling, deleting or narrowing a link rotates
	/// both parties' epochs and publishers only send to the current epoch's groups, so a subscription made before the
	/// revocation stops receiving events at once instead of when the client next reconnects; subscribing again re-checks
	/// the link. These tests run the real DepartmentLinksService over an in-memory link row and cache.
	/// </para>
	/// </summary>
	[TestFixture]
	public class DepartmentLinkSubscriptionTests
	{
		private const int Creator = 100;
		private const int Linked = 200;
		private const int Outsider = 300;
		private const int LinkId = 9;

		public enum HubKind { Eventing, Services }

		private static readonly HubKind[] BothHubs = { HubKind.Eventing, HubKind.Services };

		/// <summary>The link row, the cache and the service that every connection and publisher in a test share.</summary>
		private sealed class World
		{
			private DepartmentLink _stored;

			public World(DepartmentLink link, ICacheProvider cache = null)
			{
				_stored = Copy(link);
				Cache = cache ?? new InMemoryCacheProvider();

				// Every read is a fresh copy, as from the database: editing a link changes nothing until it is saved.
				var repository = new Mock<IDepartmentLinksRepository>();
				repository.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
					.ReturnsAsync((object id) => _stored != null && (int)id == _stored.DepartmentLinkId ? Copy(_stored) : null);
				repository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((DepartmentLink saved, CancellationToken _, bool __) => _stored = Copy(saved));
				repository.Setup(x => x.DeleteAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((DepartmentLink deleted, CancellationToken _) =>
					{
						var existed = _stored != null && _stored.DepartmentLinkId == deleted.DepartmentLinkId;
						if (existed)
							_stored = null;
						return existed;
					});

				Service = new DepartmentLinksService(repository.Object, Mock.Of<IDepartmentsRepository>(), Cache, System.Array.Empty<IDepartmentLinkAccessListener>());
			}

			public ICacheProvider Cache { get; }
			public DepartmentLinksService Service { get; }

			/// <summary>What an admin edits: a copy of the stored row.</summary>
			public DepartmentLink Stored() => _stored == null ? null : Copy(_stored);

			/// <summary>The group a publisher sends this kind of event to right now (null when there is no epoch).</summary>
			public Task<string> CurrentGroup(int departmentId, DepartmentLinkShareTypes type) => Service.GetShareGroupNameAsync(departmentId, type);

			public Connection Connect(int departmentId, string subject = null) => new Connection(this, departmentId, subject);

			public Connection Publisher() => Connect(0, "system_eventing");
		}

		private static DepartmentLink Copy(DepartmentLink link) => link == null ? null : new DepartmentLink
		{
			DepartmentLinkId = link.DepartmentLinkId,
			DepartmentId = link.DepartmentId,
			DepartmentColor = link.DepartmentColor,
			DepartmentShareCalls = link.DepartmentShareCalls,
			DepartmentShareUnits = link.DepartmentShareUnits,
			DepartmentSharePersonnel = link.DepartmentSharePersonnel,
			DepartmentShareOrders = link.DepartmentShareOrders,
			LinkedDepartmentId = link.LinkedDepartmentId,
			LinkEnabled = link.LinkEnabled,
			LinkedDepartmentColor = link.LinkedDepartmentColor,
			LinkedDepartmentShareCalls = link.LinkedDepartmentShareCalls,
			LinkedDepartmentShareUnits = link.LinkedDepartmentShareUnits,
			LinkedDepartmentSharePersonnel = link.LinkedDepartmentSharePersonnel,
			LinkedDepartmentShareOrders = link.LinkedDepartmentShareOrders,
			LinkCreated = link.LinkCreated,
			LinkAccepted = link.LinkAccepted
		};

		/// <summary>One SignalR connection, on either hub, with the groups it is in and what it sent.</summary>
		private sealed class Connection
		{
			private static int _next;
			private readonly EventingHubs.EventingHub _eventing;
			private readonly ServicesHubs.EventingHub _services;

			public Connection(World world, int departmentId, string subject)
			{
				var connectionId = $"conn-{Interlocked.Increment(ref _next)}";
				var claims = new List<Claim>
				{
					new Claim(ClaimTypes.PrimarySid, subject ?? $"USER-{departmentId}"),
					new Claim(ClaimTypes.PrimaryGroupSid, departmentId.ToString())
				};
				var context = new Mock<HubCallerContext>();
				context.SetupGet(x => x.ConnectionId).Returns(connectionId);
				context.SetupGet(x => x.ConnectionAborted).Returns(CancellationToken.None);
				context.SetupGet(x => x.Items).Returns(new Dictionary<object, object>());
				context.SetupGet(x => x.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));

				var clients = new Mock<IHubCallerClients>();
				clients.Setup(x => x.Group(It.IsAny<string>())).Returns<string>(name =>
				{
					var proxy = new Mock<IClientProxy>();
					proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
						.Callback<string, object[], CancellationToken>((method, _, __) => Sent.Add((name, method)))
						.Returns(Task.CompletedTask);
					return proxy.Object;
				});

				var groups = new Mock<IGroupManager>();
				groups.Setup(x => x.AddToGroupAsync(connectionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.Callback<string, string, CancellationToken>((_, group, __) => { Joined.Add(group); Member.Add(group); }).Returns(Task.CompletedTask);
				groups.Setup(x => x.RemoveFromGroupAsync(connectionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.Callback<string, string, CancellationToken>((_, group, __) => { Left.Add(group); Member.Remove(group); }).Returns(Task.CompletedTask);

				_eventing = new EventingHubs.EventingHub(world.Service, Mock.Of<ICallsService>()) { Context = context.Object, Groups = groups.Object, Clients = clients.Object };
				_services = new ServicesHubs.EventingHub(world.Service) { Context = context.Object, Groups = groups.Object, Clients = clients.Object };
			}

			public List<string> Joined { get; } = new List<string>();
			public List<string> Left { get; } = new List<string>();
			public HashSet<string> Member { get; } = new HashSet<string>();
			public List<(string Group, string Method)> Sent { get; } = new List<(string, string)>();

			public Task Subscribe(HubKind hub) =>
				hub == HubKind.Eventing ? _eventing.SubscribeToDepartmentLink(LinkId) : _services.SubscribeToDepartmentLink(LinkId);

			public Task Unsubscribe(HubKind hub) =>
				hub == HubKind.Eventing ? _eventing.UnsubscribeToDepartmentLink(LinkId) : _services.UnsubscribeToDepartmentLink(LinkId);

			public Task UnitStatusUpdated(HubKind hub, int departmentId) =>
				hub == HubKind.Eventing ? _eventing.UnitStatusUpdated(departmentId, 1) : _services.UnitStatusUpdated(departmentId, 1);

			public Task PersonnelStatusUpdated(HubKind hub, int departmentId) =>
				hub == HubKind.Eventing ? _eventing.PersonnelStatusUpdated(departmentId, 1) : _services.PersonnelStatusUpdated(departmentId, 1);

			public Task CallsUpdated(HubKind hub, int departmentId) =>
				hub == HubKind.Eventing ? _eventing.CallsUpdated(departmentId, 1) : _services.CallsUpdated(departmentId, 1);
		}

		/// <summary>The events <paramref name="publisher"/> sent to a group <paramref name="subscriber"/> is in.</summary>
		private static List<string> Received(Connection subscriber, Connection publisher) =>
			publisher.Sent.Where(s => subscriber.Member.Contains(s.Group)).Select(s => s.Method).ToList();

		private static DepartmentLink Link(bool enabled = true) =>
			new DepartmentLink { DepartmentLinkId = LinkId, DepartmentId = Creator, LinkedDepartmentId = Linked, LinkEnabled = enabled };

		private static DepartmentLink CreatorSharesUnitsAndPersonnel()
		{
			var link = Link();
			link.DepartmentShareUnits = true;
			link.DepartmentSharePersonnel = true;
			return link;
		}

		// ---- Who joins what ----

		[TestCaseSource(nameof(BothHubs))]
		public async Task The_linked_department_joins_only_the_kinds_the_creator_shares(HubKind hub)
		{
			var link = CreatorSharesUnitsAndPersonnel();
			// What the linked department shares back does not widen what it may receive.
			link.LinkedDepartmentShareCalls = true;
			var world = new World(link);
			var subscriber = world.Connect(Linked);

			await subscriber.Subscribe(hub);

			subscriber.Joined.Should().BeEquivalentTo(
				await world.CurrentGroup(Creator, DepartmentLinkShareTypes.Units), await world.CurrentGroup(Creator, DepartmentLinkShareTypes.Personnel));
			subscriber.Joined.Should().NotContain(Creator.ToString(), "the department's own group carries all of its traffic");
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task The_creator_joins_only_the_kinds_the_linked_department_shares_back(HubKind hub)
		{
			var link = Link();
			link.DepartmentShareCalls = link.DepartmentShareUnits = link.DepartmentSharePersonnel = true;
			link.LinkedDepartmentShareCalls = true;
			var world = new World(link);
			var subscriber = world.Connect(Creator);

			await subscriber.Subscribe(hub);

			subscriber.Joined.Should().Equal(await world.CurrentGroup(Linked, DepartmentLinkShareTypes.Calls));
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task Nothing_shared_toward_the_caller_is_refused(HubKind hub)
		{
			// The creator shares everything, but nothing comes back toward the creator.
			var link = Link();
			link.DepartmentShareCalls = link.DepartmentShareUnits = link.DepartmentSharePersonnel = link.DepartmentShareOrders = true;
			var subscriber = new World(link).Connect(Creator);

			await subscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>();
			subscriber.Joined.Should().BeEmpty();
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task Orders_alone_are_not_an_evented_kind_and_join_nothing(HubKind hub)
		{
			var link = Link();
			link.DepartmentShareOrders = true;
			var subscriber = new World(link).Connect(Linked);

			await subscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>();
			subscriber.Joined.Should().BeEmpty();
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task A_disabled_link_is_refused(HubKind hub)
		{
			var link = Link(enabled: false);
			link.DepartmentShareCalls = link.DepartmentShareUnits = link.DepartmentSharePersonnel = true;
			var subscriber = new World(link).Connect(Linked);

			await subscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>();
			subscriber.Joined.Should().BeEmpty();
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task A_department_outside_the_link_is_refused_and_leaves_nothing(HubKind hub)
		{
			var link = Link();
			link.DepartmentShareCalls = link.DepartmentShareUnits = link.DepartmentSharePersonnel = true;
			link.LinkedDepartmentShareCalls = link.LinkedDepartmentShareUnits = link.LinkedDepartmentSharePersonnel = true;
			var subscriber = new World(link).Connect(Outsider);

			await subscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>();
			await subscriber.Unsubscribe(hub);
			subscriber.Joined.Should().BeEmpty();
			subscriber.Left.Should().BeEmpty();
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task Unsubscribing_leaves_exactly_the_groups_joined_even_after_the_link_is_deleted(HubKind hub)
		{
			var world = new World(CreatorSharesUnitsAndPersonnel());
			var subscriber = world.Connect(Linked);
			await subscriber.Unsubscribe(hub);
			subscriber.Left.Should().BeEmpty("a connection that never subscribed has nothing to leave");

			await subscriber.Subscribe(hub);
			await world.Service.DeleteLinkAsync(world.Stored(), CancellationToken.None);
			await subscriber.Unsubscribe(hub);

			subscriber.Left.Should().BeEquivalentTo(subscriber.Joined);
			subscriber.Left.Should().NotContain(Creator.ToString());
			subscriber.Member.Should().BeEmpty();
		}

		// ---- Who receives what ----

		[TestCaseSource(nameof(BothHubs))]
		public async Task Hub_publishes_reach_the_department_and_only_the_matching_current_share_group(HubKind hub)
		{
			var link = Link();
			link.DepartmentShareCalls = link.DepartmentShareUnits = link.DepartmentSharePersonnel = true;
			var world = new World(link);
			var subscriber = world.Connect(Linked);
			await subscriber.Subscribe(hub);
			var publisher = world.Publisher();

			await publisher.UnitStatusUpdated(hub, Creator);
			await publisher.PersonnelStatusUpdated(hub, Creator);
			await publisher.CallsUpdated(hub, Creator);

			publisher.Sent.Select(s => s.Group).Should().Equal(
				Creator.ToString(), await world.CurrentGroup(Creator, DepartmentLinkShareTypes.Units),
				Creator.ToString(), await world.CurrentGroup(Creator, DepartmentLinkShareTypes.Personnel),
				Creator.ToString(), await world.CurrentGroup(Creator, DepartmentLinkShareTypes.Calls));
			Received(subscriber, publisher).Should().HaveCount(3);
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task With_no_subscription_epoch_hub_publishes_reach_only_the_department(HubKind hub)
		{
			var publisher = new World(CreatorSharesUnitsAndPersonnel()).Publisher();

			await publisher.UnitStatusUpdated(hub, Creator);

			publisher.Sent.Select(s => s.Group).Should().Equal(Creator.ToString());
		}

		// ---- Revocation ----

		[TestCaseSource(nameof(BothHubs))]
		public async Task Disabling_the_link_stops_existing_subscriptions_at_once_and_they_cannot_resubscribe(HubKind hub)
		{
			var world = new World(CreatorSharesUnitsAndPersonnel());
			var subscriber = world.Connect(Linked);
			await subscriber.Subscribe(hub);
			var publisher = world.Publisher();
			await publisher.UnitStatusUpdated(hub, Creator);
			Received(subscriber, publisher).Should().ContainSingle("before the change the subscription receives the creator's unit events");

			// What LinksController.DisableLink saves.
			var link = world.Stored();
			link.LinkEnabled = false;
			link.LinkAccepted = null;
			await world.Service.SaveAsync(link, CancellationToken.None);

			publisher.Sent.Clear();
			await publisher.UnitStatusUpdated(hub, Creator);
			await publisher.PersonnelStatusUpdated(hub, Creator);

			Received(subscriber, publisher).Should().BeEmpty("the subscription made before the link was disabled is stranded");
			publisher.Sent.Select(s => s.Group).Should().Contain(Creator.ToString(), "the department's own members still get their events");
			await subscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>();
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task Deleting_the_link_stops_existing_subscriptions_on_both_sides(HubKind hub)
		{
			var link = CreatorSharesUnitsAndPersonnel();
			link.LinkedDepartmentShareUnits = true;
			var world = new World(link);
			var linkedSubscriber = world.Connect(Linked);
			var creatorSubscriber = world.Connect(Creator);
			await linkedSubscriber.Subscribe(hub);
			await creatorSubscriber.Subscribe(hub);

			await world.Service.DeleteLinkAsync(world.Stored(), CancellationToken.None);

			var publisher = world.Publisher();
			await publisher.UnitStatusUpdated(hub, Creator);
			await publisher.UnitStatusUpdated(hub, Linked);

			Received(linkedSubscriber, publisher).Should().BeEmpty();
			Received(creatorSubscriber, publisher).Should().BeEmpty();
			await linkedSubscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>();
			await creatorSubscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>();
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task Narrowing_the_link_strands_existing_subscriptions_and_resubscribing_gets_only_what_is_still_shared(HubKind hub)
		{
			var world = new World(CreatorSharesUnitsAndPersonnel());
			var subscriber = world.Connect(Linked);
			await subscriber.Subscribe(hub);

			var link = world.Stored();
			link.DepartmentShareUnits = false;
			await world.Service.SaveAsync(link, CancellationToken.None);

			var publisher = world.Publisher();
			await publisher.UnitStatusUpdated(hub, Creator);
			await publisher.PersonnelStatusUpdated(hub, Creator);
			Received(subscriber, publisher).Should().BeEmpty();

			await subscriber.Subscribe(hub);
			publisher.Sent.Clear();
			await publisher.UnitStatusUpdated(hub, Creator);
			await publisher.PersonnelStatusUpdated(hub, Creator);

			Received(subscriber, publisher).Should().ContainSingle().Which.Should().BeOneOf("PersonnelStatusUpdated", "personnelStatusUpdated");
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task Saving_a_link_without_taking_anything_away_leaves_subscriptions_alone(HubKind hub)
		{
			var world = new World(CreatorSharesUnitsAndPersonnel());
			var subscriber = world.Connect(Linked);
			await subscriber.Subscribe(hub);

			// What LinksController.Enable saves when an already-enabled link is recolored.
			var link = world.Stored();
			link.DepartmentColor = "#ff0000";
			link.LinkAccepted = DateTime.UtcNow;
			await world.Service.SaveAsync(link, CancellationToken.None);

			var publisher = world.Publisher();
			await publisher.UnitStatusUpdated(hub, Creator);
			Received(subscriber, publisher).Should().ContainSingle();
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task A_revocation_landing_between_the_epoch_read_and_the_link_re_read_is_refused(HubKind hub)
		{
			var cache = new HookedCache();
			var world = new World(CreatorSharesUnitsAndPersonnel(), cache);
			var subscriber = world.Connect(Linked);

			// The admin's disable (write, then rotate) lands right after the subscriber read the old epoch.
			cache.AfterEpochRead = async () =>
			{
				var link = world.Stored();
				link.LinkEnabled = false;
				await world.Service.SaveAsync(link, CancellationToken.None);
			};

			await subscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>();
			subscriber.Joined.Should().BeEmpty();
		}

		[TestCaseSource(nameof(BothHubs))]
		public async Task Without_the_cache_subscribing_is_refused_and_publishes_reach_only_the_department(HubKind hub)
		{
			// A cache that is down: GetOrAddStringAsync and GetStringAsync give null, SetStringAsync gives false.
			var world = new World(CreatorSharesUnitsAndPersonnel(), Mock.Of<ICacheProvider>());
			var subscriber = world.Connect(Linked);

			(await subscriber.Invoking(s => s.Subscribe(hub)).Should().ThrowAsync<HubException>())
				.Which.Message.Should().Contain("temporarily unavailable");
			subscriber.Joined.Should().BeEmpty();

			var publisher = world.Publisher();
			await publisher.UnitStatusUpdated(hub, Creator);
			publisher.Sent.Select(s => s.Group).Should().Equal(Creator.ToString());
		}

		// ---- The Eventing Worker (RabbitMQ events) ----

		private static readonly (string Event, DepartmentLinkShareTypes Kind)[] WorkerEvents =
		{
			("personnelStatusUpdated", DepartmentLinkShareTypes.Personnel),
			("personnelStaffingUpdated", DepartmentLinkShareTypes.Personnel),
			("unitStatusUpdated", DepartmentLinkShareTypes.Units),
			("callsUpdated", DepartmentLinkShareTypes.Calls),
			("callAdded", DepartmentLinkShareTypes.Calls),
			("callClosed", DepartmentLinkShareTypes.Calls)
		};

		private static (EventingWorker Worker, List<(string Group, string Method)> Sent) Worker(World world)
		{
			var sent = new List<(string Group, string Method)>();
			var clients = new Mock<IHubClients>();
			clients.Setup(x => x.Group(It.IsAny<string>())).Returns<string>(name =>
			{
				var proxy = new Mock<IClientProxy>();
				proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
					.Callback<string, object[], CancellationToken>((method, _, __) => sent.Add((name, method)))
					.Returns(Task.CompletedTask);
				return proxy.Object;
			});
			var hubContext = new Mock<IHubContext<EventingHubs.EventingHub>>();
			hubContext.SetupGet(x => x.Clients).Returns(clients.Object);
			var chat = new Mock<IHubContext<EventingHubs.ChatHub>>();
			chat.SetupGet(x => x.Clients).Returns(Mock.Of<IHubClients>(c => c.Group(It.IsAny<string>()) == Mock.Of<IClientProxy>()));

			var services = new ServiceCollection()
				.AddSingleton(Mock.Of<IRabbitInboundEventProvider>())
				.AddSingleton<IDepartmentLinksService>(world.Service)
				.BuildServiceProvider();

			return (new EventingWorker(services, hubContext.Object, Mock.Of<IHubContext<EventingHubs.GeolocationHub>>(), chat.Object, null, null), sent);
		}

		private static async Task PublishEveryLinkEvent(EventingWorker worker, int departmentId)
		{
			await worker.PersonnelStatusUpdated(departmentId, "1");
			await worker.PersonnelStaffingUpdated(departmentId, "1");
			await worker.UnitStatusUpdated(departmentId, "1");
			await worker.CallsUpdated(departmentId, "1");
			await worker.CallAdded(departmentId, "1");
			await worker.CallClosed(departmentId, "1");
		}

		[Test]
		public async Task Worker_events_reach_the_department_and_only_the_matching_current_share_group()
		{
			var link = Link();
			link.DepartmentShareCalls = link.DepartmentShareUnits = link.DepartmentSharePersonnel = true;
			var world = new World(link);
			var subscriber = world.Connect(Linked);
			await subscriber.Subscribe(HubKind.Eventing);
			var (worker, sent) = Worker(world);

			await PublishEveryLinkEvent(worker, Creator);

			foreach (var (eventName, kind) in WorkerEvents)
			{
				sent.Where(s => s.Method == eventName).Select(s => s.Group)
					.Should().BeEquivalentTo(new[] { Creator.ToString(), await world.CurrentGroup(Creator, kind) }, eventName);
			}
			sent.Where(s => subscriber.Member.Contains(s.Group)).Should().HaveCount(WorkerEvents.Length);
		}

		[Test]
		public async Task Worker_events_reach_only_the_department_when_nobody_subscribed()
		{
			var (worker, sent) = Worker(new World(CreatorSharesUnitsAndPersonnel()));

			await PublishEveryLinkEvent(worker, Creator);

			sent.Select(s => s.Group).Should().OnlyContain(group => group == Creator.ToString());
		}

		[Test]
		public async Task Worker_events_stop_reaching_a_subscription_once_the_link_is_deleted()
		{
			var link = Link();
			link.DepartmentShareCalls = link.DepartmentShareUnits = link.DepartmentSharePersonnel = true;
			var world = new World(link);
			var subscriber = world.Connect(Linked);
			await subscriber.Subscribe(HubKind.Eventing);
			var (worker, sent) = Worker(world);

			await world.Service.DeleteLinkAsync(world.Stored(), CancellationToken.None);
			await PublishEveryLinkEvent(worker, Creator);

			sent.Where(s => subscriber.Member.Contains(s.Group)).Should().BeEmpty();
			sent.Where(s => s.Group == Creator.ToString()).Should().HaveCount(WorkerEvents.Length);
		}

		[Test]
		public async Task Worker_department_wide_events_never_reach_a_share_group()
		{
			var (worker, sent) = Worker(new World(CreatorSharesUnitsAndPersonnel()));

			await worker.DepartmentUpdated(Creator);

			sent.Select(s => s.Group).Should().Equal(Creator.ToString());
		}

		/// <summary>An in-memory cache that runs a hook once, right after the next access-epoch read.</summary>
		private sealed class HookedCache : ICacheProvider
		{
			private readonly InMemoryCacheProvider _inner = new InMemoryCacheProvider();

			public Func<Task> AfterEpochRead { get; set; }

			public async Task<string> GetOrAddStringAsync(string cacheKey, string valueIfAbsent, TimeSpan slidingExpiration)
			{
				var value = await _inner.GetOrAddStringAsync(cacheKey, valueIfAbsent, slidingExpiration);
				var hook = AfterEpochRead;
				AfterEpochRead = null;
				if (hook != null)
					await hook();
				return value;
			}

			public T Retrieve<T>(string cacheKey, Func<T> fallbackFunction, TimeSpan expiration) where T : class => _inner.Retrieve(cacheKey, fallbackFunction, expiration);
			public Task<T> RetrieveAsync<T>(string cacheKey, Func<Task<T>> fallbackFunction, TimeSpan expiration) => _inner.RetrieveAsync(cacheKey, fallbackFunction, expiration);
			public void Remove(string cacheKey) => _inner.Remove(cacheKey);
			public Task<bool> RemoveAsync(string cacheKey) => _inner.RemoveAsync(cacheKey);
			public bool IsConnected() => true;
			public Task<bool> SetStringAsync(string cacheKey, string value, TimeSpan expiration) => _inner.SetStringAsync(cacheKey, value, expiration);
			public Task<string> GetStringAsync(string cacheKey) => _inner.GetStringAsync(cacheKey);
			public Task<T> GetAsync<T>(string cacheKey) where T : class => _inner.GetAsync<T>(cacheKey);
			public Task<long> IncrementAsync(string cacheKey, TimeSpan expiration) => _inner.IncrementAsync(cacheKey, expiration);
			public Task<(bool Succeeded, string Value)> TryGetStringAsync(string cacheKey) => _inner.TryGetStringAsync(cacheKey);
			public Task<long> IncrementByAsync(string cacheKey, long amount, TimeSpan expiration) => _inner.IncrementByAsync(cacheKey, amount, expiration);
		}
	}
}
