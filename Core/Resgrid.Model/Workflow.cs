using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("Workflows")]
	public class Workflow : IEntity
	{
		[Key]
		[Required]
		public string WorkflowId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(250)]
		public string Name { get; set; }

		[MaxLength(1000)]
		public string Description { get; set; }

		/// <summary>Maps to <see cref="WorkflowTriggerEventType"/>.</summary>
		[Required]
		public int TriggerEventType { get; set; }

		public bool IsEnabled { get; set; } = true;

		public int MaxRetryCount { get; set; } = 3;

		public int RetryBackoffBaseSeconds { get; set; } = 5;

		[Required]
		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public virtual ICollection<WorkflowStep> Steps { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => WorkflowId;
			set => WorkflowId = (string)value;
		}

		[NotMapped] public string TableName => "Workflows";
		[NotMapped] public string IdName => "WorkflowId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Steps" };
	}
}
