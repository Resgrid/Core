namespace Resgrid.Model
{
	public enum PlanAddonTypes
	{
		PTT = 1,
		ADP = 2,
		ReadinessPro = 3,
		/// <summary>Workforce &amp; Business Operations (plan decision 42): monthly add-on gating customer invoicing, contractor billing, Cal OES MARS and workforce costing. Registry 2026-09-18.</summary>
		BusinessOperations = 4,
		/// <summary>Enhanced AI (enhanced-ai-addon-plan.md): monthly add-on (USD 95 Stripe US / EUR 145 Paddle EU) gating model-backed features. Checkout lives in the Billing API. Registry §4E.</summary>
		EnhancedAi = 5
	}
}
