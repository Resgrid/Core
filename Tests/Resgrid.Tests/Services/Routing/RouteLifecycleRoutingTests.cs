using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>Route lifecycle fixes from the MapLibre routing plan (§10.1 RD3, RD6–RD9; §10.4 deviations).</summary>
	[TestFixture]
	public class RouteLifecycleRoutingTests
	{
		private Mock<IRoutePlansRepository> _plans;
		private Mock<IRouteStopsRepository> _stops;
		private Mock<IRouteInstancesRepository> _instances;
		private Mock<IRouteInstanceStopsRepository> _instanceStops;
		private Mock<IRouteDeviationsRepository> _deviations;
		private Mock<IUnitLocationsDocRepository> _locations;
		private Mock<IDepartmentSettingsService> _settings;
		private RouteService _service;
		private List<RouteDeviation> _recorded;
		private List<RouteInstanceStop> _savedInstanceStops;

		[SetUp]
		public void SetUp()
		{
			_plans = new Mock<IRoutePlansRepository>();
			_stops = new Mock<IRouteStopsRepository>();
			_instances = new Mock<IRouteInstancesRepository>();
			_instanceStops = new Mock<IRouteInstanceStopsRepository>();
			_deviations = new Mock<IRouteDeviationsRepository>();
			_locations = new Mock<IUnitLocationsDocRepository>();
			_settings = new Mock<IDepartmentSettingsService>();
			_recorded = new List<RouteDeviation>();
			_savedInstanceStops = new List<RouteInstanceStop>();

			_settings.Setup(s => s.GetMappingNavigationConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new MappingNavigationConfig());
			_deviations.Setup(d => d.SaveOrUpdateAsync(It.IsAny<RouteDeviation>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteDeviation d, CancellationToken _, bool __) => { _recorded.Add(d); return d; });
			_instances.Setup(i => i.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstance i, CancellationToken _, bool __) => { i.RouteInstanceId ??= "new-instance"; return i; });
			_instanceStops.Setup(i => i.SaveOrUpdateAsync(It.IsAny<RouteInstanceStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstanceStop s, CancellationToken _, bool __) => { _savedInstanceStops.Add(s); return s; });
			_stops.Setup(s => s.SaveOrUpdateAsync(It.IsAny<RouteStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteStop s, CancellationToken _, bool __) => { s.RouteStopId ??= "new-stop"; return s; });
			_plans.Setup(p => p.SaveOrUpdateAsync(It.IsAny<RoutePlan>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RoutePlan p, CancellationToken _, bool __) => p);

			_service = new RouteService(_plans.Object, _stops.Object, new Mock<IRouteSchedulesRepository>().Object, _instances.Object,
				_instanceStops.Object, _deviations.Object, _locations.Object, _settings.Object);
		}

		[Test]
		public async Task start_sets_planned_arrivals_and_snapshots_the_route()
		{
			_plans.Setup(p => p.GetByIdAsync("plan")).ReturnsAsync(new RoutePlan { RoutePlanId = "plan", DepartmentId = 7, RouteDirectionsJson = "{\"legs\":[]}" });
			_stops.Setup(s => s.GetStopsByRoutePlanIdAsync("plan")).ReturnsAsync(new List<RouteStop>
			{
				new RouteStop { RouteStopId = "a", StopOrder = 1, PlannedOffsetSeconds = 600 },
				new RouteStop { RouteStopId = "b", StopOrder = 2 }
			});
			_instances.Setup(i => i.GetActiveInstancesByUnitIdAsync(30)).ReturnsAsync(new List<RouteInstance>());

			var started = await _service.StartRouteAsync("plan", 30, "user");

			started.PlannedDirectionsJson.Should().Be("{\"legs\":[]}");
			var a = _savedInstanceStops.Single(s => s.RouteStopId == "a");
			a.EstimatedArrivalOn.Should().BeCloseTo(started.ActualStartOn.Value.AddSeconds(600), TimeSpan.FromSeconds(1));
			_savedInstanceStops.Single(s => s.RouteStopId == "b").EstimatedArrivalOn.Should().BeNull();
		}

		[Test]
		public async Task a_stale_plan_gives_no_planned_arrivals()
		{
			_plans.Setup(p => p.GetByIdAsync("plan")).ReturnsAsync(new RoutePlan { RoutePlanId = "plan", DepartmentId = 7, RouteIsStale = true, RouteDirectionsJson = "{}" });
			_stops.Setup(s => s.GetStopsByRoutePlanIdAsync("plan")).ReturnsAsync(new List<RouteStop> { new RouteStop { RouteStopId = "a", StopOrder = 1, PlannedOffsetSeconds = 600 } });
			_instances.Setup(i => i.GetActiveInstancesByUnitIdAsync(30)).ReturnsAsync(new List<RouteInstance>());

			var started = await _service.StartRouteAsync("plan", 30, "user");

			started.PlannedDirectionsJson.Should().BeNull();
			_savedInstanceStops.Single().EstimatedArrivalOn.Should().BeNull();
		}

		[Test]
		public void a_paused_route_blocks_a_second_start()
		{
			_plans.Setup(p => p.GetByIdAsync("plan")).ReturnsAsync(new RoutePlan { RoutePlanId = "plan", DepartmentId = 7 });
			_instances.Setup(i => i.GetActiveInstancesByUnitIdAsync(30)).ReturnsAsync(new List<RouteInstance>());
			_instances.Setup(i => i.GetActiveInstancesByDepartmentIdAsync(7)).ReturnsAsync(new List<RouteInstance>
			{
				new RouteInstance { RouteInstanceId = "paused", UnitId = 30, DepartmentId = 7, Status = (int)RouteInstanceStatus.Paused }
			});
			_instances.Setup(i => i.GetByIdAsync("paused")).ReturnsAsync(new RouteInstance { RouteInstanceId = "paused", UnitId = 30, DepartmentId = 7, Status = (int)RouteInstanceStatus.Paused });

			Func<Task> start = () => _service.StartRouteAsync("plan", 30, "user");

			start.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task active_or_paused_prefers_in_progress_and_finds_paused()
		{
			_instances.Setup(i => i.GetActiveInstancesByUnitIdAsync(30)).ReturnsAsync(new List<RouteInstance>());
			_instances.Setup(i => i.GetActiveInstancesByDepartmentIdAsync(7)).ReturnsAsync(new List<RouteInstance>
			{
				new RouteInstance { RouteInstanceId = "other", UnitId = 31, DepartmentId = 7, Status = (int)RouteInstanceStatus.Paused },
				new RouteInstance { RouteInstanceId = "paused", UnitId = 30, DepartmentId = 7, Status = (int)RouteInstanceStatus.Paused }
			});
			_instances.Setup(i => i.GetByIdAsync("paused")).ReturnsAsync(new RouteInstance { RouteInstanceId = "paused", UnitId = 30, DepartmentId = 7, Status = 5 });

			(await _service.GetActiveOrPausedInstanceForUnitAsync(7, 30)).RouteInstanceId.Should().Be("paused");
			(await _service.GetActiveInstanceForUnitAsync(30)).Should().BeNull();
		}

		[Test]
		public async Task a_stop_added_to_a_running_plan_joins_the_instance_and_marks_the_plan_stale()
		{
			var plan = new RoutePlan { RoutePlanId = "plan", DepartmentId = 7 };
			var running = new RouteInstance { RouteInstanceId = "inst", RoutePlanId = "plan", Status = (int)RouteInstanceStatus.InProgress, StopsTotal = 2 };
			_plans.Setup(p => p.GetByIdAsync("plan")).ReturnsAsync(plan);
			_instances.Setup(i => i.GetInstancesByRoutePlanIdAsync("plan")).ReturnsAsync(new List<RouteInstance>
			{
				running,
				new RouteInstance { RouteInstanceId = "done", RoutePlanId = "plan", Status = (int)RouteInstanceStatus.Completed }
			});
			_instanceStops.Setup(i => i.GetStopsByRouteInstanceIdAsync(It.IsAny<string>())).ReturnsAsync(new List<RouteInstanceStop>());

			await _service.SaveRouteStopAsync(new RouteStop { RoutePlanId = "plan", StopOrder = 3, Name = "C" });

			plan.RouteIsStale.Should().BeTrue();
			_savedInstanceStops.Should().ContainSingle(s => s.RouteInstanceId == "inst" && s.RouteStopId == "new-stop" && s.Status == 0);
			running.StopsTotal.Should().Be(3);
		}

		[Test]
		public async Task deleting_a_stop_closes_the_gap()
		{
			var stops = new List<RouteStop>
			{
				new RouteStop { RouteStopId = "a", RoutePlanId = "plan", StopOrder = 1 },
				new RouteStop { RouteStopId = "b", RoutePlanId = "plan", StopOrder = 2 },
				new RouteStop { RouteStopId = "c", RoutePlanId = "plan", StopOrder = 3 }
			};
			_stops.Setup(s => s.GetByIdAsync("b")).ReturnsAsync(stops[1]);
			_stops.Setup(s => s.GetStopsByRoutePlanIdAsync("plan")).ReturnsAsync(stops);

			await _service.DeleteRouteStopAsync("b");

			stops[0].StopOrder.Should().Be(1);
			stops[2].StopOrder.Should().Be(2);
		}

		[Test]
		public async Task a_late_check_in_records_a_late_arrival_and_skipped_required_stops_are_out_of_order()
		{
			_instanceStops.Setup(i => i.GetByIdAsync("ib")).ReturnsAsync(new RouteInstanceStop
			{
				RouteInstanceStopId = "ib", RouteInstanceId = "inst", RouteStopId = "b", StopOrder = 2, Status = 0,
				EstimatedArrivalOn = DateTime.UtcNow.AddMinutes(-30)
			});
			_instances.Setup(i => i.GetByIdAsync("inst")).ReturnsAsync(new RouteInstance { RouteInstanceId = "inst", RoutePlanId = "plan", DepartmentId = 7, Status = 1 });
			_instanceStops.Setup(i => i.GetStopsByRouteInstanceIdAsync("inst")).ReturnsAsync(new List<RouteInstanceStop>
			{
				new RouteInstanceStop { RouteInstanceStopId = "ia", RouteStopId = "a", StopOrder = 1, Status = 0 },
				new RouteInstanceStop { RouteInstanceStopId = "ib", RouteStopId = "b", StopOrder = 2, Status = 1 }
			});
			_stops.Setup(s => s.GetStopsByRoutePlanIdAsync("plan")).ReturnsAsync(new List<RouteStop>
			{
				new RouteStop { RouteStopId = "a", Name = "Alpha", Priority = (int)RouteStopPriority.Normal },
				new RouteStop { RouteStopId = "b", Name = "Bravo" }
			});

			await _service.CheckInAtStopAsync("ib", 39.5m, -119.8m, RouteStopCheckInType.Manual);

			_recorded.Should().Contain(d => d.DeviationType == (int)RouteDeviationTypes.LateArrival);
			_recorded.Should().Contain(d => d.DeviationType == (int)RouteDeviationTypes.OutOfOrder && d.Notes == "Alpha");
		}

		[Test]
		public async Task time_deviations_can_be_turned_off_and_optional_stops_are_not_out_of_order()
		{
			_settings.Setup(s => s.GetMappingNavigationConfigAsync(7, It.IsAny<bool>())).ReturnsAsync(new MappingNavigationConfig { TimeDeviationsOff = true });
			_instanceStops.Setup(i => i.GetByIdAsync("ib")).ReturnsAsync(new RouteInstanceStop
			{
				RouteInstanceStopId = "ib", RouteInstanceId = "inst", RouteStopId = "b", StopOrder = 2, Status = 0,
				EstimatedArrivalOn = DateTime.UtcNow.AddMinutes(-30)
			});
			_instances.Setup(i => i.GetByIdAsync("inst")).ReturnsAsync(new RouteInstance { RouteInstanceId = "inst", RoutePlanId = "plan", DepartmentId = 7, Status = 1 });
			_instanceStops.Setup(i => i.GetStopsByRouteInstanceIdAsync("inst")).ReturnsAsync(new List<RouteInstanceStop>
			{
				new RouteInstanceStop { RouteInstanceStopId = "ia", RouteStopId = "a", StopOrder = 1, Status = 0 }
			});
			_stops.Setup(s => s.GetStopsByRoutePlanIdAsync("plan")).ReturnsAsync(new List<RouteStop>
			{
				new RouteStop { RouteStopId = "a", Name = "Alpha", Priority = (int)RouteStopPriority.Optional }
			});

			await _service.CheckInAtStopAsync("ib", 39.5m, -119.8m, RouteStopCheckInType.Manual);

			_recorded.Should().BeEmpty();
		}

		[Test]
		public async Task ending_a_route_stores_the_driven_track_and_missed_required_stops()
		{
			var start = DateTime.UtcNow.AddHours(-1);
			_instances.Setup(i => i.GetByIdAsync("inst")).ReturnsAsync(new RouteInstance
			{
				RouteInstanceId = "inst", RoutePlanId = "plan", DepartmentId = 7, UnitId = 30, Status = 1, ActualStartOn = start
			});
			_locations.Setup(l => l.GetLocationsForDepartmentInRangeAsync(7, start, It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<UnitsLocation>
				{
					new UnitsLocation { UnitId = 30, Latitude = 39.52m, Longitude = -119.80m, Timestamp = start.AddMinutes(20) },
					new UnitsLocation { UnitId = 30, Latitude = 39.50m, Longitude = -119.80m, Timestamp = start.AddMinutes(1) },
					new UnitsLocation { UnitId = 30, Latitude = 39.51m, Longitude = -119.80m, Timestamp = start.AddMinutes(10) }
				});
			_instanceStops.Setup(i => i.GetStopsByRouteInstanceIdAsync("inst")).ReturnsAsync(new List<RouteInstanceStop>
			{
				new RouteInstanceStop { RouteInstanceStopId = "ia", RouteStopId = "a", StopOrder = 1, Status = 2 },
				new RouteInstanceStop { RouteInstanceStopId = "ib", RouteStopId = "b", StopOrder = 2, Status = 0 },
				new RouteInstanceStop { RouteInstanceStopId = "ic", RouteStopId = "c", StopOrder = 3, Status = 0 }
			});
			_stops.Setup(s => s.GetStopsByRoutePlanIdAsync("plan")).ReturnsAsync(new List<RouteStop>
			{
				new RouteStop { RouteStopId = "a", Name = "A" },
				new RouteStop { RouteStopId = "b", Name = "B", Latitude = 39.6m, Longitude = -119.7m },
				new RouteStop { RouteStopId = "c", Name = "C", Priority = (int)RouteStopPriority.Optional }
			});

			var ended = await _service.EndRouteAsync("inst", "user");

			ended.ActualRouteGeometry.Should().Be("{\"type\":\"LineString\",\"coordinates\":[[-119.8,39.5],[-119.8,39.52]]}");
			ended.TotalDistanceMeters.Should().BeApproximately(2224, 5);
			_recorded.Should().ContainSingle(d => d.DeviationType == (int)RouteDeviationTypes.MissedStop && d.Notes == "B" && d.Latitude == 39.6m);
		}
	}
}
