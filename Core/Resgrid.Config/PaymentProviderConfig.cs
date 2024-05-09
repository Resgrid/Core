namespace Resgrid.Config
{
	public static class PaymentProviderConfig
	{
#if DEBUG
		public static bool IsTestMode = true;
#else
		public static bool IsTestMode = false;
#endif
		public static string ProductionApiKey = "";
		public static string TestApiKey = "";

		public static string ProductionClientKey = "";
		public static string TestClientKey = "";

		public static string ProductionWebhookSigningKey = "";
		public static string TestWebhookSigningKey = "";

		public static string ProductionBillingWebhookSigningKey = "";
		public static string TestBillingWebhookSigningKey = "";

		public static string PTT10UserAddonPackage = "6f4c5f8b-584d-4291-8a7d-29bf97ae6aa9";
		public static string PTT10UserAddonPackageTest = "6f4c5f8b-584d-4291-8a7d-29bf97ae6aa9";

		public static string TenEntityPackage = "6f4c5f8b-584d-4291-8a7d-29bf97ae6aa9";
		public static string TenEntityPackageTest = "6f4c5f8b-584d-4291-8a7d-29bf97ae6aa9";

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

		public static string GetStripeWebhookSigningKey()
		{
			if (IsTestMode)
				return TestWebhookSigningKey;
			else
				return ProductionWebhookSigningKey;
		}

		public static string GetStripeBillingWebhookSigningKey()
		{
			if (IsTestMode)
				return TestBillingWebhookSigningKey;
			else
				return ProductionBillingWebhookSigningKey;
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
