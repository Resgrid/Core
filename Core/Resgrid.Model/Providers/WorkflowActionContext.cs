namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Carries all data needed by an action executor for a single workflow step execution.
	/// </summary>
	public sealed class WorkflowActionContext
	{
		/// <summary>The Scriban-rendered output from the step template.</summary>
		public string RenderedContent { get; init; }

		/// <summary>Decrypted credential fields as a JSON string (deserialized by each executor).</summary>
		public string DecryptedCredentialJson { get; init; }

		/// <summary>Action-specific configuration as a JSON string (deserialized by each executor).</summary>
		public string ActionConfigJson { get; init; }

		public string WorkflowId { get; init; }
		public string WorkflowStepId { get; init; }
		public string WorkflowRunId { get; init; }
		public int DepartmentId { get; init; }

		/// <summary>Maps to <see cref="WorkflowActionType"/>.</summary>
		public int ActionType { get; init; }
	}
}
