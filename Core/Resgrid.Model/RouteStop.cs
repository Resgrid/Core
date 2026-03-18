using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class RouteStop : IEntity
	{
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

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public string ContactId { get; set; }

		public string Notes { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public string TableName => "RouteStops";

		[NotMapped]
		public string IdName => "RouteStopId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RouteStopId; }
			set { RouteStopId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
