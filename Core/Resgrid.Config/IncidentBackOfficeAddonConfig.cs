namespace Resgrid.Config
{
	/// <summary>
	/// Department-level monthly Incident Back Office add-on prices and provider product ids (large-incident-logistics-finance-plan.md
	/// rev 4.1, decision D6; PlanAddonTypes.IncidentBackOffice = 6). Provider checkout lives in the Billing API (int-CommonApis),
	/// cloned from Business Ops; Core only proxies it. Environment keys: RESGRID:IncidentBackOfficeAddonConfig:StripeProductId,
	/// :StripeTestProductId, :StripePriceId, :StripeTestPriceId, :PaddleProductId, :PaddleTestProductId, :StripeMonthlyAmount,
	/// :PaddleMonthlyAmount. The Paddle price id lives in PaymentProviderConfig.PaddleIncidentBackOfficeAddon (the Readiness Pro
	/// convention). Prices decided 2026-10-26: USD 250/month (Stripe, US) and EUR 350/month (Paddle, EU). Live ids set 2026-10-27:
	/// Stripe product prod_VWFcR3xzTuNhkv / price price_0UVD6mqJFDZJcnkVqBlHZD0w, Paddle product pro_01m5z2ex50evcfvdfw0h0sq6d4 /
	/// price pri_01m5z2fz6wr1msc1s1nwdpeg38; test-mode ids are still empty, so test-mode checkout fails closed.
	/// </summary>
	public static class IncidentBackOfficeAddonConfig
	{
		/// <summary>Fixed PlanAddons row id so every data center's catalog row matches (M0291; the PTT/ADP/Readiness Pro convention).</summary>
		public const string PlanAddonId = "4e7b2c19-6d3a-4f85-b1c0-9a2e5d7f3b61";

		public static string StripeProductId = "prod_VWFcR3xzTuNhkv";
		public static string StripeTestProductId = "";
		public static string StripePriceId = "price_0UVD6mqJFDZJcnkVqBlHZD0w";
		public static string StripeTestPriceId = "";
		public static string PaddleProductId = "pro_01m5z2ex50evcfvdfw0h0sq6d4";
		public static string PaddleTestProductId = "";

		/// <summary>USD per month through Stripe (US cluster). Decided 2026-10-26.</summary>
		public static decimal StripeMonthlyAmount = 250m;

		/// <summary>EUR per month through Paddle (EU cluster). Decided 2026-10-26.</summary>
		public static decimal PaddleMonthlyAmount = 350m;
	}
}
