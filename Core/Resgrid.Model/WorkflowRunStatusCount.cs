using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Runs of one workflow in one <see cref="WorkflowRunStatus"/> (home dashboard Workflow Health widget): an aggregate row, never
	/// the run's payload or error text.
	/// </summary>
	public sealed class WorkflowRunStatusCount
	{
		public string WorkflowId { get; set; }

		public int Status { get; set; }

		public int Total { get; set; }

		/// <summary>The newest run start in the group.</summary>
		public DateTime? LastOn { get; set; }
	}
}
