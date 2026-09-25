extern alias Eventing;

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Eventing::Resgrid.Web.Eventing.Hubs;
using Eventing::Resgrid.Web.Eventing.Hubs.Models;
using Eventing::Resgrid.Web.Eventing.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Web.Eventing
{
	[TestFixture]
	public class GeolocationVisibilityTests
	{
		private const int DepartmentId = 7;
		private const string Viewer = "b0000000-0000-0000-0000-000000000001";
		private const string Subject = "c0000000-0000-0000-0000-000000000001";

		private Mock<ILocationVisibilityService> _visibility;

		[SetUp]
		public void SetUp()
		{
			_visibility = new Mock<ILocationVisibilityService>();
			_visibility.Setup(x => x.GetUnitLocationAudienceAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(LocationAudience.EntireDepartment);
			_visibility.Setup(x => x.GetPersonnelLocationAudienceAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(LocationAudience.EntireDepartment);
			_visibility.Setup(x => x.GetVisibilitySetKeysForViewerAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new string[0]);
			_visibility.Setup(x => x.CanViewUnitLocationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(true);
			_visibility.Setup(x => x.CanViewPersonnelLocationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
		}

		[Test]
		public async Task Unrestricted_unit_location_goes_to_the_department_and_the_unit_trackers()
		{
			var broadcaster = new GeolocationBroadcaster(_visibility.Object);
			var (clients, sentTo, proxy) = CaptureGroupSends();
			var location = new UnitLocationUpdate { DepartmentId = DepartmentId, UnitId = "12", Latitude = 39.5, Longitude = -119.8 };

			await broadcaster.SendUnitLocationAsync(clients.Object, DepartmentId, location);

			sentTo.Single().Should().BeEquivalentTo(new[] { "UnitLocation_12", "7" });
			proxy.Verify(x => x.SendCoreAsync("onUnitLocationUpdated", It.Is<object[]>(args => args.Single() == location), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Restricted_unit_location_goes_only_to_its_visibility_set()
		{
			_visibility.Setup(x => x.GetUnitLocationAudienceAsync(DepartmentId, 12)).ReturnsAsync(LocationAudience.ForVisibilitySet("abc"));
			var broadcaster = new GeolocationBroadcaster(_visibility.Object);

			var groups = await broadcaster.GetUnitLocationGroupsAsync(DepartmentId, "12");

			groups.Should().BeEquivalentTo(new[] { "UnitLocation_12", "GeoVisibility_7_abc" });
			groups.Should().NotContain(GeolocationGroups.Department(DepartmentId));
		}

		[Test]
		public async Task Unit_id_that_cannot_be_checked_never_reaches_the_department()
		{
			var broadcaster = new GeolocationBroadcaster(_visibility.Object);

			(await broadcaster.GetUnitLocationGroupsAsync(DepartmentId, "not-a-unit")).Should().BeEquivalentTo(new[] { "UnitLocation_not-a-unit" });
		}

		[Test]
		public async Task Restricted_personnel_location_also_reaches_the_person_themself()
		{
			_visibility.Setup(x => x.GetPersonnelLocationAudienceAsync(DepartmentId, Subject.ToUpperInvariant())).ReturnsAsync(LocationAudience.ForVisibilitySet("def"));
			var broadcaster = new GeolocationBroadcaster(_visibility.Object);

			var groups = await broadcaster.GetPersonnelLocationGroupsAsync(DepartmentId, Subject.ToUpperInvariant());

			groups.Should().BeEquivalentTo(new[] { $"PersonLocation_7_{Subject}", "GeoVisibility_7_def", $"GeoSelf_7_{Subject}" });
		}

		[Test]
		public async Task Department_map_joins_the_department_its_own_group_and_its_visibility_sets()
		{
			_visibility.Setup(x => x.GetVisibilitySetKeysForViewerAsync(DepartmentId, Viewer)).ReturnsAsync(new[] { "abc", "def" });
			var membership = new GeolocationMembership(_visibility.Object);
			var groups = new Mock<IGroupManager>();
			var connection = new GeolocationConnection("conn-1", DepartmentId, Viewer.ToUpperInvariant());
			connection.SubscribeToDepartmentMap();

			await membership.SyncAsync(groups.Object, connection);

			VerifyJoined(groups, "7", $"GeoSelf_7_{Viewer}", "GeoVisibility_7_abc", "GeoVisibility_7_def");
			groups.Verify(x => x.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Matrix_change_moves_the_viewer_to_the_new_visibility_set()
		{
			_visibility.Setup(x => x.GetVisibilitySetKeysForViewerAsync(DepartmentId, Viewer)).ReturnsAsync(new[] { "abc" });
			var membership = new GeolocationMembership(_visibility.Object);
			var groups = new Mock<IGroupManager>();
			var connection = new GeolocationConnection("conn-1", DepartmentId, Viewer);
			connection.SubscribeToDepartmentMap();
			await membership.SyncAsync(groups.Object, connection);
			groups.Invocations.Clear();

			_visibility.Setup(x => x.GetVisibilitySetKeysForViewerAsync(DepartmentId, Viewer)).ReturnsAsync(new[] { "xyz" });
			await membership.SyncAsync(groups.Object, connection);

			groups.Verify(x => x.AddToGroupAsync("conn-1", "GeoVisibility_7_xyz", It.IsAny<CancellationToken>()), Times.Once);
			groups.Verify(x => x.RemoveFromGroupAsync("conn-1", "GeoVisibility_7_abc", It.IsAny<CancellationToken>()), Times.Once);
			groups.Verify(x => x.AddToGroupAsync("conn-1", "7", It.IsAny<CancellationToken>()), Times.Never, "unchanged groups are left alone");
		}

		[Test]
		public async Task Unit_tracker_leaves_the_unit_group_when_access_is_revoked_and_rejoins_when_restored()
		{
			var membership = new GeolocationMembership(_visibility.Object);
			var groups = new Mock<IGroupManager>();
			var connection = new GeolocationConnection("conn-1", DepartmentId, Viewer);
			connection.SubscribeToUnit(12);
			await membership.SyncAsync(groups.Object, connection);

			_visibility.Setup(x => x.CanViewUnitLocationAsync(DepartmentId, 12, Viewer)).ReturnsAsync(false);
			await membership.SyncAsync(groups.Object, connection);

			_visibility.Setup(x => x.CanViewUnitLocationAsync(DepartmentId, 12, Viewer)).ReturnsAsync(true);
			await membership.SyncAsync(groups.Object, connection);

			groups.Verify(x => x.AddToGroupAsync("conn-1", "UnitLocation_12", It.IsAny<CancellationToken>()), Times.Exactly(2));
			groups.Verify(x => x.RemoveFromGroupAsync("conn-1", "UnitLocation_12", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Periodic_sync_applies_matrix_changes_to_tracked_connections()
		{
			var tracker = new GeolocationConnectionTracker();
			var groups = new Mock<IGroupManager>();
			var hubContext = new Mock<IHubContext<GeolocationHub>>();
			hubContext.SetupGet(x => x.Groups).Returns(groups.Object);
			var sync = new GeolocationVisibilitySync(tracker, new GeolocationMembership(_visibility.Object), hubContext.Object);
			tracker.GetOrAdd("conn-1", DepartmentId, Viewer).SubscribeToDepartmentMap();
			tracker.GetOrAdd("conn-2", DepartmentId, Subject).SubscribeToDepartmentMap();
			tracker.Remove("conn-2");

			_visibility.Setup(x => x.GetVisibilitySetKeysForViewerAsync(DepartmentId, Viewer)).ReturnsAsync(new[] { "abc" });
			await sync.SyncAllAsync(CancellationToken.None);

			groups.Verify(x => x.AddToGroupAsync("conn-1", "GeoVisibility_7_abc", It.IsAny<CancellationToken>()), Times.Once);
			groups.Verify(x => x.AddToGroupAsync("conn-2", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "disconnected connections are dropped");
		}

		[Test]
		public async Task Hub_refuses_to_track_a_unit_the_viewer_may_not_see()
		{
			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitByIdAsync(12)).ReturnsAsync(new Unit { UnitId = 12, DepartmentId = DepartmentId });
			_visibility.Setup(x => x.CanViewUnitLocationAsync(DepartmentId, 12, Viewer)).ReturnsAsync(false);
			var (hub, groups, caller, tracker) = CreateHub(units.Object, Viewer);

			await hub.UnitLocationConnect(12);

			groups.Verify(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			caller.Verify(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
			tracker.Contains("conn-1").Should().BeFalse();
		}

		[Test]
		public async Task Hub_geolocation_connect_joins_the_viewers_groups_and_acknowledges()
		{
			_visibility.Setup(x => x.GetVisibilitySetKeysForViewerAsync(DepartmentId, Viewer)).ReturnsAsync(new[] { "abc" });
			var (hub, groups, caller, tracker) = CreateHub(Mock.Of<IUnitsService>(), Viewer);

			await hub.GeolocationConnect();

			VerifyJoined(groups, "7", $"GeoSelf_7_{Viewer}", "GeoVisibility_7_abc");
			caller.Verify(x => x.SendCoreAsync("onGeolocationConnect", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
			tracker.Contains("conn-1").Should().BeTrue();

			await hub.OnDisconnectedAsync(null);
			tracker.Contains("conn-1").Should().BeFalse();
		}

		[Test]
		public void Hub_location_publish_methods_are_reserved_for_the_internal_publisher()
		{
			var (hub, _, _, _) = CreateHub(Mock.Of<IUnitsService>(), Viewer);

			FluentActions.Awaiting(() => hub.UnitLocationUpdated(new UnitLocationUpdate { DepartmentId = 99, UnitId = "12" }))
				.Should().ThrowAsync<HubException>();
		}

		private static void VerifyJoined(Mock<IGroupManager> groups, params string[] expected)
		{
			var joined = groups.Invocations
				.Where(x => x.Method.Name == nameof(IGroupManager.AddToGroupAsync))
				.Select(x => (string)x.Arguments[1])
				.ToList();

			joined.Should().BeEquivalentTo(expected);
		}

		private static (Mock<IHubClients<IClientProxy>> Clients, List<IReadOnlyList<string>> SentTo, Mock<IClientProxy> Proxy) CaptureGroupSends()
		{
			var proxy = new Mock<IClientProxy>();
			var sentTo = new List<IReadOnlyList<string>>();
			var clients = new Mock<IHubClients<IClientProxy>>();
			clients.Setup(x => x.Groups(It.IsAny<IReadOnlyList<string>>()))
				.Callback<IReadOnlyList<string>>(groups => sentTo.Add(groups))
				.Returns(proxy.Object);

			return (clients, sentTo, proxy);
		}

		private (GeolocationHub Hub, Mock<IGroupManager> Groups, Mock<ISingleClientProxy> Caller, GeolocationConnectionTracker Tracker) CreateHub(
			IUnitsService unitsService, string userId)
		{
			var tracker = new GeolocationConnectionTracker();
			var groups = new Mock<IGroupManager>();
			var caller = new Mock<ISingleClientProxy>();
			var clients = new Mock<IHubCallerClients>();
			clients.SetupGet(x => x.Caller).Returns(caller.Object);

			var context = new Mock<HubCallerContext>();
			context.SetupGet(x => x.ConnectionId).Returns("conn-1");
			context.SetupGet(x => x.ConnectionAborted).Returns(CancellationToken.None);
			context.SetupGet(x => x.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, userId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}, "test")));

			var hub = new GeolocationHub(unitsService, Mock.Of<IUsersService>(), Mock.Of<IDepartmentsService>(), _visibility.Object, tracker,
				new GeolocationMembership(_visibility.Object), new GeolocationBroadcaster(_visibility.Object))
			{
				Context = context.Object,
				Groups = groups.Object,
				Clients = clients.Object
			};

			return (hub, groups, caller, tracker);
		}
	}
}
