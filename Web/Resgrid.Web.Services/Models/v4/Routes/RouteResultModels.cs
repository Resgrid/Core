using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Routes
{
	public class GetRouteStopsWithDetailsResult : StandardApiResponseV4Base
	{
		public List<RouteStopWithDetailsResultData> Data { get; set; }
		public GetRouteStopsWithDetailsResult() { Data = new List<RouteStopWithDetailsResultData>(); }
	}

	public class RouteStopWithDetailsResultData
	{
		// Plan-level stop data
		public string RouteStopId { get; set; }
		public string RoutePlanId { get; set; }
		public int StopOrder { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int StopType { get; set; }
		public int? CallId { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string Address { get; set; }
		public int? GeofenceRadiusMeters { get; set; }
		public int Priority { get; set; }
		public DateTime? PlannedArrivalTime { get; set; }
		public DateTime? PlannedDepartureTime { get; set; }
		public int? EstimatedDwellMinutes { get; set; }
		public string ContactId { get; set; }
		public string ContactName { get; set; }
		public string ContactNumber { get; set; }
		public string PlanNotes { get; set; }

		// Instance-level execution data (null when not yet started)
		public string RouteInstanceStopId { get; set; }
		public string RouteInstanceId { get; set; }
		public int Status { get; set; }
		public DateTime? CheckInOn { get; set; }
		public int? CheckInType { get; set; }
		public decimal? CheckInLatitude { get; set; }
		public decimal? CheckInLongitude { get; set; }
		public DateTime? CheckOutOn { get; set; }
		public decimal? CheckOutLatitude { get; set; }
		public decimal? CheckOutLongitude { get; set; }
		public int? DwellSeconds { get; set; }
		public string SkipReason { get; set; }
		public string InstanceNotes { get; set; }
		public DateTime? EstimatedArrivalOn { get; set; }
		public int? ActualArrivalDeviation { get; set; }
	}

	public class GetStopContactResult : StandardApiResponseV4Base
	{
		public RouteStopContactResultData Data { get; set; }
		public GetStopContactResult() { Data = new RouteStopContactResultData(); }
	}

	public class GetRouteContactsResult : StandardApiResponseV4Base
	{
		public List<RouteStopContactResultData> Data { get; set; }
		public GetRouteContactsResult() { Data = new List<RouteStopContactResultData>(); }
	}

	public class RouteStopContactResultData
	{
		public string ContactId { get; set; }
		public string RouteStopId { get; set; }
		public string StopName { get; set; }
		public int ContactType { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string CompanyName { get; set; }
		public string Email { get; set; }
		public string HomePhoneNumber { get; set; }
		public string CellPhoneNumber { get; set; }
		public string OfficePhoneNumber { get; set; }
		public string Website { get; set; }
		public string LocationGpsCoordinates { get; set; }
		public string EntranceGpsCoordinates { get; set; }
		public string ExitGpsCoordinates { get; set; }
		public string LocationGeofence { get; set; }
		public string Description { get; set; }
	}

	public class GetRouteDirectionsResult : StandardApiResponseV4Base
	{
		public RouteDirectionsResultData Data { get; set; }
		public GetRouteDirectionsResult() { Data = new RouteDirectionsResultData(); }
	}

	public class RouteDirectionsResultData
	{
		public string RoutePlanId { get; set; }
		public string RouteInstanceId { get; set; }
		public string Geometry { get; set; }
		public double? EstimatedDistanceMeters { get; set; }
		public double? EstimatedDurationSeconds { get; set; }
		/// <summary>Route profile to use for turn-by-turn navigation: driving, walking, cycling, driving-traffic.</summary>
		public string RouteProfile { get; set; }
		/// <summary>Origin latitude supplied by the caller (unit's current location). Null means start from the first waypoint.</summary>
		public decimal? OriginLatitude { get; set; }
		/// <summary>Origin longitude supplied by the caller (unit's current location). Null means start from the first waypoint.</summary>
		public decimal? OriginLongitude { get; set; }
		public List<RouteDirectionWaypointData> Waypoints { get; set; }
		public RouteDirectionsResultData() { Waypoints = new List<RouteDirectionWaypointData>(); }
	}

	public class RouteDirectionWaypointData
	{
		public string RouteStopId { get; set; }
		public string RouteInstanceStopId { get; set; }
		public int StopOrder { get; set; }
		public string Name { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string Address { get; set; }
		public int Status { get; set; }
	}

	public class GetActiveRouteInstancesResult : StandardApiResponseV4Base
	{
		public List<ActiveRouteInstanceResultData> Data { get; set; }
		public GetActiveRouteInstancesResult() { Data = new List<ActiveRouteInstanceResultData>(); }
	}

	public class ActiveRouteInstanceResultData
	{
		public string RouteInstanceId { get; set; }
		public string RoutePlanId { get; set; }
		public string RoutePlanName { get; set; }
		public int UnitId { get; set; }
		public int Status { get; set; }
		public DateTime? ActualStartOn { get; set; }
		public int StopsCompleted { get; set; }
		public int StopsTotal { get; set; }
		public int ProgressPercent { get; set; }
		public int CurrentStopIndex { get; set; }
		public string CurrentStopName { get; set; }
	}

	public class GetScheduledRoutesResult : StandardApiResponseV4Base
	{
		public List<ScheduledRouteResultData> Data { get; set; }
		public GetScheduledRoutesResult() { Data = new List<ScheduledRouteResultData>(); }
	}

	public class ScheduledRouteResultData
	{
		public string RouteScheduleId { get; set; }
		public string RoutePlanId { get; set; }
		public string RoutePlanName { get; set; }
		public int? UnitId { get; set; }
		public int RecurrenceType { get; set; }
		public string RecurrenceCron { get; set; }
		public string DaysOfWeek { get; set; }
		public int? DayOfMonth { get; set; }
		public string ScheduledStartTime { get; set; }
		public DateTime EffectiveFrom { get; set; }
		public DateTime? EffectiveTo { get; set; }
		public bool IsActive { get; set; }
	}


	public class GetRoutePlansResult : StandardApiResponseV4Base
	{
		public List<RoutePlanResultData> Data { get; set; }
		public GetRoutePlansResult() { Data = new List<RoutePlanResultData>(); }
	}

	public class GetRoutePlanResult : StandardApiResponseV4Base
	{
		public RoutePlanDetailResultData Data { get; set; }
		public GetRoutePlanResult() { Data = new RoutePlanDetailResultData(); }
	}

	public class SaveRoutePlanResult : StandardApiResponseV4Base
	{
		public string Id { get; set; }
		public string Status { get; set; }
	}

	public class GetRouteInstanceResult : StandardApiResponseV4Base
	{
		public RouteInstanceResultData Data { get; set; }
		public GetRouteInstanceResult() { Data = new RouteInstanceResultData(); }
	}

	public class GetRouteInstancesResult : StandardApiResponseV4Base
	{
		public List<RouteInstanceResultData> Data { get; set; }
		public GetRouteInstancesResult() { Data = new List<RouteInstanceResultData>(); }
	}

	public class GetRouteProgressResult : StandardApiResponseV4Base
	{
		public RouteProgressResultData Data { get; set; }
		public GetRouteProgressResult() { Data = new RouteProgressResultData(); }
	}

	public class GetRouteDeviationsResult : StandardApiResponseV4Base
	{
		public List<RouteDeviationResultData> Data { get; set; }
		public GetRouteDeviationsResult() { Data = new List<RouteDeviationResultData>(); }
	}

	public class RoutePlanResultData
	{
		public string RoutePlanId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int? UnitId { get; set; }
		public int RouteStatus { get; set; }
		public string RouteColor { get; set; }
		public int StopsCount { get; set; }
		public double? EstimatedDistanceMeters { get; set; }
		public double? EstimatedDurationSeconds { get; set; }
		public string MapboxRouteProfile { get; set; }
		public DateTime AddedOn { get; set; }
	}

	public class RoutePlanDetailResultData
	{
		public string RoutePlanId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int DepartmentId { get; set; }
		public int? UnitId { get; set; }
		public int RouteStatus { get; set; }
		public string RouteColor { get; set; }
		public decimal? StartLatitude { get; set; }
		public decimal? StartLongitude { get; set; }
		public decimal? EndLatitude { get; set; }
		public decimal? EndLongitude { get; set; }
		public bool UseStationAsStart { get; set; }
		public bool UseStationAsEnd { get; set; }
		public bool OptimizeStopOrder { get; set; }
		public string MapboxRouteProfile { get; set; }
		public string MapboxRouteGeometry { get; set; }
		public double? EstimatedDistanceMeters { get; set; }
		public double? EstimatedDurationSeconds { get; set; }
		public int GeofenceRadiusMeters { get; set; }
		public DateTime AddedOn { get; set; }
		public List<RouteStopResultData> Stops { get; set; }
		public List<RouteScheduleResultData> Schedules { get; set; }

		public RoutePlanDetailResultData()
		{
			Stops = new List<RouteStopResultData>();
			Schedules = new List<RouteScheduleResultData>();
		}
	}

	public class RouteStopResultData
	{
		public string RouteStopId { get; set; }
		public int StopOrder { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int StopType { get; set; }
		public int? CallId { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string Address { get; set; }
		public int? GeofenceRadiusMeters { get; set; }
		public int Priority { get; set; }
		public DateTime? PlannedArrivalTime { get; set; }
		public DateTime? PlannedDepartureTime { get; set; }
		public int? EstimatedDwellMinutes { get; set; }
		public string ContactId { get; set; }
		public string ContactName { get; set; }
		public string ContactNumber { get; set; }
		public string Notes { get; set; }
	}

	public class RouteScheduleResultData
	{
		public string RouteScheduleId { get; set; }
		public int RecurrenceType { get; set; }
		public string RecurrenceCron { get; set; }
		public string DaysOfWeek { get; set; }
		public int? DayOfMonth { get; set; }
		public string ScheduledStartTime { get; set; }
		public DateTime EffectiveFrom { get; set; }
		public DateTime? EffectiveTo { get; set; }
		public bool IsActive { get; set; }
	}

	public class RouteInstanceResultData
	{
		public string RouteInstanceId { get; set; }
		public string RoutePlanId { get; set; }
		public string RoutePlanName { get; set; }
		public int UnitId { get; set; }
		public int Status { get; set; }
		public DateTime? ActualStartOn { get; set; }
		public DateTime? ActualEndOn { get; set; }
		public int StopsCompleted { get; set; }
		public int StopsTotal { get; set; }
		public double? TotalDistanceMeters { get; set; }
		public double? TotalDurationSeconds { get; set; }
		public string Notes { get; set; }
		public DateTime AddedOn { get; set; }
	}

	public class RouteProgressResultData
	{
		public RouteInstanceResultData Instance { get; set; }
		public int TotalStops { get; set; }
		public int CompletedStops { get; set; }
		public int SkippedStops { get; set; }
		public int PendingStops { get; set; }
		public List<RouteInstanceStopResultData> Stops { get; set; }

		public RouteProgressResultData()
		{
			Stops = new List<RouteInstanceStopResultData>();
		}
	}

	public class RouteInstanceStopResultData
	{
		public string RouteInstanceStopId { get; set; }
		public string RouteStopId { get; set; }
		public int StopOrder { get; set; }
		public int Status { get; set; }
		public DateTime? CheckInOn { get; set; }
		public int? CheckInType { get; set; }
		public decimal? CheckInLatitude { get; set; }
		public decimal? CheckInLongitude { get; set; }
		public DateTime? CheckOutOn { get; set; }
		public decimal? CheckOutLatitude { get; set; }
		public decimal? CheckOutLongitude { get; set; }
		public int? DwellSeconds { get; set; }
		public string SkipReason { get; set; }
		public string Notes { get; set; }
		public DateTime? EstimatedArrivalOn { get; set; }
		public int? ActualArrivalDeviation { get; set; }
	}

	public class RouteDeviationResultData
	{
		public string RouteDeviationId { get; set; }
		public string RouteInstanceId { get; set; }
		public DateTime DetectedOn { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public double DeviationDistanceMeters { get; set; }
		public int DeviationType { get; set; }
		public bool IsAcknowledged { get; set; }
		public string AcknowledgedByUserId { get; set; }
		public DateTime? AcknowledgedOn { get; set; }
		public string Notes { get; set; }
	}
}
