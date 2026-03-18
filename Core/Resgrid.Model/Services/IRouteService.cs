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
		Task<RouteInstance> GetInstanceByIdAsync(string routeInstanceId);
		Task<List<RouteInstance>> GetInstancesForDepartmentAsync(int departmentId);
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

		// Mapbox Integration
		Task<RoutePlan> UpdateRouteGeometryAsync(string routePlanId, string geometry, double distance, double duration, CancellationToken cancellationToken = default);

		// Geofence
		Task<RouteInstanceStop> CheckGeofenceProximityAsync(int unitId, decimal latitude, decimal longitude);
	}
}
