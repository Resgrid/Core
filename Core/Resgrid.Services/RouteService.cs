using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class RouteService : IRouteService
	{
		private readonly IRoutePlansRepository _routePlansRepository;
		private readonly IRouteStopsRepository _routeStopsRepository;
		private readonly IRouteSchedulesRepository _routeSchedulesRepository;
		private readonly IRouteInstancesRepository _routeInstancesRepository;
		private readonly IRouteInstanceStopsRepository _routeInstanceStopsRepository;
		private readonly IRouteDeviationsRepository _routeDeviationsRepository;

		public RouteService(
			IRoutePlansRepository routePlansRepository,
			IRouteStopsRepository routeStopsRepository,
			IRouteSchedulesRepository routeSchedulesRepository,
			IRouteInstancesRepository routeInstancesRepository,
			IRouteInstanceStopsRepository routeInstanceStopsRepository,
			IRouteDeviationsRepository routeDeviationsRepository)
		{
			_routePlansRepository = routePlansRepository;
			_routeStopsRepository = routeStopsRepository;
			_routeSchedulesRepository = routeSchedulesRepository;
			_routeInstancesRepository = routeInstancesRepository;
			_routeInstanceStopsRepository = routeInstanceStopsRepository;
			_routeDeviationsRepository = routeDeviationsRepository;
		}

		#region Route Plan CRUD

		public async Task<RoutePlan> SaveRoutePlanAsync(RoutePlan routePlan, CancellationToken cancellationToken = default)
		{
			return await _routePlansRepository.SaveOrUpdateAsync(routePlan, cancellationToken);
		}

		public async Task<RoutePlan> GetRoutePlanByIdAsync(string routePlanId)
		{
			return await _routePlansRepository.GetByIdAsync(routePlanId);
		}

		public async Task<List<RoutePlan>> GetRoutePlansForDepartmentAsync(int departmentId)
		{
			var plans = await _routePlansRepository.GetRoutePlansByDepartmentIdAsync(departmentId);
			return plans?.Where(x => !x.IsDeleted).ToList() ?? new List<RoutePlan>();
		}

		public async Task<List<RoutePlan>> GetRoutePlansForUnitAsync(int unitId)
		{
			var plans = await _routePlansRepository.GetRoutePlansByUnitIdAsync(unitId);
			return plans?.Where(x => !x.IsDeleted).ToList() ?? new List<RoutePlan>();
		}

		public async Task<bool> DeleteRoutePlanAsync(string routePlanId, CancellationToken cancellationToken = default)
		{
			var plan = await _routePlansRepository.GetByIdAsync(routePlanId);
			if (plan == null)
				return false;

			plan.IsDeleted = true;
			await _routePlansRepository.SaveOrUpdateAsync(plan, cancellationToken);
			return true;
		}

		#endregion Route Plan CRUD

		#region Route Stop CRUD

		public async Task<RouteStop> SaveRouteStopAsync(RouteStop routeStop, CancellationToken cancellationToken = default)
		{
			return await _routeStopsRepository.SaveOrUpdateAsync(routeStop, cancellationToken);
		}

		public async Task<List<RouteStop>> GetRouteStopsForPlanAsync(string routePlanId)
		{
			var stops = await _routeStopsRepository.GetStopsByRoutePlanIdAsync(routePlanId);
			return stops?.Where(x => !x.IsDeleted).OrderBy(x => x.StopOrder).ToList() ?? new List<RouteStop>();
		}

		public async Task<List<RouteStop>> GetRouteStopsForContactAsync(string contactId, int departmentId)
		{
			var stops = await _routeStopsRepository.GetStopsByContactIdAsync(contactId);
			if (stops == null)
				return new List<RouteStop>();

			var plans = await _routePlansRepository.GetRoutePlansByDepartmentIdAsync(departmentId);
			var planIds = plans?.Where(p => !p.IsDeleted).Select(p => p.RoutePlanId).ToHashSet() ?? new HashSet<string>();

			return stops.Where(s => !s.IsDeleted && planIds.Contains(s.RoutePlanId)).ToList();
		}

		public async Task<bool> ReorderRouteStopsAsync(string routePlanId, List<string> orderedStopIds, CancellationToken cancellationToken = default)
		{
			var stops = await _routeStopsRepository.GetStopsByRoutePlanIdAsync(routePlanId);
			if (stops == null)
				return false;

			var stopDict = stops.ToDictionary(s => s.RouteStopId);
			for (int i = 0; i < orderedStopIds.Count; i++)
			{
				if (stopDict.TryGetValue(orderedStopIds[i], out var stop))
				{
					stop.StopOrder = i;
					await _routeStopsRepository.SaveOrUpdateAsync(stop, cancellationToken);
				}
			}

			return true;
		}

		public async Task<bool> DeleteRouteStopAsync(string routeStopId, CancellationToken cancellationToken = default)
		{
			var stop = await _routeStopsRepository.GetByIdAsync(routeStopId);
			if (stop == null)
				return false;

			stop.IsDeleted = true;
			await _routeStopsRepository.SaveOrUpdateAsync(stop, cancellationToken);
			return true;
		}

		#endregion Route Stop CRUD

		#region Route Schedule CRUD

		public async Task<RouteSchedule> SaveRouteScheduleAsync(RouteSchedule schedule, CancellationToken cancellationToken = default)
		{
			return await _routeSchedulesRepository.SaveOrUpdateAsync(schedule, cancellationToken);
		}

		public async Task<List<RouteSchedule>> GetSchedulesForPlanAsync(string routePlanId)
		{
			var schedules = await _routeSchedulesRepository.GetSchedulesByRoutePlanIdAsync(routePlanId);
			return schedules?.ToList() ?? new List<RouteSchedule>();
		}

		public async Task<List<RouteSchedule>> GetDueSchedulesAsync(DateTime asOfDate)
		{
			var schedules = await _routeSchedulesRepository.GetActiveSchedulesDueAsync(asOfDate);
			return schedules?.ToList() ?? new List<RouteSchedule>();
		}

		public async Task<bool> DeleteRouteScheduleAsync(string routeScheduleId, CancellationToken cancellationToken = default)
		{
			var schedule = await _routeSchedulesRepository.GetByIdAsync(routeScheduleId);
			if (schedule == null)
				return false;

			return await _routeSchedulesRepository.DeleteAsync(schedule, cancellationToken);
		}

		#endregion Route Schedule CRUD

		#region Route Instance Lifecycle

		public async Task<RouteInstance> StartRouteAsync(string routePlanId, int unitId, string startedByUserId, CancellationToken cancellationToken = default)
		{
			var existingActive = await GetActiveInstanceForUnitAsync(unitId);
			if (existingActive != null)
				throw new InvalidOperationException("Unit already has an active route instance.");

			var plan = await _routePlansRepository.GetByIdAsync(routePlanId);
			if (plan == null)
				throw new ArgumentException("Route plan not found.", nameof(routePlanId));

			var stops = await GetRouteStopsForPlanAsync(routePlanId);

			var instance = new RouteInstance
			{
				RoutePlanId = routePlanId,
				UnitId = unitId,
				DepartmentId = plan.DepartmentId,
				Status = (int)RouteInstanceStatus.InProgress,
				StartedByUserId = startedByUserId,
				ActualStartOn = DateTime.UtcNow,
				StopsCompleted = 0,
				StopsTotal = stops.Count,
				AddedOn = DateTime.UtcNow
			};

			await _routeInstancesRepository.SaveOrUpdateAsync(instance, cancellationToken);

			foreach (var stop in stops)
			{
				var instanceStop = new RouteInstanceStop
				{
					RouteInstanceId = instance.RouteInstanceId,
					RouteStopId = stop.RouteStopId,
					StopOrder = stop.StopOrder,
					Status = 0, // Pending
					AddedOn = DateTime.UtcNow
				};

				await _routeInstanceStopsRepository.SaveOrUpdateAsync(instanceStop, cancellationToken);
			}

			return instance;
		}

		public async Task<RouteInstance> EndRouteAsync(string routeInstanceId, string endedByUserId, CancellationToken cancellationToken = default)
		{
			var instance = await _routeInstancesRepository.GetByIdAsync(routeInstanceId);
			if (instance == null)
				throw new ArgumentException("Route instance not found.", nameof(routeInstanceId));

			if (instance.Status != (int)RouteInstanceStatus.InProgress)
				throw new InvalidOperationException("Route instance is not in progress.");

			instance.Status = (int)RouteInstanceStatus.Completed;
			instance.ActualEndOn = DateTime.UtcNow;
			instance.EndedByUserId = endedByUserId;

			if (instance.ActualStartOn.HasValue)
				instance.TotalDurationSeconds = (DateTime.UtcNow - instance.ActualStartOn.Value).TotalSeconds;

			await _routeInstancesRepository.SaveOrUpdateAsync(instance, cancellationToken);
			return instance;
		}

		public async Task<RouteInstance> CancelRouteAsync(string routeInstanceId, string userId, string reason, CancellationToken cancellationToken = default)
		{
			var instance = await _routeInstancesRepository.GetByIdAsync(routeInstanceId);
			if (instance == null)
				throw new ArgumentException("Route instance not found.", nameof(routeInstanceId));

			instance.Status = (int)RouteInstanceStatus.Cancelled;
			instance.ActualEndOn = DateTime.UtcNow;
			instance.EndedByUserId = userId;
			instance.Notes = reason;

			await _routeInstancesRepository.SaveOrUpdateAsync(instance, cancellationToken);
			return instance;
		}

		public async Task<RouteInstance> PauseRouteAsync(string routeInstanceId, string userId, CancellationToken cancellationToken = default)
		{
			var instance = await _routeInstancesRepository.GetByIdAsync(routeInstanceId);
			if (instance == null)
				throw new ArgumentException("Route instance not found.", nameof(routeInstanceId));

			if (instance.Status != (int)RouteInstanceStatus.InProgress)
				throw new InvalidOperationException("Route instance is not in progress.");

			instance.Status = (int)RouteInstanceStatus.Paused;
			await _routeInstancesRepository.SaveOrUpdateAsync(instance, cancellationToken);
			return instance;
		}

		public async Task<RouteInstance> ResumeRouteAsync(string routeInstanceId, string userId, CancellationToken cancellationToken = default)
		{
			var instance = await _routeInstancesRepository.GetByIdAsync(routeInstanceId);
			if (instance == null)
				throw new ArgumentException("Route instance not found.", nameof(routeInstanceId));

			if (instance.Status != (int)RouteInstanceStatus.Paused)
				throw new InvalidOperationException("Route instance is not paused.");

			instance.Status = (int)RouteInstanceStatus.InProgress;
			await _routeInstancesRepository.SaveOrUpdateAsync(instance, cancellationToken);
			return instance;
		}

		public async Task<RouteInstance> GetActiveInstanceForUnitAsync(int unitId)
		{
			var instances = await _routeInstancesRepository.GetActiveInstancesByUnitIdAsync(unitId);
			return instances?.FirstOrDefault();
		}

		public async Task<RouteInstance> GetInstanceByIdAsync(string routeInstanceId)
		{
			return await _routeInstancesRepository.GetByIdAsync(routeInstanceId);
		}

		public async Task<List<RouteInstance>> GetInstancesForDepartmentAsync(int departmentId)
		{
			var instances = await _routeInstancesRepository.GetInstancesByDepartmentIdAsync(departmentId);
			return instances?.ToList() ?? new List<RouteInstance>();
		}

		public async Task<List<RouteInstance>> GetInstancesByDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			var instances = await _routeInstancesRepository.GetInstancesByDateRangeAsync(departmentId, startDate, endDate);
			return instances?.ToList() ?? new List<RouteInstance>();
		}

		#endregion Route Instance Lifecycle

		#region Stop Check-in/Check-out

		public async Task<RouteInstanceStop> CheckInAtStopAsync(string routeInstanceStopId, decimal latitude, decimal longitude, RouteStopCheckInType checkInType, CancellationToken cancellationToken = default)
		{
			var instanceStop = await _routeInstanceStopsRepository.GetByIdAsync(routeInstanceStopId);
			if (instanceStop == null)
				throw new ArgumentException("Route instance stop not found.", nameof(routeInstanceStopId));

			var instance = await _routeInstancesRepository.GetByIdAsync(instanceStop.RouteInstanceId);
			if (instance == null || instance.Status != (int)RouteInstanceStatus.InProgress)
				throw new InvalidOperationException("Route instance is not in progress.");

			instanceStop.Status = 1; // CheckedIn
			instanceStop.CheckInOn = DateTime.UtcNow;
			instanceStop.CheckInType = (int)checkInType;
			instanceStop.CheckInLatitude = latitude;
			instanceStop.CheckInLongitude = longitude;

			if (instanceStop.EstimatedArrivalOn.HasValue)
				instanceStop.ActualArrivalDeviation = (int)(DateTime.UtcNow - instanceStop.EstimatedArrivalOn.Value).TotalSeconds;

			await _routeInstanceStopsRepository.SaveOrUpdateAsync(instanceStop, cancellationToken);
			return instanceStop;
		}

		public async Task<RouteInstanceStop> CheckOutFromStopAsync(string routeInstanceStopId, decimal latitude, decimal longitude, CancellationToken cancellationToken = default)
		{
			var instanceStop = await _routeInstanceStopsRepository.GetByIdAsync(routeInstanceStopId);
			if (instanceStop == null)
				throw new ArgumentException("Route instance stop not found.", nameof(routeInstanceStopId));

			if (instanceStop.Status != 1) // Not CheckedIn
				throw new InvalidOperationException("Must check in before checking out.");

			instanceStop.Status = 2; // CheckedOut
			instanceStop.CheckOutOn = DateTime.UtcNow;
			instanceStop.CheckOutLatitude = latitude;
			instanceStop.CheckOutLongitude = longitude;

			if (instanceStop.CheckInOn.HasValue)
				instanceStop.DwellSeconds = (int)(DateTime.UtcNow - instanceStop.CheckInOn.Value).TotalSeconds;

			await _routeInstanceStopsRepository.SaveOrUpdateAsync(instanceStop, cancellationToken);

			// Update instance completed count
			var instance = await _routeInstancesRepository.GetByIdAsync(instanceStop.RouteInstanceId);
			if (instance != null)
			{
				instance.StopsCompleted += 1;
				await _routeInstancesRepository.SaveOrUpdateAsync(instance, cancellationToken);
			}

			return instanceStop;
		}

		public async Task<RouteInstanceStop> SkipStopAsync(string routeInstanceStopId, string reason, CancellationToken cancellationToken = default)
		{
			var instanceStop = await _routeInstanceStopsRepository.GetByIdAsync(routeInstanceStopId);
			if (instanceStop == null)
				throw new ArgumentException("Route instance stop not found.", nameof(routeInstanceStopId));

			instanceStop.Status = 3; // Skipped
			instanceStop.SkipReason = reason;

			await _routeInstanceStopsRepository.SaveOrUpdateAsync(instanceStop, cancellationToken);
			return instanceStop;
		}

		public async Task<List<RouteInstanceStop>> GetInstanceStopsAsync(string routeInstanceId)
		{
			var stops = await _routeInstanceStopsRepository.GetStopsByRouteInstanceIdAsync(routeInstanceId);
			return stops?.OrderBy(x => x.StopOrder).ToList() ?? new List<RouteInstanceStop>();
		}

		#endregion Stop Check-in/Check-out

		#region Deviation Tracking

		public async Task<RouteDeviation> RecordDeviationAsync(RouteDeviation deviation, CancellationToken cancellationToken = default)
		{
			return await _routeDeviationsRepository.SaveOrUpdateAsync(deviation, cancellationToken);
		}

		public async Task<RouteDeviation> AcknowledgeDeviationAsync(string routeDeviationId, string userId, CancellationToken cancellationToken = default)
		{
			var deviation = await _routeDeviationsRepository.GetByIdAsync(routeDeviationId);
			if (deviation == null)
				throw new ArgumentException("Route deviation not found.", nameof(routeDeviationId));

			deviation.IsAcknowledged = true;
			deviation.AcknowledgedByUserId = userId;
			deviation.AcknowledgedOn = DateTime.UtcNow;

			await _routeDeviationsRepository.SaveOrUpdateAsync(deviation, cancellationToken);
			return deviation;
		}

		public async Task<List<RouteDeviation>> GetUnacknowledgedDeviationsAsync(int departmentId)
		{
			var deviations = await _routeDeviationsRepository.GetUnacknowledgedDeviationsByDepartmentAsync(departmentId);
			return deviations?.ToList() ?? new List<RouteDeviation>();
		}

		#endregion Deviation Tracking

		#region Mapbox Integration

		public async Task<RoutePlan> UpdateRouteGeometryAsync(string routePlanId, string geometry, double distance, double duration, CancellationToken cancellationToken = default)
		{
			var plan = await _routePlansRepository.GetByIdAsync(routePlanId);
			if (plan == null)
				throw new ArgumentException("Route plan not found.", nameof(routePlanId));

			plan.MapboxRouteGeometry = geometry;
			plan.EstimatedDistanceMeters = distance;
			plan.EstimatedDurationSeconds = duration;

			await _routePlansRepository.SaveOrUpdateAsync(plan, cancellationToken);
			return plan;
		}

		#endregion Mapbox Integration

		#region Single-record lookups

		public async Task<RouteStop> GetRouteStopByIdAsync(string routeStopId)
		{
			return await _routeStopsRepository.GetByIdAsync(routeStopId);
		}

		public async Task<RouteInstanceStop> GetInstanceStopByIdAsync(string routeInstanceStopId)
		{
			return await _routeInstanceStopsRepository.GetByIdAsync(routeInstanceStopId);
		}

		public async Task<RouteInstanceStop> UpdateInstanceStopNotesAsync(string routeInstanceStopId, string notes, CancellationToken cancellationToken = default)
		{
			var instanceStop = await _routeInstanceStopsRepository.GetByIdAsync(routeInstanceStopId);
			if (instanceStop == null)
				throw new ArgumentException("Route instance stop not found.", nameof(routeInstanceStopId));

			instanceStop.Notes = notes;
			await _routeInstanceStopsRepository.SaveOrUpdateAsync(instanceStop, cancellationToken);
			return instanceStop;
		}

		#endregion Single-record lookups

		#region Geofence

		public async Task<RouteInstanceStop> CheckGeofenceProximityAsync(int unitId, decimal latitude, decimal longitude)
		{
			var activeInstance = await GetActiveInstanceForUnitAsync(unitId);
			if (activeInstance == null)
				return null;

			var plan = await _routePlansRepository.GetByIdAsync(activeInstance.RoutePlanId);
			var instanceStops = await GetInstanceStopsAsync(activeInstance.RouteInstanceId);
			var pendingStops = instanceStops.Where(s => s.Status == 0).ToList(); // Pending

			foreach (var instanceStop in pendingStops)
			{
				var routeStop = await _routeStopsRepository.GetByIdAsync(instanceStop.RouteStopId);
				if (routeStop == null) continue;

				var radiusMeters = routeStop.GeofenceRadiusMeters ?? plan?.GeofenceRadiusMeters ?? 100;
				var distance = HaversineDistance(latitude, longitude, routeStop.Latitude, routeStop.Longitude);

				if (distance <= radiusMeters)
					return instanceStop;
			}

			return null;
		}

		private static double HaversineDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
		{
			const double R = 6371000; // Earth radius in meters
			var dLat = ToRadians((double)(lat2 - lat1));
			var dLon = ToRadians((double)(lon2 - lon1));
			var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
				Math.Cos(ToRadians((double)lat1)) * Math.Cos(ToRadians((double)lat2)) *
				Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
			var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
			return R * c;
		}

		private static double ToRadians(double degrees)
		{
			return degrees * Math.PI / 180;
		}

		#endregion Geofence
	}
}
