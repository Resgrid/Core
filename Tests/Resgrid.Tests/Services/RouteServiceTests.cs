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
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class RouteServiceTests
	{
		private Mock<IRoutePlansRepository> _plansRepo;
		private Mock<IRouteStopsRepository> _stopsRepo;
		private Mock<IRouteSchedulesRepository> _schedulesRepo;
		private Mock<IRouteInstancesRepository> _instancesRepo;
		private Mock<IRouteInstanceStopsRepository> _instanceStopsRepo;
		private Mock<IRouteDeviationsRepository> _deviationsRepo;
		private RouteService _service;

		[SetUp]
		public void SetUp()
		{
			_plansRepo = new Mock<IRoutePlansRepository>();
			_stopsRepo = new Mock<IRouteStopsRepository>();
			_schedulesRepo = new Mock<IRouteSchedulesRepository>();
			_instancesRepo = new Mock<IRouteInstancesRepository>();
			_instanceStopsRepo = new Mock<IRouteInstanceStopsRepository>();
			_deviationsRepo = new Mock<IRouteDeviationsRepository>();
			_service = new RouteService(
				_plansRepo.Object, _stopsRepo.Object, _schedulesRepo.Object,
				_instancesRepo.Object, _instanceStopsRepo.Object, _deviationsRepo.Object);
		}

		#region Route Plan CRUD

		[Test]
		public async Task SaveRoutePlanAsync_ShouldCallRepository()
		{
			var plan = new RoutePlan { RoutePlanId = "rp1", Name = "Test Route" };
			_plansRepo.Setup(x => x.SaveOrUpdateAsync(plan, It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(plan);

			var result = await _service.SaveRoutePlanAsync(plan);

			result.Should().NotBeNull();
			result.RoutePlanId.Should().Be("rp1");
			_plansRepo.Verify(x => x.SaveOrUpdateAsync(plan, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task GetRoutePlanByIdAsync_ShouldReturnPlan()
		{
			var plan = new RoutePlan { RoutePlanId = "rp1" };
			_plansRepo.Setup(x => x.GetByIdAsync("rp1")).ReturnsAsync(plan);

			var result = await _service.GetRoutePlanByIdAsync("rp1");

			result.Should().NotBeNull();
			result.RoutePlanId.Should().Be("rp1");
		}

		[Test]
		public async Task GetRoutePlansForDepartmentAsync_ShouldReturnNonDeleted()
		{
			var plans = new List<RoutePlan>
			{
				new RoutePlan { RoutePlanId = "rp1", DepartmentId = 1, IsDeleted = false },
				new RoutePlan { RoutePlanId = "rp2", DepartmentId = 1, IsDeleted = true },
				new RoutePlan { RoutePlanId = "rp3", DepartmentId = 1, IsDeleted = false }
			};
			_plansRepo.Setup(x => x.GetRoutePlansByDepartmentIdAsync(1)).ReturnsAsync(plans);

			var result = await _service.GetRoutePlansForDepartmentAsync(1);

			result.Should().HaveCount(2);
			result.All(p => !p.IsDeleted).Should().BeTrue();
		}

		[Test]
		public async Task GetRoutePlansForUnitAsync_ShouldFilterByUnit()
		{
			var plans = new List<RoutePlan>
			{
				new RoutePlan { RoutePlanId = "rp1", UnitId = 10, IsDeleted = false }
			};
			_plansRepo.Setup(x => x.GetRoutePlansByUnitIdAsync(10)).ReturnsAsync(plans);

			var result = await _service.GetRoutePlansForUnitAsync(10);

			result.Should().HaveCount(1);
			result[0].UnitId.Should().Be(10);
		}

		[Test]
		public async Task DeleteRoutePlanAsync_ShouldSoftDelete()
		{
			var plan = new RoutePlan { RoutePlanId = "rp1", IsDeleted = false };
			_plansRepo.Setup(x => x.GetByIdAsync("rp1")).ReturnsAsync(plan);
			_plansRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RoutePlan>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RoutePlan p, CancellationToken ct, bool b) => p);

			var result = await _service.DeleteRoutePlanAsync("rp1");

			result.Should().BeTrue();
			plan.IsDeleted.Should().BeTrue();
		}

		[Test]
		public async Task DeleteRoutePlanAsync_NotFound_ShouldReturnFalse()
		{
			_plansRepo.Setup(x => x.GetByIdAsync("rp999")).ReturnsAsync((RoutePlan)null);

			var result = await _service.DeleteRoutePlanAsync("rp999");

			result.Should().BeFalse();
		}

		#endregion

		#region Route Stop CRUD

		[Test]
		public async Task SaveRouteStopAsync_ShouldCallRepository()
		{
			var stop = new RouteStop { RouteStopId = "rs1", Name = "Stop 1" };
			_stopsRepo.Setup(x => x.SaveOrUpdateAsync(stop, It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(stop);

			var result = await _service.SaveRouteStopAsync(stop);

			result.Should().NotBeNull();
			_stopsRepo.Verify(x => x.SaveOrUpdateAsync(stop, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task GetStopsForPlanAsync_ShouldReturnOrderedNonDeleted()
		{
			var stops = new List<RouteStop>
			{
				new RouteStop { RouteStopId = "rs2", StopOrder = 1, IsDeleted = false },
				new RouteStop { RouteStopId = "rs1", StopOrder = 0, IsDeleted = false },
				new RouteStop { RouteStopId = "rs3", StopOrder = 2, IsDeleted = true }
			};
			_stopsRepo.Setup(x => x.GetStopsByRoutePlanIdAsync("rp1")).ReturnsAsync(stops);

			var result = await _service.GetRouteStopsForPlanAsync("rp1");

			result.Should().HaveCount(2);
			result[0].RouteStopId.Should().Be("rs1");
			result[1].RouteStopId.Should().Be("rs2");
		}

		[Test]
		public async Task ReorderStopsAsync_ShouldUpdateStopOrders()
		{
			var stops = new List<RouteStop>
			{
				new RouteStop { RouteStopId = "rs1", StopOrder = 0, IsDeleted = false },
				new RouteStop { RouteStopId = "rs2", StopOrder = 1, IsDeleted = false }
			};
			_stopsRepo.Setup(x => x.GetStopsByRoutePlanIdAsync("rp1")).ReturnsAsync(stops);
			_stopsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteStop s, CancellationToken ct, bool b) => s);

			var result = await _service.ReorderRouteStopsAsync("rp1", new List<string> { "rs2", "rs1" });

			result.Should().BeTrue();
			stops.First(s => s.RouteStopId == "rs2").StopOrder.Should().Be(0);
			stops.First(s => s.RouteStopId == "rs1").StopOrder.Should().Be(1);
		}

		[Test]
		public async Task DeleteRouteStopAsync_ShouldSoftDelete()
		{
			var stop = new RouteStop { RouteStopId = "rs1", IsDeleted = false };
			_stopsRepo.Setup(x => x.GetByIdAsync("rs1")).ReturnsAsync(stop);
			_stopsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteStop s, CancellationToken ct, bool b) => s);

			var result = await _service.DeleteRouteStopAsync("rs1");

			result.Should().BeTrue();
			stop.IsDeleted.Should().BeTrue();
		}

		#endregion

		#region Route Instance Lifecycle

		[Test]
		public async Task StartRouteAsync_ShouldCreateInstance_WithStops()
		{
			var plan = new RoutePlan { RoutePlanId = "rp1", DepartmentId = 1 };
			var stops = new List<RouteStop>
			{
				new RouteStop { RouteStopId = "rs1", StopOrder = 0, IsDeleted = false },
				new RouteStop { RouteStopId = "rs2", StopOrder = 1, IsDeleted = false }
			};

			_plansRepo.Setup(x => x.GetByIdAsync("rp1")).ReturnsAsync(plan);
			_stopsRepo.Setup(x => x.GetStopsByRoutePlanIdAsync("rp1")).ReturnsAsync(stops);
			_instancesRepo.Setup(x => x.GetActiveInstancesByUnitIdAsync(10)).ReturnsAsync(new List<RouteInstance>());
			_instancesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstance i, CancellationToken ct, bool b) => i);
			_instanceStopsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstanceStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstanceStop s, CancellationToken ct, bool b) => s);

			var result = await _service.StartRouteAsync("rp1", 10, "user1");

			result.Should().NotBeNull();
			result.Status.Should().Be((int)RouteInstanceStatus.InProgress);
			result.StopsTotal.Should().Be(2);
			_instanceStopsRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstanceStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
		}

		[Test]
		public void StartRouteAsync_AlreadyActive_ShouldThrow()
		{
			var existing = new List<RouteInstance> { new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.InProgress } };
			_instancesRepo.Setup(x => x.GetActiveInstancesByUnitIdAsync(10)).ReturnsAsync(existing);

			Func<Task> act = async () => await _service.StartRouteAsync("rp1", 10, "user1");

			act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task EndRouteAsync_ShouldSetCompleted()
		{
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.InProgress, ActualStartOn = DateTime.UtcNow.AddHours(-1) };
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);
			_instancesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstance i, CancellationToken ct, bool b) => i);

			var result = await _service.EndRouteAsync("ri1", "user1");

			result.Status.Should().Be((int)RouteInstanceStatus.Completed);
			result.ActualEndOn.Should().NotBeNull();
			result.TotalDurationSeconds.Should().BeGreaterThan(0);
		}

		[Test]
		public void EndRouteAsync_NotInProgress_ShouldThrow()
		{
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.Completed };
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);

			Func<Task> act = async () => await _service.EndRouteAsync("ri1", "user1");

			act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task PauseRouteAsync_ShouldSetPaused()
		{
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.InProgress };
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);
			_instancesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstance i, CancellationToken ct, bool b) => i);

			var result = await _service.PauseRouteAsync("ri1", "user1");

			result.Status.Should().Be((int)RouteInstanceStatus.Paused);
		}

		[Test]
		public void PauseRouteAsync_NotInProgress_ShouldThrow()
		{
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.Paused };
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);

			Func<Task> act = async () => await _service.PauseRouteAsync("ri1", "user1");

			act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task ResumeRouteAsync_ShouldSetInProgress()
		{
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.Paused };
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);
			_instancesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstance i, CancellationToken ct, bool b) => i);

			var result = await _service.ResumeRouteAsync("ri1", "user1");

			result.Status.Should().Be((int)RouteInstanceStatus.InProgress);
		}

		[Test]
		public async Task CancelRouteAsync_ShouldSetCancelled()
		{
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.InProgress };
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);
			_instancesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstance i, CancellationToken ct, bool b) => i);

			var result = await _service.CancelRouteAsync("ri1", "user1", "Testing cancel");

			result.Status.Should().Be((int)RouteInstanceStatus.Cancelled);
			result.Notes.Should().Be("Testing cancel");
		}

		[Test]
		public async Task GetActiveInstanceForUnitAsync_NoActive_ShouldReturnNull()
		{
			_instancesRepo.Setup(x => x.GetActiveInstancesByUnitIdAsync(10)).ReturnsAsync(new List<RouteInstance>());

			var result = await _service.GetActiveInstanceForUnitAsync(10);

			result.Should().BeNull();
		}

		#endregion

		#region Check-in/Check-out

		[Test]
		public async Task CheckInAtStopAsync_ShouldCreateCheckIn()
		{
			var instanceStop = new RouteInstanceStop { RouteInstanceStopId = "ris1", RouteInstanceId = "ri1", Status = 0 };
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.InProgress };
			_instanceStopsRepo.Setup(x => x.GetByIdAsync("ris1")).ReturnsAsync(instanceStop);
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);
			_instanceStopsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstanceStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstanceStop s, CancellationToken ct, bool b) => s);

			var result = await _service.CheckInAtStopAsync("ris1", 40.7128m, -74.0060m, RouteStopCheckInType.Manual);

			result.Status.Should().Be(1); // CheckedIn
			result.CheckInOn.Should().NotBeNull();
			result.CheckInLatitude.Should().Be(40.7128m);
		}

		[Test]
		public void CheckInAtStopAsync_InvalidInstance_ShouldThrow()
		{
			var instanceStop = new RouteInstanceStop { RouteInstanceStopId = "ris1", RouteInstanceId = "ri1" };
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.Completed };
			_instanceStopsRepo.Setup(x => x.GetByIdAsync("ris1")).ReturnsAsync(instanceStop);
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);

			Func<Task> act = async () => await _service.CheckInAtStopAsync("ris1", 40.7128m, -74.0060m, RouteStopCheckInType.Manual);

			act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task CheckOutFromStopAsync_ShouldSetCheckOutTime()
		{
			var instanceStop = new RouteInstanceStop { RouteInstanceStopId = "ris1", RouteInstanceId = "ri1", Status = 1, CheckInOn = DateTime.UtcNow.AddMinutes(-5) };
			var instance = new RouteInstance { RouteInstanceId = "ri1", Status = (int)RouteInstanceStatus.InProgress, StopsCompleted = 0 };
			_instanceStopsRepo.Setup(x => x.GetByIdAsync("ris1")).ReturnsAsync(instanceStop);
			_instancesRepo.Setup(x => x.GetByIdAsync("ri1")).ReturnsAsync(instance);
			_instanceStopsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstanceStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstanceStop s, CancellationToken ct, bool b) => s);
			_instancesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstance i, CancellationToken ct, bool b) => i);

			var result = await _service.CheckOutFromStopAsync("ris1", 40.7128m, -74.0060m);

			result.Status.Should().Be(2); // CheckedOut
			result.CheckOutOn.Should().NotBeNull();
			result.DwellSeconds.Should().BeGreaterThan(0);
		}

		[Test]
		public void CheckOutFromStopAsync_NotCheckedIn_ShouldThrow()
		{
			var instanceStop = new RouteInstanceStop { RouteInstanceStopId = "ris1", Status = 0 }; // Pending
			_instanceStopsRepo.Setup(x => x.GetByIdAsync("ris1")).ReturnsAsync(instanceStop);

			Func<Task> act = async () => await _service.CheckOutFromStopAsync("ris1", 40.7128m, -74.0060m);

			act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task SkipStopAsync_ShouldSetSkippedWithReason()
		{
			var instanceStop = new RouteInstanceStop { RouteInstanceStopId = "ris1", Status = 0 };
			_instanceStopsRepo.Setup(x => x.GetByIdAsync("ris1")).ReturnsAsync(instanceStop);
			_instanceStopsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RouteInstanceStop>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstanceStop s, CancellationToken ct, bool b) => s);

			var result = await _service.SkipStopAsync("ris1", "Road closed");

			result.Status.Should().Be(3); // Skipped
			result.SkipReason.Should().Be("Road closed");
		}

		#endregion

		#region Geofence

		[Test]
		public async Task CheckGeofenceProximityAsync_WithinRadius_ShouldReturnStop()
		{
			var instance = new RouteInstance { RouteInstanceId = "ri1", RoutePlanId = "rp1" };
			var plan = new RoutePlan { RoutePlanId = "rp1", GeofenceRadiusMeters = 200 };
			var instanceStop = new RouteInstanceStop { RouteInstanceStopId = "ris1", RouteStopId = "rs1", Status = 0, StopOrder = 0 };
			var routeStop = new RouteStop { RouteStopId = "rs1", Latitude = 40.7128m, Longitude = -74.0060m };

			_instancesRepo.Setup(x => x.GetActiveInstancesByUnitIdAsync(10)).ReturnsAsync(new List<RouteInstance> { instance });
			_plansRepo.Setup(x => x.GetByIdAsync("rp1")).ReturnsAsync(plan);
			_instanceStopsRepo.Setup(x => x.GetStopsByRouteInstanceIdAsync("ri1")).ReturnsAsync(new List<RouteInstanceStop> { instanceStop });
			_stopsRepo.Setup(x => x.GetByIdAsync("rs1")).ReturnsAsync(routeStop);

			// Position very close to the stop
			var result = await _service.CheckGeofenceProximityAsync(10, 40.7128m, -74.0060m);

			result.Should().NotBeNull();
			result.RouteInstanceStopId.Should().Be("ris1");
		}

		[Test]
		public async Task CheckGeofenceProximityAsync_OutsideRadius_ShouldReturnNull()
		{
			var instance = new RouteInstance { RouteInstanceId = "ri1", RoutePlanId = "rp1" };
			var plan = new RoutePlan { RoutePlanId = "rp1", GeofenceRadiusMeters = 50 };
			var instanceStop = new RouteInstanceStop { RouteInstanceStopId = "ris1", RouteStopId = "rs1", Status = 0, StopOrder = 0 };
			var routeStop = new RouteStop { RouteStopId = "rs1", Latitude = 40.7128m, Longitude = -74.0060m };

			_instancesRepo.Setup(x => x.GetActiveInstancesByUnitIdAsync(10)).ReturnsAsync(new List<RouteInstance> { instance });
			_plansRepo.Setup(x => x.GetByIdAsync("rp1")).ReturnsAsync(plan);
			_instanceStopsRepo.Setup(x => x.GetStopsByRouteInstanceIdAsync("ri1")).ReturnsAsync(new List<RouteInstanceStop> { instanceStop });
			_stopsRepo.Setup(x => x.GetByIdAsync("rs1")).ReturnsAsync(routeStop);

			// Position far from the stop (different city)
			var result = await _service.CheckGeofenceProximityAsync(10, 34.0522m, -118.2437m);

			result.Should().BeNull();
		}

		[Test]
		public async Task CheckGeofenceProximityAsync_NoActiveRoute_ShouldReturnNull()
		{
			_instancesRepo.Setup(x => x.GetActiveInstancesByUnitIdAsync(10)).ReturnsAsync(new List<RouteInstance>());

			var result = await _service.CheckGeofenceProximityAsync(10, 40.7128m, -74.0060m);

			result.Should().BeNull();
		}

		#endregion

		#region Progress

		[Test]
		public async Task GetRouteProgressAsync_ShouldCalculateCompletedStops()
		{
			var stops = new List<RouteInstanceStop>
			{
				new RouteInstanceStop { RouteInstanceStopId = "ris1", Status = 2, StopOrder = 0 }, // CheckedOut
				new RouteInstanceStop { RouteInstanceStopId = "ris2", Status = 2, StopOrder = 1 }, // CheckedOut
				new RouteInstanceStop { RouteInstanceStopId = "ris3", Status = 0, StopOrder = 2 }  // Pending
			};
			_instanceStopsRepo.Setup(x => x.GetStopsByRouteInstanceIdAsync("ri1")).ReturnsAsync(stops);

			var result = await _service.GetInstanceStopsAsync("ri1");

			result.Should().HaveCount(3);
			result.Count(s => s.Status == 2).Should().Be(2); // 2 completed
			result.Count(s => s.Status == 0).Should().Be(1); // 1 pending
		}

		[Test]
		public async Task GetRouteProgressAsync_NoCheckIns_ShouldShowZero()
		{
			var stops = new List<RouteInstanceStop>
			{
				new RouteInstanceStop { RouteInstanceStopId = "ris1", Status = 0, StopOrder = 0 },
				new RouteInstanceStop { RouteInstanceStopId = "ris2", Status = 0, StopOrder = 1 }
			};
			_instanceStopsRepo.Setup(x => x.GetStopsByRouteInstanceIdAsync("ri1")).ReturnsAsync(stops);

			var result = await _service.GetInstanceStopsAsync("ri1");

			result.Should().HaveCount(2);
			result.All(s => s.Status == 0).Should().BeTrue();
		}

		#endregion
	}
}
