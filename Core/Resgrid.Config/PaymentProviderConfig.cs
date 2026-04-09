using System;

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

		public static string PaddleProductionApiKey = "";
		public static string PaddleTestApiKey = "";
		public static string PaddleProductionBillingWebhookSigningKey = "";
		public static string PaddleTestBillingWebhookSigningKey = "";
		public static string PaddlePTT10UserAddonPackage = "";
		public static string PaddlePTT10UserAddonPackageTest = "";
		public static string PaddleProductionEnvironment = "production";
		public static string PaddleTestEnvironment = "sandbox";
		public static string PaddleProductionClientToken = "";
		public static string PaddleTestClientToken = "";

		// Global toggle: 1 = Stripe (default), 7 = Paddle. Matches PaymentMethods enum values.
		// Set per-instance via ResgridConfig.json: "PaymentProviderConfig.ActivePaymentProvider": "7"
		public static int ActivePaymentProvider = 1;

		public const int ProviderStripe = 1;
		public const int ProviderPaddle = 7;

		public static int GetActivePaymentProvider()
		{
			if (ActivePaymentProvider != ProviderStripe && ActivePaymentProvider != ProviderPaddle)
				throw new InvalidOperationException(
					$"Unsupported ActivePaymentProvider value '{ActivePaymentProvider}'. Expected {ProviderStripe} (Stripe) or {ProviderPaddle} (Paddle).");

			return ActivePaymentProvider;
		}

		public static bool IsStripeActive()
		{
			return GetActivePaymentProvider() == ProviderStripe;
		}

		public static bool IsPaddleActive()
		{
			return GetActivePaymentProvider() == ProviderPaddle;
		}

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

		public static string GetPaddleApiKey()
		{
			if (IsTestMode)
				return PaddleTestApiKey;
			else
				return PaddleProductionApiKey;
		}

		public static string GetPaddleBillingWebhookSigningKey()
		{
			if (IsTestMode)
				return PaddleTestBillingWebhookSigningKey;
			else
				return PaddleProductionBillingWebhookSigningKey;
		}

		public static string GetPaddlePTT10UserAddonPackageId()
		{
			if (IsTestMode)
				return PaddlePTT10UserAddonPackageTest;
			else
				return PaddlePTT10UserAddonPackage;
		}

		public static string GetPaddleEnvironment()
		{
			if (IsTestMode)
				return PaddleTestEnvironment;
			else
				return PaddleProductionEnvironment;
		}

		public static string GetPaddleClientToken()
		{
			if (IsTestMode)
				return PaddleTestClientToken;
			else
				return PaddleProductionClientToken;
		}
	}
}
