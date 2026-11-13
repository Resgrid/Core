using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>Saving road closures and handing the ones in effect to routing; travel estimates by travel mode (M0314).</summary>
	[TestFixture]
	public class RoadClosureServiceTests
	{
		private const int DepartmentId = 7;
		private const string BlockPolygon = "{\"type\":\"Polygon\",\"coordinates\":[[[3.715,51.049],[3.725,51.049],[3.725,51.051],[3.715,51.051],[3.715,51.049]]]}";

		private Mock<IRoadClosuresRepository> _repository;
		private List<RoadClosure> _stored;
		private bool _cacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_cacheEnabled = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = false;

			_stored = new List<RoadClosure>();
			_repository = new Mock<IRoadClosuresRepository>();
			_repository.Setup(r => r.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _stored.ToList());
			_repository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string id, int _) => _stored.FirstOrDefault(c => c.RoadClosureId == id));
			_repository.Setup(r => r.InsertAsync(It.IsAny<RoadClosure>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RoadClosure c, CancellationToken _, bool __) => { _stored.Add(c); return c; });
			_repository.Setup(r => r.UpdateAsync(It.IsAny<RoadClosure>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RoadClosure c, CancellationToken _, bool __) => c);
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.CacheEnabled = _cacheEnabled;
		}

		private RoadClosureService Service() => new RoadClosureService(_repository.Object, null);

		private static RoadClosure Valid() => new RoadClosure
		{
			DepartmentId = DepartmentId,
			Name = "  City marathon - Main Street  ",
			GeometryJson = BlockPolygon,
			AppliesTo = (int)RoadClosureTravelModes.Driving
		};

		[Test]
		public async Task a_new_closure_gets_an_id_trimmed_text_a_normalized_shape_and_who_made_it()
		{
			var closure = Valid();
			closure.Reason = "  ";

			var saved = await Service().SaveClosureAsync(closure, "dispatcher-1");

			saved.RoadClosureId.Should().NotBeNullOrWhiteSpace();
			saved.Name.Should().Be("City marathon - Main Street");
			saved.Reason.Should().BeNull();
			saved.GeometryJson.Should().StartWith("{\"type\":\"FeatureCollection\"");
			RoadClosureShape.ParseOrNull(saved.GeometryJson).Polygons.Should().ContainSingle("the stored shape must route like the drawn one");
			saved.CreatedByUserId.Should().Be("dispatcher-1");
			saved.UpdatedByUserId.Should().Be("dispatcher-1");
		}

		[Test]
		public async Task an_edit_keeps_who_created_it()
		{
			var created = await Service().SaveClosureAsync(Valid(), "dispatcher-1");
			var createdOn = created.CreatedOn;

			var edit = Valid();
			edit.RoadClosureId = created.RoadClosureId;
			var saved = await Service().SaveClosureAsync(edit, "dispatcher-2");

			saved.CreatedByUserId.Should().Be("dispatcher-1");
			saved.CreatedOn.Should().Be(createdOn);
			saved.UpdatedByUserId.Should().Be("dispatcher-2");
		}

		[Test]
		public async Task another_departments_closure_cannot_be_overwritten()
		{
			var edit = Valid();
			edit.RoadClosureId = "someone-elses";

			Func<Task> save = () => Service().SaveClosureAsync(edit, "dispatcher-1");

			await save.Should().ThrowAsync<InvalidOperationException>();
			_repository.Verify(r => r.UpdateAsync(It.IsAny<RoadClosure>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[TestCase(nameof(RoadClosure.Name), "", RoadClosureValidationErrors.NameRequired)]
		[TestCase(nameof(RoadClosure.GeometryJson), "", RoadClosureValidationErrors.ShapeRequired)]
		[TestCase(nameof(RoadClosure.GeometryJson), "{\"type\":\"Point\",\"coordinates\":[3.7,51.0]}", RoadClosureValidationErrors.ShapeInvalid)]
		[TestCase(nameof(RoadClosure.AppliesTo), "0", RoadClosureValidationErrors.TravelModeRequired)]
		[TestCase(nameof(RoadClosure.WindowsJson), "[{\"StartsOn\":\"2026-11-20T10:00:00Z\",\"EndsOn\":\"2026-11-20T08:00:00Z\"}]", RoadClosureValidationErrors.WindowEndsBeforeStart)]
		[TestCase(nameof(RoadClosure.Severity), "5", RoadClosureValidationErrors.InvalidType)]
		public void invalid_closures_are_refused_with_the_field_at_fault(string field, string value, RoadClosureValidationErrors expected)
		{
			var closure = Valid();
			switch (field)
			{
				case nameof(RoadClosure.Name): closure.Name = value; break;
				case nameof(RoadClosure.GeometryJson): closure.GeometryJson = value; break;
				case nameof(RoadClosure.AppliesTo): closure.AppliesTo = int.Parse(value); break;
				case nameof(RoadClosure.WindowsJson): closure.WindowsJson = value; break;
				case nameof(RoadClosure.Severity): closure.Severity = int.Parse(value); break;
			}

			Action validate = () => RoadClosureService.Validate(closure);

			validate.Should().Throw<RoadClosureValidationException>().Where(e => e.Error == expected && e.Field == field);
		}

		[Test]
		public async Task routing_gets_only_enabled_closures_in_effect_now_for_the_trips_mode()
		{
			var now = DateTime.UtcNow;
			_stored.Add(Stored("now-driving", RoadClosureTravelModes.Driving, true, new RoadClosureWindow { StartsOn = now.AddHours(-1), EndsOn = now.AddHours(1) }));
			_stored.Add(Stored("later", RoadClosureTravelModes.Driving, true, new RoadClosureWindow { StartsOn = now.AddHours(5), EndsOn = now.AddHours(8) }));
			_stored.Add(Stored("switched-off", RoadClosureTravelModes.Driving, false));
			_stored.Add(Stored("walkers-only", RoadClosureTravelModes.Walking, true));

			var driving = await Service().GetRoutingClosuresAsync(DepartmentId, UnitTravelModes.Driving, now);
			var walking = await Service().GetRoutingClosuresAsync(DepartmentId, UnitTravelModes.Walking, now);
			var air = await Service().GetRoutingClosuresAsync(DepartmentId, UnitTravelModes.Air, now);

			driving.Select(c => c.ClosureId).Should().Equal("now-driving");
			driving[0].Shape.Polygons.Should().HaveCount(1);
			walking.Select(c => c.ClosureId).Should().Equal("walkers-only");
			air.Should().BeEmpty();
		}

		[Test]
		public async Task switching_a_closure_on_or_off_writes_only_the_switch()
		{
			// PR 585: writing back the whole closure read a moment earlier would undo an edit saved in between.
			_repository.Setup(r => r.SetEnabledAsync(DepartmentId, "mine", false, "dispatcher-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

			(await Service().SetClosureEnabledAsync(DepartmentId, "mine", false, "dispatcher-1")).Should().BeTrue();
			(await Service().SetClosureEnabledAsync(DepartmentId, "someone-elses", false, "dispatcher-1")).Should().BeFalse("no row of this department changed");

			_repository.Verify(r => r.UpdateAsync(It.IsAny<RoadClosure>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_repository.Verify(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task a_read_that_finishes_after_a_save_cannot_put_the_old_closures_back_in_the_cache()
		{
			// PR 585: a cache miss reads the closures, a save lands and clears the cache, then the slow read stores what it
			// read. With one fixed key that brought the old closures back for a day; under the department's version it is
			// stored where nobody reads again.
			SystemBehaviorConfig.CacheEnabled = true;
			var cache = new Resgrid.Tests.Security.Live.InMemoryCacheProvider();
			_stored.Add(Stored("old", RoadClosureTravelModes.Driving, true));

			var saveDuringRead = true;
			_repository.Setup(r => r.GetForDepartmentAsync(DepartmentId)).Returns(async () =>
			{
				var read = _stored.ToList();
				if (saveDuringRead)
				{
					saveDuringRead = false;
					await new RoadClosureService(_repository.Object, cache).SaveClosureAsync(Valid(), "dispatcher-1");
				}

				return read;
			});

			var slow = await new RoadClosureService(_repository.Object, cache).GetClosuresForDepartmentAsync(DepartmentId);
			var next = await new RoadClosureService(_repository.Object, cache).GetClosuresForDepartmentAsync(DepartmentId);
			var cached = await new RoadClosureService(_repository.Object, cache).GetClosuresForDepartmentAsync(DepartmentId);

			slow.Should().ContainSingle("it read before the save");
			next.Should().HaveCount(2, "the next read must see the saved closure");
			cached.Should().HaveCount(2);
			_repository.Verify(r => r.GetForDepartmentAsync(DepartmentId), Times.Exactly(2), "the third read is a cache hit");
		}

		private static RoadClosure Stored(string id, RoadClosureTravelModes modes, bool enabled, params RoadClosureWindow[] windows)
		{
			return new RoadClosure
			{
				RoadClosureId = id,
				DepartmentId = DepartmentId,
				Name = id,
				GeometryJson = BlockPolygon,
				AppliesTo = (int)modes,
				IsEnabled = enabled,
				WindowsJson = windows.Length == 0 ? null : JsonConvert.SerializeObject(windows),
				UpdatedOn = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
			};
		}

		#region Travel estimates (GeoService)

		private Mock<IRoutingService> _routing;
		private Mock<IRoadClosureService> _closures;

		private GeoService Geo(RouteResult route)
		{
			_routing = new Mock<IRoutingService>();
			_routing.Setup(r => r.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(route);
			_closures = new Mock<IRoadClosureService>();
			_closures.Setup(c => c.GetRoutingClosuresAsync(DepartmentId, It.IsAny<UnitTravelModes>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<RoutingClosure> { new RoutingClosure { ClosureId = "c1", Name = "Marathon", Shape = RoadClosureShape.ParseOrNull(BlockPolygon) } });

			return new GeoService(Mock.Of<IGeoLocationProvider>(), Mock.Of<ICallsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IAddressService>(),
				Mock.Of<IMappingService>(), _routing.Object, _closures.Object);
		}

		[Test]
		public async Task air_travel_is_the_straight_line_at_air_speed_without_routing()
		{
			var geo = Geo(new RouteResult { Successful = true, Provider = RoutingProviderNames.Valhalla, DurationSeconds = 999 });

			var estimate = await geo.GetTravelEstimateAsync("51.05,3.70", "51.05,3.74", new TravelLookup
			{
				DepartmentId = DepartmentId,
				Travel = new UnitTravelProfile(UnitTravelModes.Air, 200)
			});

			estimate.Source.Should().Be(EtaSources.Direct);
			estimate.Seconds.Should().BeApproximately(2800 / (200000d / 3600d), 3);
			_routing.Verify(r => r.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_closures.Verify(c => c.GetRoutingClosuresAsync(It.IsAny<int>(), It.IsAny<UnitTravelModes>(), It.IsAny<DateTime>()), Times.Never);
		}

		[Test]
		public async Task a_walking_team_is_routed_on_foot_at_its_own_speed_through_the_walking_closures()
		{
			var geo = Geo(new RouteResult { Successful = true, Provider = RoutingProviderNames.Mapbox, DistanceMeters = 1000, DurationSeconds = 700, ClosureDelaySeconds = 30 });

			var estimate = await geo.GetTravelEstimateAsync("51.05,3.70", "51.05,3.71", new TravelLookup
			{
				DepartmentId = DepartmentId,
				Travel = new UnitTravelProfile(UnitTravelModes.Walking, 4),
				Vehicle = new VehicleProfile { HeightMeters = 3 }
			});

			_routing.Verify(r => r.GetRouteAsync(It.Is<RouteRequest>(q => q.Profile == RoutingProfile.Walking && q.Vehicle == null && q.Closures.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
			_closures.Verify(c => c.GetRoutingClosuresAsync(DepartmentId, UnitTravelModes.Walking, It.IsAny<DateTime>()), Times.Once);
			estimate.Source.Should().Be(EtaSources.Road);
			estimate.Seconds.Should().Be(900 + 30, "1 km at 4 km/h, plus the closure delay");
			estimate.ClosureDelaySeconds.Should().Be(30);
		}

		[Test]
		public async Task a_teams_own_speed_retimes_the_closure_slowdown_but_not_the_detour()
		{
			// PR 585: the provider walked 1 km in 670 s and the closures added 230 s (a 130 s slowdown at its pace, a 100 s
			// detour). At 4 km/h the team takes 900 s, so it is inside the closure 900/670 as long: 130 s becomes ~175 s.
			var geo = Geo(new RouteResult
			{
				Successful = true,
				Provider = RoutingProviderNames.Mapbox,
				DistanceMeters = 1000,
				DurationSeconds = 900,
				ClosureDelaySeconds = 230,
				Closures = new List<RouteClosureEffect>
				{
					new RouteClosureEffect { ClosureId = "c1", Name = "Marathon", Effect = RouteClosureEffects.Slowed, DelaySeconds = 130 },
					new RouteClosureEffect { ClosureId = "c2", Name = "Bridge", Effect = RouteClosureEffects.Crossed, DelaySeconds = 100 }
				}
			});

			var estimate = await geo.GetTravelEstimateAsync("51.05,3.70", "51.05,3.71", new TravelLookup
			{
				DepartmentId = DepartmentId,
				Travel = new UnitTravelProfile(UnitTravelModes.Walking, 4)
			});

			var delay = 100 + (int)Math.Round(130 * 900d / 670d);
			estimate.ClosureDelaySeconds.Should().Be(delay);
			estimate.Seconds.Should().Be(900 + delay);
		}

		[Test]
		public async Task a_driving_unit_keeps_its_vehicle_limits_and_an_estimate_only_answer_is_no_answer()
		{
			var geo = Geo(new RouteResult { Successful = true, Provider = RoutingProviderNames.Estimate, DistanceMeters = 1000, DurationSeconds = 90 });
			var truck = new VehicleProfile { HeightMeters = 3.8 };

			var estimate = await geo.GetTravelEstimateAsync("51.05,3.70", "51.05,3.71", new TravelLookup { DepartmentId = DepartmentId, Vehicle = truck });

			_routing.Verify(r => r.GetRouteAsync(It.Is<RouteRequest>(q => q.Profile == RoutingProfile.Truck && q.Vehicle == truck), It.IsAny<CancellationToken>()), Times.Once);
			estimate.HasValue.Should().BeFalse();
		}

		[Test]
		public async Task a_failed_closure_read_still_returns_a_drive_time()
		{
			var geo = Geo(new RouteResult { Successful = true, Provider = RoutingProviderNames.Valhalla, DistanceMeters = 1000, DurationSeconds = 90 });
			_closures.Setup(c => c.GetRoutingClosuresAsync(It.IsAny<int>(), It.IsAny<UnitTravelModes>(), It.IsAny<DateTime>())).ThrowsAsync(new TimeoutException());

			var estimate = await geo.GetTravelEstimateAsync("51.05,3.70", "51.05,3.71", new TravelLookup { DepartmentId = DepartmentId });

			estimate.Seconds.Should().Be(90);
		}

		#endregion
	}
}
