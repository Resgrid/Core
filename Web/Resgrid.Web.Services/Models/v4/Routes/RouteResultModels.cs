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

		// Appended by the MapLibre routing plan (section 10.5); legacy builds read only the fields above.
		/// <summary>The routing provider that produced the route (valhalla, mapbox, google, osrm, estimate).</summary>
		public string Provider { get; set; }
		/// <summary>Attribution the provider requires wherever the route is shown.</summary>
		public string Attribution { get; set; }
		/// <summary>True when only a straight-line estimate was available (no road route).</summary>
		public bool IsEstimate { get; set; }
		public bool IsTrafficAware { get; set; }
		/// <summary>True when the unit's size and weight limits were applied.</summary>
		public bool VehicleRestricted { get; set; }
		/// <summary>True when the route was computed from the caller's position (live instance directions).</summary>
		public bool IsLive { get; set; }
		/// <summary>Travel time plus estimated dwell at the stops.</summary>
		public double? EstimatedDurationWithDwellSeconds { get; set; }
		public DateTime? ComputedOn { get; set; }
		/// <summary>The resolved route start (station or explicit point); null when the route starts at the first stop.</summary>
		public RouteEndpointData Start { get; set; }
		/// <summary>The resolved route end; null when the route ends at the last stop.</summary>
		public RouteEndpointData End { get; set; }
		/// <summary>One leg per stop arrival (and one to the end), with planned offsets or live ETAs.</summary>
		public List<RouteDirectionLegData> Legs { get; set; } = new List<RouteDirectionLegData>();
		/// <summary>The full route in the OSRM v5 / Mapbox Directions shape (polyline6 geometry, legs with steps); null when unavailable.</summary>
		public Newtonsoft.Json.Linq.JToken Route { get; set; }
		public List<Routing.RouteIncidentData> Incidents { get; set; } = new List<Routing.RouteIncidentData>();

		public RouteDirectionsResultData() { Waypoints = new List<RouteDirectionWaypointData>(); }
	}

	public class RouteEndpointData
	{
		public double Latitude { get; set; }
		public double Longitude { get; set; }
		/// <summary>The station the point is; null for an explicit point.</summary>
		public int? DepartmentGroupId { get; set; }
		public string Name { get; set; }
	}

	public class RouteDirectionLegData
	{
		/// <summary>The stop this leg arrives at; null for the leg to the route end.</summary>
		public string RouteStopId { get; set; }
		public string RouteInstanceStopId { get; set; }
		public string Name { get; set; }
		public double DistanceMeters { get; set; }
		public double DurationSeconds { get; set; }
		/// <summary>Seconds from the route start (or from now, for live directions) to arrival, including dwell before it.</summary>
		public double OffsetSeconds { get; set; }
		/// <summary>Arrival time for live instance directions.</summary>
		public DateTime? EtaUtc { get; set; }
	}

	public class PreviewRouteOptimizationResult : StandardApiResponseV4Base
	{
		public RouteOptimizationPreviewData Data { get; set; }
		public PreviewRouteOptimizationResult() { Data = new RouteOptimizationPreviewData(); }
	}

	public class RouteOptimizationPreviewData
	{
		public string RoutePlanId { get; set; }
		public string Provider { get; set; }
		public bool Changed { get; set; }
		public List<string> CurrentOrder { get; set; } = new List<string>();
		public List<string> ProposedOrder { get; set; } = new List<string>();
		public double CurrentDistanceMeters { get; set; }
		public double CurrentDurationSeconds { get; set; }
		public double ProposedDistanceMeters { get; set; }
		public double ProposedDurationSeconds { get; set; }
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

		// Appended by the MapLibre routing plan (section 10.5).
		public int? EstimatedDwellMinutes { get; set; }
		public double? PlannedOffsetSeconds { get; set; }
		/// <summary>Live ETA (instance directions from a position) or the planned arrival of a running instance's stop.</summary>
		public DateTime? EtaUtc { get; set; }
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

		// Appended by the MapLibre routing plan (section 10.5); legacy builds ignore them.
		/// <summary>RoutingProfile: Driving=0, DrivingTraffic=1, Truck=2, Walking=3, Cycling=4.</summary>
		public int RoutingProfile { get; set; }
		/// <summary>RouteAvoidOptions flags: Tolls=1, Highways=2, Ferries=4, Unpaved=8.</summary>
		public int AvoidOptions { get; set; }
		public string RouteProvider { get; set; }
		public DateTime? RouteComputedOn { get; set; }
		/// <summary>True when stops or options changed since the stored route was generated.</summary>
		public bool RouteIsStale { get; set; }
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

		// Appended by the MapLibre routing plan (section 10.5); legacy builds ignore them.
		/// <summary>RoutingProfile: Driving=0, DrivingTraffic=1, Truck=2, Walking=3, Cycling=4.</summary>
		public int RoutingProfile { get; set; }
		/// <summary>RouteAvoidOptions flags: Tolls=1, Highways=2, Ferries=4, Unpaved=8.</summary>
		public int AvoidOptions { get; set; }
		public int? StartDepartmentGroupId { get; set; }
		public int? EndDepartmentGroupId { get; set; }
		public string RouteProvider { get; set; }
		public DateTime? RouteComputedOn { get; set; }
		public bool RouteIsStale { get; set; }
		/// <summary>Travel time plus every stop's estimated dwell.</summary>
		public double? EstimatedDurationWithDwellSeconds { get; set; }

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

		// Appended by the MapLibre routing plan (section 10.5).
		/// <summary>Distance of the leg arriving at this stop.</summary>
		public double? LegDistanceMeters { get; set; }
		/// <summary>Travel time of the leg arriving at this stop.</summary>
		public double? LegDurationSeconds { get; set; }
		/// <summary>Seconds from the route start to arrival here, including earlier stops' dwell.</summary>
		public double? PlannedOffsetSeconds { get; set; }
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
