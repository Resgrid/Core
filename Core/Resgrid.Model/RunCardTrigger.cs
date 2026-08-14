using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// A single match condition for a run card. Multiple triggers on one card are OR'd
	/// together. Priority follows the call priority convention (0-3 = system CallPriority,
	/// above 3 = DepartmentCallPriorityId); CallTypeId is a real FK to CallTypes and is
	/// resolved from Call.Type's name at match time.
	/// </summary>
	[Table("RunCardTriggers")]
	public class RunCardTrigger : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RunCardTriggerId { get; set; }

		[Required]
		[ForeignKey("RunCard"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int RunCardId { get; set; }

		public virtual RunCard RunCard { get; set; }

		/// <summary>RunCardTriggerTypes value.</summary>
		public int TriggerType { get; set; }

		public int? Priority { get; set; }

		public int? CallTypeId { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardTriggerId; }
			set { RunCardTriggerId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardTriggers";

		[NotMapped]
		public string IdName => "RunCardTriggerId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "RunCard" };
	}
}
