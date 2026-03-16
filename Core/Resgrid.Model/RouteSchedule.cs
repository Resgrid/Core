using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class RouteSchedule : IEntity
	{
		public string RouteScheduleId { get; set; }

		public string RoutePlanId { get; set; }

		public int RecurrenceType { get; set; }

		public string RecurrenceCron { get; set; }

		public string DaysOfWeek { get; set; }

		public int? DayOfMonth { get; set; }

		public string ScheduledStartTime { get; set; }

		public DateTime EffectiveFrom { get; set; }

		public DateTime? EffectiveTo { get; set; }

		public bool IsActive { get; set; }

		public DateTime? LastInstanceCreatedOn { get; set; }

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public string TableName => "RouteSchedules";

		[NotMapped]
		public string IdName => "RouteScheduleId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RouteScheduleId; }
			set { RouteScheduleId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
