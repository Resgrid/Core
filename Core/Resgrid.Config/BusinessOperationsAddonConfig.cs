namespace Resgrid.Config
{
	/// <summary>
	/// Department-level monthly Business Operations add-on prices and provider product ids (Workforce &amp; Business
	/// Operations plan, decision 42; PlanAddonTypes.BusinessOperations = 4). Provider checkout lives in the Billing API
	/// (int-CommonApis), cloned from Readiness Pro; Core only proxies it. Environment keys:
	/// RESGRID:BusinessOperationsAddonConfig:StripeProductId, :StripeTestProductId, :StripePriceId, :StripeTestPriceId,
	/// :PaddleProductId, :PaddleTestProductId, :StripeMonthlyAmount, :PaddleMonthlyAmount. The Paddle price id lives in
	/// PaymentProviderConfig.PaddleBusinessOperationsAddon (the Readiness Pro convention). Live ids set 2026-09-18:
	/// Stripe product prod_VHnlBsvKsSpqeP / price price_0UHEA6qJFDZJcnkVnj0ZaAFw (USD 250/month), Paddle product
	/// pro_01m2vrcv37k2pnqseb22d8r244 / price pri_01m2vrdycmx8kfhys5xxcjgqnx (EUR 295/month); test-mode ids are still empty.
	/// </summary>
	public static class BusinessOperationsAddonConfig
	{
		/// <summary>Fixed PlanAddons row id so every data center's catalog row matches (M0211; the PTT/ADP/Readiness Pro convention).</summary>
		public const string PlanAddonId = "8c2f0d6e-5b1a-4f2e-9d3c-7a6b5e4d3c2b";

		public static string StripeProductId = "prod_VHnlBsvKsSpqeP";
		public static string StripeTestProductId = "";
		public static string StripePriceId = "price_0UHEA6qJFDZJcnkVnj0ZaAFw";
		public static string StripeTestPriceId = "";
		public static string PaddleProductId = "pro_01m2vrcv37k2pnqseb22d8r244";
		public static string PaddleTestProductId = "";

		/// <summary>USD per month through Stripe (US cluster). Set 2026-09-18.</summary>
		public static decimal StripeMonthlyAmount = 250m;

		/// <summary>EUR per month through Paddle (EU cluster). Set 2026-09-18.</summary>
		public static decimal PaddleMonthlyAmount = 295m;
	}
}
