﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("WorkflowSteps")]
	public class WorkflowStep : IEntity
	{
		[Key]
		[Required]
		public string WorkflowStepId { get; set; }

		[Required]
		public string WorkflowId { get; set; }

		[ForeignKey("WorkflowId")]
		public virtual Workflow Workflow { get; set; }

		/// <summary>Maps to <see cref="WorkflowActionType"/>.</summary>
		[Required]
		public int ActionType { get; set; }

		/// <summary>Execution order within the workflow (ascending).</summary>
		public int StepOrder { get; set; }

		/// <summary>Scriban template that produces the output content for the action.</summary>
		[Required]
		public string OutputTemplate { get; set; }

		/// <summary>JSON blob with action-specific settings (To, Subject, URL, bucket, path, etc.).</summary>
		public string ActionConfig { get; set; }

		public string WorkflowCredentialId { get; set; }

		[ForeignKey("WorkflowCredentialId")]
		public virtual WorkflowCredential Credential { get; set; }

		public bool IsEnabled { get; set; } = true;

		[Required]
		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => WorkflowStepId;
			set => WorkflowStepId = (string)value;
		}

		[NotMapped] public string TableName => "WorkflowSteps";
		[NotMapped] public string IdName => "WorkflowStepId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "Workflow", "Credential" };
	}
}
