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

		public static string PTT10UserAddonPackage = "";
		public static string PTT10UserAddonPackageTest = "";

		public static string TenEntityPackage = "";
		public static string TenEntityPackageTest = "";

		public static string GetStripeClientKey()
		{
			if (IsTestMode)
				return TestClientKey;
			else
				return ProductionClientKey;
		}

		public static string GetStripeApiKey()
		{
			if (IsTestMode)
				return TestApiKey;
			else
				return ProductionApiKey;
		}

		public static string GetPTT10UserAddonPackageId()
		{
			if (IsTestMode)
				return PTT10UserAddonPackageTest;
			else
				return PTT10UserAddonPackage;
		}

		public static string GetTenEntityPackageId()
		{
			if (IsTestMode)
				return TenEntityPackageTest;
			else
				return TenEntityPackage;
		}
	}
}
