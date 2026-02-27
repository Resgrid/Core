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

		public static WorkflowActionResult Succeeded(string message = null) =>
			new WorkflowActionResult { Success = true, ResultMessage = message };

		public static WorkflowActionResult Failed(string message, string detail = null) =>
			new WorkflowActionResult { Success = false, ResultMessage = message, ErrorDetail = detail };
	}
}

