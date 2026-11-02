using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class RoutePlan : IEntity
	{
		public string RoutePlanId { get; set; }

		public int DepartmentId { get; set; }

		public int? UnitId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public int RouteStatus { get; set; }

		public string RouteColor { get; set; }

		public decimal? StartLatitude { get; set; }

		public decimal? StartLongitude { get; set; }

		public decimal? EndLatitude { get; set; }

		public decimal? EndLongitude { get; set; }

		public bool UseStationAsStart { get; set; }

		public bool UseStationAsEnd { get; set; }

		public bool OptimizeStopOrder { get; set; }

		/// <summary>Legacy profile name (driving, driving-traffic, walking, cycling); still written from <see cref="RoutingProfile"/> for builds that read it.</summary>
		public string MapboxRouteProfile { get; set; }

		/// <summary>The generated route line as a GeoJSON LineString string (every reader parses GeoJSON).</summary>
		public string MapboxRouteGeometry { get; set; }

		public double? EstimatedDistanceMeters { get; set; }

		public double? EstimatedDurationSeconds { get; set; }

		public int GeofenceRadiusMeters { get; set; }

		/// <summary>A <see cref="Model.RoutingProfile"/> value (M0308).</summary>
		public int RoutingProfile { get; set; }

		/// <summary>A <see cref="RouteAvoidOptions"/> flags value.</summary>
		public int AvoidOptions { get; set; }

		/// <summary>Station the route starts at; null with <see cref="UseStationAsStart"/> means the plan unit's station.</summary>
		public int? StartDepartmentGroupId { get; set; }

		/// <summary>Station the route ends at; null with <see cref="UseStationAsEnd"/> means the plan unit's station.</summary>
		public int? EndDepartmentGroupId { get; set; }

		/// <summary>Routing provider that generated the stored route.</summary>
		public string RouteProvider { get; set; }

		public DateTime? RouteComputedOn { get; set; }

		/// <summary>SHA-256 of everything the route depends on (start, end, stops, profile, avoid options, vehicle).</summary>
		public string RouteInputHash { get; set; }

		/// <summary>The normalized route (OSRM v5 / Mapbox Directions shape, polyline6 geometry) including legs and steps.</summary>
		public string RouteDirectionsJson { get; set; }

		/// <summary>True when stops or options changed since the stored route was generated.</summary>
		public bool RouteIsStale { get; set; }

		public bool IsDeleted { get; set; }

		public string AddedById { get; set; }

		public DateTime AddedOn { get; set; }

		public string UpdatedById { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		public List<RouteStop> Stops { get; set; }

		[NotMapped]
		public List<RouteSchedule> Schedules { get; set; }

		[NotMapped]
		public string TableName => "RoutePlans";

		[NotMapped]
		public string IdName => "RoutePlanId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RoutePlanId; }
			set { RoutePlanId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Stops", "Schedules" };
	}
}
