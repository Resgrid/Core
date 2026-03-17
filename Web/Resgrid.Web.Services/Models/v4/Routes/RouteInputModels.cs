using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Routes
{
	public class NewRoutePlanInput
	{
		[Required]
		public string Name { get; set; }
		public string Description { get; set; }
		public int? UnitId { get; set; }
		public int RouteStatus { get; set; }
		public string RouteColor { get; set; }
		public decimal? StartLatitude { get; set; }
		public decimal? StartLongitude { get; set; }
		public decimal? EndLatitude { get; set; }
		public decimal? EndLongitude { get; set; }
		public bool UseStationAsStart { get; set; } = true;
		public bool UseStationAsEnd { get; set; } = true;
		public bool OptimizeStopOrder { get; set; }
		public string MapboxRouteProfile { get; set; }
		public int GeofenceRadiusMeters { get; set; } = 100;
		public List<NewRouteStopInput> Stops { get; set; }
		public List<RouteScheduleInput> Schedules { get; set; }
	}

	public class UpdateRoutePlanInput : NewRoutePlanInput
	{
		[Required]
		public string RoutePlanId { get; set; }
	}

	public class NewRouteStopInput
	{
		[Required]
		public string Name { get; set; }
		public string Description { get; set; }
		public int StopType { get; set; }
		public int? CallId { get; set; }
		[Required]
		public decimal Latitude { get; set; }
		[Required]
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

	public class RouteScheduleInput
	{
		public int RecurrenceType { get; set; }
		public string RecurrenceCron { get; set; }
		public string DaysOfWeek { get; set; }
		public int? DayOfMonth { get; set; }
		public string ScheduledStartTime { get; set; }
		[Required]
		public DateTime EffectiveFrom { get; set; }
		public DateTime? EffectiveTo { get; set; }
	}

	public class StartRouteInput
	{
		[Required]
		public string RoutePlanId { get; set; }
		[Required]
		public int UnitId { get; set; }
	}

	public class EndRouteInput
	{
		[Required]
		public string RouteInstanceId { get; set; }
	}

	public class PauseRouteInput
	{
		[Required]
		public string RouteInstanceId { get; set; }
	}

	public class ResumeRouteInput
	{
		[Required]
		public string RouteInstanceId { get; set; }
	}

	public class CancelRouteInput
	{
		[Required]
		public string RouteInstanceId { get; set; }
		public string Reason { get; set; }
	}

	public class CheckInInput
	{
		[Required]
		public string RouteInstanceStopId { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
	}

	public class CheckOutInput
	{
		[Required]
		public string RouteInstanceStopId { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
	}

	public class SkipStopInput
	{
		[Required]
		public string RouteInstanceStopId { get; set; }
		public string Reason { get; set; }
	}

	public class GeofenceCheckInInput
	{
		[Required]
		public int UnitId { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
	}

	public class UpdateRouteGeometryInput
	{
		[Required]
		public string RoutePlanId { get; set; }
		[Required]
		public string Geometry { get; set; }
		public double DistanceMeters { get; set; }
		public double DurationSeconds { get; set; }
	}
}
