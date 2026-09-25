namespace Resgrid.Model.AdminAssist
{
	public static class DispatchProviderOutcome
	{
		/// <summary>Normalize only documented creation-response states. Later receipt stages are not inferred.</summary>
		public static bool? CreationStatus(string status) => status?.ToLowerInvariant() switch
		{
			"accepted" or "queued" or "sending" or "sent" or "delivered" or "read" or "scheduled" or
			"ringing" or "in-progress" or "completed" => true,
			"failed" or "undelivered" or "canceled" or "busy" or "no-answer" => false,
			_ => null
		};
	}
}
