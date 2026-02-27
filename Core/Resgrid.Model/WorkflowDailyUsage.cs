using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Tracks how many times an outbound-messaging action (Email, SMS) has been sent
	/// by a department on a given UTC calendar day. Used to enforce per-day send limits.
	/// </summary>
	public class WorkflowDailyUsage : IEntity
	{
		public string WorkflowDailyUsageId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="WorkflowActionType"/> (e.g. SendEmail=0, SendSms=1).</summary>
		[Required]
		public int ActionType { get; set; }

		/// <summary>UTC calendar date for this usage record (time component is always midnight).</summary>
		[Required]
		public DateTime UsageDate { get; set; }

		/// <summary>Number of messages sent for this department/action/date combination.</summary>
		[Required]
		public int SendCount { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => WorkflowDailyUsageId;
			set => WorkflowDailyUsageId = (string)value;
		}

		[NotMapped] public string TableName => "WorkflowDailyUsages";
		[NotMapped] public string IdName => "WorkflowDailyUsageId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
