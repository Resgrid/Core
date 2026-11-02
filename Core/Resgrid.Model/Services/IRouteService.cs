using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IRouteService
	{
		// Route Plan CRUD
		Task<RoutePlan> SaveRoutePlanAsync(RoutePlan routePlan, CancellationToken cancellationToken = default);
		Task<RoutePlan> GetRoutePlanByIdAsync(string routePlanId);
		Task<List<RoutePlan>> GetRoutePlansForDepartmentAsync(int departmentId);
		Task<List<RoutePlan>> GetRoutePlansForUnitAsync(int unitId);
		Task<bool> DeleteRoutePlanAsync(string routePlanId, CancellationToken cancellationToken = default);

		// Route Stop CRUD
		Task<RouteStop> SaveRouteStopAsync(RouteStop routeStop, CancellationToken cancellationToken = default);
		Task<List<RouteStop>> GetRouteStopsForPlanAsync(string routePlanId);
		Task<List<RouteStop>> GetRouteStopsForContactAsync(string contactId, int departmentId);
		/// <summary>Renumbers the plan's stops 1..n in the given order (stops not listed follow, in their current order).</summary>
		Task<bool> ReorderRouteStopsAsync(string routePlanId, List<string> orderedStopIds, CancellationToken cancellationToken = default);
		Task<bool> DeleteRouteStopAsync(string routeStopId, CancellationToken cancellationToken = default);

		// Route Schedule CRUD
		Task<RouteSchedule> SaveRouteScheduleAsync(RouteSchedule schedule, CancellationToken cancellationToken = default);
		Task<List<RouteSchedule>> GetSchedulesForPlanAsync(string routePlanId);
		Task<List<RouteSchedule>> GetDueSchedulesAsync(DateTime asOfDate);
		Task<bool> DeleteRouteScheduleAsync(string routeScheduleId, CancellationToken cancellationToken = default);

		// Route Instance Lifecycle
		Task<RouteInstance> StartRouteAsync(string routePlanId, int unitId, string startedByUserId, CancellationToken cancellationToken = default);
		Task<RouteInstance> EndRouteAsync(string routeInstanceId, string endedByUserId, CancellationToken cancellationToken = default);
		Task<RouteInstance> CancelRouteAsync(string routeInstanceId, string userId, string reason, CancellationToken cancellationToken = default);
		Task<RouteInstance> PauseRouteAsync(string routeInstanceId, string userId, CancellationToken cancellationToken = default);
		Task<RouteInstance> ResumeRouteAsync(string routeInstanceId, string userId, CancellationToken cancellationToken = default);
		Task<RouteInstance> GetActiveInstanceForUnitAsync(int unitId);

		/// <summary>The unit's in-progress instance, else its paused one (the legacy query returns in-progress only).</summary>
		Task<RouteInstance> GetActiveOrPausedInstanceForUnitAsync(int departmentId, int unitId);
		Task<RouteInstance> GetInstanceByIdAsync(string routeInstanceId);
		Task<List<RouteInstance>> GetInstancesForDepartmentAsync(int departmentId);

		/// <summary>In-progress and paused instances only, without geometry or notes (the home dashboard's route widget).</summary>
		Task<List<RouteInstance>> GetActiveInstancesForDepartmentAsync(int departmentId);
		Task<List<RouteInstance>> GetInstancesByDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);

		// Stop Check-in/Check-out
		Task<RouteInstanceStop> CheckInAtStopAsync(string routeInstanceStopId, decimal latitude, decimal longitude, RouteStopCheckInType checkInType, CancellationToken cancellationToken = default);
		Task<RouteInstanceStop> CheckOutFromStopAsync(string routeInstanceStopId, decimal latitude, decimal longitude, CancellationToken cancellationToken = default);
		Task<RouteInstanceStop> SkipStopAsync(string routeInstanceStopId, string reason, CancellationToken cancellationToken = default);
		Task<List<RouteInstanceStop>> GetInstanceStopsAsync(string routeInstanceId);

		// Deviation Tracking
		Task<RouteDeviation> RecordDeviationAsync(RouteDeviation deviation, CancellationToken cancellationToken = default);
		Task<RouteDeviation> AcknowledgeDeviationAsync(string routeDeviationId, string userId, CancellationToken cancellationToken = default);
		Task<List<RouteDeviation>> GetUnacknowledgedDeviationsAsync(int departmentId);
		Task<List<RouteDeviation>> GetDeviationsForInstanceAsync(string routeInstanceId);

		// Generated route storage
		Task<RoutePlan> UpdateRouteGeometryAsync(string routePlanId, string geometry, double distance, double duration, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes a generated route (the plan's routing fields and each stop's leg values) without marking the plan stale.
		/// </summary>
		Task<RoutePlan> SaveGeneratedRouteAsync(RoutePlan plan, IEnumerable<RouteStop> stops, CancellationToken cancellationToken = default);

		/// <summary>Flags the plan's stored route as out of date (stops or options changed).</summary>
		Task MarkRoutePlanStaleAsync(string routePlanId, CancellationToken cancellationToken = default);

		Task<RouteInstance> SetInstanceLastRoutedOnAsync(string routeInstanceId, DateTime routedOn, CancellationToken cancellationToken = default);

		// Geofence
		Task<RouteInstanceStop> CheckGeofenceProximityAsync(int unitId, decimal latitude, decimal longitude);

		// Single-record lookups
		Task<RouteStop> GetRouteStopByIdAsync(string routeStopId);
		Task<RouteInstanceStop> GetInstanceStopByIdAsync(string routeInstanceStopId);
		Task<RouteInstanceStop> UpdateInstanceStopNotesAsync(string routeInstanceStopId, string notes, CancellationToken cancellationToken = default);
	}
}
