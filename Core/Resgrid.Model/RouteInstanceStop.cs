using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class RouteInstanceStop : IEntity
	{
		public string RouteInstanceStopId { get; set; }

		public string RouteInstanceId { get; set; }

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

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public string TableName => "RouteInstanceStops";

		[NotMapped]
		public string IdName => "RouteInstanceStopId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RouteInstanceStopId; }
			set { RouteInstanceStopId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
