using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class CheckInTimerOverride : IEntity
	{
		public string CheckInTimerOverrideId { get; set; }

		public int DepartmentId { get; set; }

		public int? CallTypeId { get; set; }

		public int? CallPriority { get; set; }

		public int TimerTargetType { get; set; }

		public int? UnitTypeId { get; set; }

		public int DurationMinutes { get; set; }

		public int WarningThresholdMinutes { get; set; }

		public bool IsEnabled { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string ActiveForStates { get; set; }

		[NotMapped]
		public string TableName => "CheckInTimerOverrides";

		[NotMapped]
		public string IdName => "CheckInTimerOverrideId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CheckInTimerOverrideId; }
			set { CheckInTimerOverrideId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
