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

		/// <summary>
		/// Protected Workflow send (an approved release): the executor refuses redirects, requires TLS 1.2+, applies the
		/// protected timeout, re-checks the rendered URL against <see cref="PinnedHost"/> (and an OAuth2 token URL
		/// against <see cref="PinnedTokenHost"/>), and reports only the status line — never the response body, never
		/// the payload, never an exception message carrying request content, and nothing to Logging.
		/// </summary>
		public bool ProtectedMode { get; init; }

		/// <summary>Exact host the rendered URL must resolve to in protected mode.</summary>
		public string PinnedHost { get; init; }

		/// <summary>Exact OAuth2 token endpoint host in protected mode (null when the credential is not OAuth2).</summary>
		public string PinnedTokenHost { get; init; }

		/// <summary>Protected mode: the OAuth2 client authentication the release pinned (client_secret / private_key_jwt).</summary>
		public string PinnedAuthMethod { get; init; }

		/// <summary>
		/// run.idempotency_key for this step: stable across retries of the same delivery. Sent in the step's
		/// IdempotencyHeader when one is configured.
		/// </summary>
		public string IdempotencyKey { get; init; }

		/// <summary>
		/// Maps to <see cref="WorkflowCredentialType"/> for the attached credential, when one is attached. HTTP executors use it
		/// to pick the auth scheme when the stored credential JSON carries no explicit authType (the web credential editor
		/// stores only the type's fields).
		/// </summary>
		public int? CredentialType { get; init; }
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
