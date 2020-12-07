namespace Resgrid.Config
{
	public static class PaymentProviderConfig
	{
		public static bool IsTestMode = true;

		public static string ProductionApiKey = "";
		public static string TestApiKey = "";

		public static string ProductionClientKey = "";
		public static string TestClientKey = "";

		public static string ProductionWebhookSigningKey = "";
		public static string TestWebhookSigningKey = "";
	}
}
