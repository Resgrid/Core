namespace Resgrid.Config
{
	/// <summary>
	/// Department-level monthly Enhanced AI add-on prices, provider product ids and the Admin Assist free allowance
	/// (enhanced-ai-addon-plan.md; PlanAddonTypes.EnhancedAi = 5). Provider checkout lives in the Billing API
	/// (int-CommonApis, AiBillingController, cloned from Business Operations); Core only proxies it. Environment keys:
	/// RESGRID:AiAddonConfig:StripeProductId, :StripeTestProductId, :PaddleProductId, :PaddleTestProductId,
	/// :StripeMonthlyAmount, :PaddleMonthlyAmount and the AdminAssistFree* fields. The Stripe price id lives on the
	/// PlanAddons row (M0238, ExternalId/TestExternalId); the Paddle price id lives in PaymentProviderConfig.PaddleEnhancedAiAddon
	/// (the Readiness Pro convention). Live ids set 2026-09-25: Stripe product prod_VKDzUfEQokuRv5 / price
	/// price_0UJZXtqJFDZJcnkVbvetyYlx (USD 95/month), Paddle product pro_01m3cdgjnjwgzyyg7pe0bz0526 / price
	/// pri_01m3cdhy94qbcmdvjhqk3nkppt (EUR 145/month); test-mode ids are still empty, so test-mode checkout fails closed.
	/// Inference endpoint, model pin and token budgets are operator settings in AiConfig, not here.
	/// </summary>
	public static class AiAddonConfig
	{
		/// <summary>Fixed PlanAddons row id so every data center's catalog row matches (M0238; the PTT/ADP/Readiness Pro convention).</summary>
		public const string PlanAddonId = "e8bb4e6a-d61b-4654-844f-1afb25b60e93";

		public static string StripeProductId = "prod_VKDzUfEQokuRv5";
		public static string StripeTestProductId = "";
		public static string PaddleProductId = "pro_01m3cdgjnjwgzyyg7pe0bz0526";
		public static string PaddleTestProductId = "";

		/// <summary>USD per month through Stripe (US cluster). Decided 2026-09-17; Stripe price created 2026-09-25.</summary>
		public static decimal StripeMonthlyAmount = 95m;

		/// <summary>EUR per month through Paddle (EU cluster). Decided 2026-09-17; Paddle price created 2026-09-25.</summary>
		public static decimal PaddleMonthlyAmount = 145m;

		/// <summary>
		/// Departments without the add-on may still ask Admin Assist a small number of questions on the operator's
		/// self-hosted model. Off disables the free allowance only; paid access and deterministic Admin Assist are unaffected.
		/// </summary>
		public static bool AdminAssistFreeEnabled = true;

		/// <summary>Answered questions per department in the starter window that opens with its first answered question.</summary>
		public static int AdminAssistFreeStarterQuestions = 20;

		/// <summary>Length of the starter window, in days from the department's first answered Admin Assist question.</summary>
		public static int AdminAssistFreeStarterDays = 30;

		/// <summary>Answered questions per department per calendar month (UTC) after the starter window. Unused questions do not roll over.</summary>
		public static int AdminAssistFreeMonthlyQuestions = 4;

		/// <summary>Attempts of any outcome per department per rolling 24 hours on the free allowance, bounding retries that are not charged.</summary>
		public static int AdminAssistFreeDailyAttemptLimit = 25;
	}
}
