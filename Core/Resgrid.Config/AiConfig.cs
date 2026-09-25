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
	}
}
