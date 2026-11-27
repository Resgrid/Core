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
		DedicatedNumber = 7,
		/// <summary>
		/// Workforce Pro add-on (shifts-scheduling-modernization-plan.md §7, decision D23; owner 2026-11-26: all member wellness and workplace
		/// exposure moved here): a pack add-on like the LMS, 25-member packs, graduated, monthly, with a per-member seat. The PlanAddons row is
		/// WorkforceProAddonConfig.PlanAddonId; general availability is the operator switching the Workforce.Pro flag on (seeded off). The gate
		/// (IWorkforceProAccessService) fails closed.
		/// </summary>
		WorkforcePro = 8,
		/// <summary>
		/// LMS add-on (protocols-training-knowledge-plan.md §17): courses, requirements, classes and the formal training reports.
		/// A pack add-on (§17.4): 25-member packs, graduated, monthly, with a per-member seat. The PlanAddons row is M0351
		/// (LmsAddonConfig.PlanAddonId); general availability is the operator switching the Training.Lms flag on (seeded off),
		/// not code (§25.4a item 7). The gate (ILmsAccessService) fails closed.
		/// </summary>
		Lms = 9
	}
}
