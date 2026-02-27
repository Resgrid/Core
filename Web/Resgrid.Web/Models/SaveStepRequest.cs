using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Models
{
	/// <summary>JSON body model for the SaveStep AJAX action.</summary>
	public sealed class SaveStepRequest
	{
		public string WorkflowStepId { get; set; }

		[Required]
		public string WorkflowId { get; set; }

		public int ActionType { get; set; }

		public int StepOrder { get; set; }

		[Required]
		public string OutputTemplate { get; set; }

		public string ActionConfig { get; set; }

		public string WorkflowCredentialId { get; set; }

		public bool IsEnabled { get; set; } = true;
	}
}

