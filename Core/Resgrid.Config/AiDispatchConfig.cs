namespace Resgrid.Config
{
	/// <summary>
	/// Operator settings for AI dispatch (ai-dispatch-template-plan.md; enhanced-ai-addon-plan.md §4). Only Enrich mode exists:
	/// the call is created and dispatched deterministically first, then enriched off-thread on the operator's ai-text endpoint
	/// (AiConfig) at the lowest admission priority. Triage mode waits for the ai-dispatch GPU. Environment keys:
	/// RESGRID:AiDispatchConfig:{Field}.
	/// </summary>
	public static class AiDispatchConfig
	{
		/// <summary>Host kill switch for enrichment. Off leaves AI-format departments on plain GenericTemplate calls.</summary>
		public static bool EnrichEnabled = false;

		/// <summary>Below this model confidence nothing is written to the call; the attempt is audited only.</summary>
		public static double MinimumConfidence = 0.6;

		/// <summary>One model request. Generous because the shared ai-text card serves interactive work first.</summary>
		public static int RequestTimeoutSeconds = 90;

		/// <summary>How long an enrichment waits for an idle inference slot before giving up (the call already exists).</summary>
		public static int AdmissionWaitSeconds = 120;

		/// <summary>Message text sent to the model is truncated to this many characters (subject and body together).</summary>
		public static int MaxMessageCharacters = 3000;

		/// <summary>Recent active calls offered to the model as possible duplicates.</summary>
		public static int MaxCandidateCalls = 8;

		/// <summary>Tokens reserved per enrichment against the department's monthly Enhanced AI budget.</summary>
		public static int ReservedTokens = 8192;

		/// <summary>
		/// Classify with the operator's decision model (AiConfig.Decision*) when it is a SelfHosted endpoint: whether the message is a
		/// dispatch, call type, priority and possible duplicate become per-field probabilities, and the generative model keeps
		/// extraction, title and summary. Never Workers AI: call messages carry PII and PHI. Off, a Workers AI provider or the decision
		/// model unavailable leaves classification on the generative model.
		/// </summary>
		public static bool DecisionsEnabled = false;

		/// <summary>One decision request. Past this the generative model's own classification stands.</summary>
		public static int DecisionTimeoutMilliseconds = 5000;

		/// <summary>When the decision model is at least this sure the message is not a dispatch, the generative model is not called (nothing would be filled).</summary>
		public static double SkipGenerationNotDispatchProbability = 0.9;
	}
}
