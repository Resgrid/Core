namespace Resgrid.Model.Providers
{
	/// <summary>Result returned by an action executor after attempting to perform its operation.</summary>
	public sealed class WorkflowActionResult
	{
		public bool Success { get; init; }

		/// <summary>A short human-readable message about the outcome (e.g. HTTP status, SMTP response).</summary>
		public string ResultMessage { get; init; }

		/// <summary>Detailed error information when Success is false.</summary>
		public string ErrorDetail { get; init; }

		/// <summary>HTTP status code when a request completed (HTTP executors); null otherwise.</summary>
		public int? HttpStatus { get; init; }

		/// <summary>SHA-256 (lowercase hex) of the exact request body bytes sent, when a body was sent.</summary>
		public string PayloadSha256 { get; init; }

		/// <summary>Length of the exact request body bytes sent.</summary>
		public int? PayloadBytes { get; init; }

		/// <summary>
		/// Protected mode: the disclosure outcome the executor determined when it refused or failed the send
		/// (ProtectedWorkflowDisclosureOutcomes.BlockedHost for a pin mismatch or redirect, FailedHttp otherwise).
		/// </summary>
		public string ProtectedOutcome { get; init; }

		/// <summary>
		/// Protected mode: values the step's ResponseCapture read from the response, by subject identifier key. In memory
		/// only: WorkflowService writes them through the encrypt lane and drops them. Never serialized, logged or stored here.
		/// </summary>
		[Newtonsoft.Json.JsonIgnore]
		[System.Text.Json.Serialization.JsonIgnore]
		public System.Collections.Generic.IReadOnlyDictionary<string, string> CapturedValues { get; init; }

		public static WorkflowActionResult Succeeded(string message = null) =>
			new WorkflowActionResult { Success = true, ResultMessage = message };

		public static WorkflowActionResult Failed(string message, string detail = null) =>
			new WorkflowActionResult { Success = false, ResultMessage = message, ErrorDetail = detail };
	}
}

