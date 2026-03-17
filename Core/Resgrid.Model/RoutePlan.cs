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

		public string MapboxRouteProfile { get; set; }

		public string MapboxRouteGeometry { get; set; }

		public double? EstimatedDistanceMeters { get; set; }

		public double? EstimatedDurationSeconds { get; set; }

		public int GeofenceRadiusMeters { get; set; }

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
