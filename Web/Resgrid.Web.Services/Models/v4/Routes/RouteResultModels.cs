using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Routes
{
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
