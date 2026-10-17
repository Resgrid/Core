namespace Resgrid.Config
{
	/// <summary>Operator-owned inference configuration. Department input cannot override endpoint, credentials or admission limits.</summary>
	public static class AiConfig
	{
		public static bool AdminAssistEnabled = false;
		public static bool AllowPrivateEndpoint = false;
		public static string Endpoint = "";
		public static string ApiKey = "";
		public static string Model = "Qwen/Qwen3-8B-AWQ";
		public static string ModelRevision = "";
		public static string RuntimeDigest = "";
		public static string AuditHmacKey = "";
		public static string SelfHostedDepartmentIds = "";
		public static int MonthlyTokenLimit = 2000000;
		public static int TurnTokenLimit = 32768;
		public static int ConversationRetentionDays = 30;

		/// <summary>
		/// Sends a per-department vLLM <c>cache_salt</c> on every operator chat request, so prompt prefix-cache blocks are reused only
		/// within one department and prefill timing cannot reveal another department's prompt. Turn off only for an engine that
		/// rejects the field.
		/// </summary>
		public static bool SendCacheSalt = true;

		/// <summary>Base64 key (32+ bytes) for the salt. Blank falls back to AuditHmacKey; with neither, every request gets a random salt (no reuse at all).</summary>
		public static string CacheSaltKey = "";

		/// <summary>
		/// Where the decision model (Cloudflare Clef / Clef-Flash) runs: "WorkersAi" (Cloudflare's hosted copy; DecisionEndpoint is
		/// https://api.cloudflare.com/client/v4/accounts/{account id}/ai/run/@cf/cloudflare/clef-flash and DecisionApiKey a Workers AI
		/// API token; nothing to pin) or "SelfHosted" (an SGLang server at /v1/systemone, shares AllowPrivateEndpoint, pinned by
		/// DecisionModelRevision and DecisionRuntimeDigest). Workers AI is a third party and receives only requests built from Resgrid's
		/// own reviewed text and from department list names ADP does not protect (Resgrid.Ai.AiNonPrivateFields), never department
		/// content (no PII or PHI leaves Resgrid; the transport enforces it). AI dispatch scores call
		/// messages, so it uses only a SelfHosted endpoint and otherwise classifies with the generative model above.
		/// </summary>
		public static string DecisionProvider = "WorkersAi";
		public static string DecisionEndpoint = "";
		public static string DecisionApiKey = "";
		public static string DecisionModel = "Cloudflare/clef-flash";
		public static string DecisionModelRevision = "";
		public static string DecisionRuntimeDigest = "";
	}
}
