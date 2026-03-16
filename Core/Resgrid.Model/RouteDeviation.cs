using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class RouteDeviation : IEntity
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

		[NotMapped]
		public string TableName => "RouteDeviations";

		[NotMapped]
		public string IdName => "RouteDeviationId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RouteDeviationId; }
			set { RouteDeviationId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
