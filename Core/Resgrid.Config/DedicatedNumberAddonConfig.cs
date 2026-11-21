namespace Resgrid.Config
{
	/// <summary>
	/// Dedicated Number add-on (PlanAddonTypes.DedicatedNumber = 7; call-intake-and-ai-destinations-plan.md): monthly price,
	/// provider ids and fair-use limits. Checkout lives in the Billing API (int-CommonApis); Core proxies it. Environment keys:
	/// RESGRID:DedicatedNumberAddonConfig:&lt;Field&gt;. The Stripe price id lives on the PlanAddons row (M0323, ExternalId /
	/// TestExternalId); the Paddle price id is <see cref="PaddlePriceId"/>. Sold as "Private Inbound Dispatch Number". Live ids set
	/// 2026-11-20: Stripe product prod_VfEnZg5YGLGGMp / price price_0UduJSqJFDZJcnkV8cIpUYFE (USD 15/month, seeded on the PlanAddons
	/// row by M0323), Paddle product pro_01m7wvch9xs37n1k6a0chvt20f / price pri_01m7wvdpreke6pn6mdc3mwc3ew (EUR 20/month); test-mode
	/// ids are still empty, so test-mode checkout fails closed.
	/// </summary>
	public static class DedicatedNumberAddonConfig
	{
		/// <summary>Fixed PlanAddons row id (M0323) so every data center's catalog row matches.</summary>
		public const string PlanAddonId = "3b1c7f0e-5d2a-4c86-9a4e-7f2d1e6b8c40";

		public static string StripeProductId = "prod_VfEnZg5YGLGGMp";
		public static string StripeTestProductId = "";
		public static string PaddleProductId = "pro_01m7wvch9xs37n1k6a0chvt20f";
		public static string PaddleTestProductId = "";
		public static string PaddlePriceId = "pri_01m7wvdpreke6pn6mdc3mwc3ew";
		public static string PaddleTestPriceId = "";

		/// <summary>USD per month through Stripe (US cluster; US and Canadian numbers). Decided 2026-11-19.</summary>
		public static decimal StripeMonthlyAmount = 15m;

		/// <summary>EUR per month through Paddle (EU cluster; a number in the department's country). Decided 2026-11-19.</summary>
		public static decimal PaddleMonthlyAmount = 20m;

		/// <summary>Countries numbers may be bought in for Stripe (US cluster) departments, comma-separated ISO codes.</summary>
		public static string StripeCountries = "US,CA";

		/// <summary>
		/// Countries numbers may be bought in for Paddle (EU cluster) departments; Twilio needs a regulatory bundle per country
		/// (operator task), so only countries the operator has set up belong here.
		/// </summary>
		public static string PaddleCountries = "";

		/// <summary>Twilio regulatory bundle SID per country for countries that need one, "GB=BU…,DE=BU…".</summary>
		public static string RegulatoryBundles = "";

		/// <summary>Enhanced AI voice intake calls per department per calendar month (UTC) before the line plays the over-limit message.</summary>
		public static int MonthlyVoiceIntakeLimit = 100;

		/// <summary>Inbound texts per department per calendar month that create calls before an admin alert is raised (calls are still created).</summary>
		public static int MonthlySmsIntakeLimit = 2000;

		/// <summary>
		/// Days after the department stopped paying (the add-on or its entity plan ended, cancelled or unpaid) before the number is
		/// released to the carrier so Resgrid stops paying for it. Decided 2026-11-20: a month. Intake already stops after
		/// <see cref="LapseDelayHours"/>; paying again before release restores the number.
		/// </summary>
		public static int ReleaseGraceDays = 30;

		/// <summary>
		/// Days before release when the department's admins are warned again (plan §8); the first warning goes when payment stops.
		/// Comma-separated, descending. Decided 2026-11-20: 15, 5 and 1.
		/// </summary>
		public static string ReleaseWarningDays = "15,5,1";

		/// <summary>
		/// Hours an Active number keeps routing after worker 80 first finds no paid add-on interval, before it is marked Lapsed. A paid
		/// interval ends at the period boundary and the renewal invoice is paid a little later (card retries take days), so a number is
		/// never cut off at every renewal.
		/// </summary>
		public static int LapseDelayHours = 72;

		/// <summary>Minutes a Provisioning row may wait for its carrier purchase before worker 80 marks it Failed (a crashed request).</summary>
		public static int ProvisioningTimeoutMinutes = 30;
	}
}
