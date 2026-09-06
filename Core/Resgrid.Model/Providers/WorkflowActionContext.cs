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

		/// <summary>Action-specific configuration as a JSON string (deserialized by each executor). May have had Scriban expressions resolved.</summary>
		public string ActionConfigJson { get; init; }

		public string WorkflowId { get; init; }
		public string WorkflowStepId { get; init; }
		public string WorkflowRunId { get; init; }
		public int DepartmentId { get; init; }

		/// <summary>Maps to <see cref="WorkflowActionType"/>.</summary>
		public int ActionType { get; init; }

		/// <summary>
		/// True when the department is on the free plan. Executors use this to enforce stricter
		/// recipient caps and per-action send limits.
		/// </summary>
		public bool IsFreePlanDepartment { get; init; }

		/// <summary>
		/// A file the step carries alongside its rendered content (RMS plan section 5.6: department report
		/// exports). Email actions attach it; file-upload actions upload its bytes under its file name instead
		/// of the rendered text. Null for every other step.
		/// </summary>
		public WorkflowAttachment Attachment { get; init; }
	}

	/// <summary>A rendered export handed to an executor: bytes, name and content type; never a path.</summary>
	public sealed class WorkflowAttachment
	{
		public string FileName { get; init; }
		public string ContentType { get; init; }
		public byte[] Data { get; init; }

		/// <summary>True when protected fields were withheld from the export (ADP enforcement without an acknowledged egress).</summary>
		public bool Redacted { get; init; }

		/// <summary>The stored RmsExportRun this attachment came from, for the run log.</summary>
		public string ExportRunId { get; init; }
	}
}
