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
		EnhancedAi = 5,
		/// <summary>Incident Back Office (large-incident-logistics-finance-plan.md rev 4.1, decision D6): monthly add-on (USD 250 Stripe US / EUR 350 Paddle EU) entitling the whole module — LI-A Foundation, LI-B Logistics, LI-C Business and LI-D Coordination. Registry §4C.</summary>
		IncidentBackOffice = 6,
		/// <summary>Dedicated department phone number (call-intake-and-ai-destinations-plan.md, M0323): monthly add-on (USD 15 Stripe US/CA / EUR 20 Paddle EU) for departments with an active paid entity plan; inbound SMS intake and Enhanced AI voice intake.</summary>
		DedicatedNumber = 7
	}
}
