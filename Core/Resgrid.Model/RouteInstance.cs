using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class RouteInstance : IEntity
	{
		public string RouteInstanceId { get; set; }

		public string RoutePlanId { get; set; }

		public string RouteScheduleId { get; set; }

		public int UnitId { get; set; }

		public int DepartmentId { get; set; }

		public int Status { get; set; }

		public string StartedByUserId { get; set; }

		public DateTime? ScheduledStartOn { get; set; }

		public DateTime? ActualStartOn { get; set; }

		public DateTime? ActualEndOn { get; set; }

		public string EndedByUserId { get; set; }

		public double? TotalDistanceMeters { get; set; }

		public double? TotalDurationSeconds { get; set; }

		public string ActualRouteGeometry { get; set; }

		public int StopsCompleted { get; set; }

		public int StopsTotal { get; set; }

		public string Notes { get; set; }

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public string TableName => "RouteInstances";

		[NotMapped]
		public string IdName => "RouteInstanceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RouteInstanceId; }
			set { RouteInstanceId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
