namespace Resgrid.Config
{
	/// <summary>
	/// Operator settings for Enhanced AI destination advice (call-intake-and-ai-destinations-plan.md D4, D9; worker 81). The model reads
	/// the call's nature, note and call notes on the operator's self-hosted ai-text endpoint (AiConfig) only, recommends one of the
	/// rule-ranked destinations, and never changes the call: a person applies or dismisses the advice. Environment keys:
	/// RESGRID:AiDestinationConfig:{Field}.
	/// </summary>
	public static class AiDestinationConfig
	{
		/// <summary>Host kill switch. Off: no reviews are queued, worker 81 idles and the call pages show no advice panel.</summary>
		public static bool Enabled = false;

		/// <summary>Below this confidence a change is still shown on the call, but no call note is written and nobody is alerted.</summary>
		public static double MinimumConfidence = 0.6;

		/// <summary>
		/// A review waits this long after the first trigger (call saved, note added, destination availability changed) so a burst of
		/// notes costs one model call.
		/// </summary>
		public static int DebounceSeconds = 60;

		/// <summary>A claimed review not finished in this time (a worker that stopped) is claimed again.</summary>
		public static int LeaseSeconds = 300;

		/// <summary>Newest call notes sent to the model.</summary>
		public static int MaxCallNotes = 20;

		/// <summary>Each call note is cut to this many characters.</summary>
		public static int MaxNoteChars = 500;

		/// <summary>The call's nature and its note field are each cut to this many characters.</summary>
		public static int MaxCallTextChars = 2000;

		/// <summary>Rule-ranked destinations offered to the model (plus up to five more that add a capability the shortlist lacks).</summary>
		public static int MaxCandidates = 10;

		/// <summary>One background model request.</summary>
		public static int RequestTimeoutSeconds = 60;

		/// <summary>An "Ask Enhanced AI" request (waiting for an inference slot and the model together) gives up after this.</summary>
		public static int OnDemandTimeoutSeconds = 20;

		/// <summary>How long a background review waits for an idle inference slot before it is retried on a later pass.</summary>
		public static int AdmissionWaitSeconds = 30;

		/// <summary>Tokens reserved per review against the department's monthly Enhanced AI budget.</summary>
		public static int ReservedTokens = 6144;

		/// <summary>Reviews one worker 81 pass processes at most.</summary>
		public static int MaxReviewsPerPass = 25;

		/// <summary>Processed review rows older than this are deleted by worker 81.</summary>
		public static int ReviewRetentionDays = 7;

		/// <summary>How long a department's entitlement answer is reused by the review triggers (bounds the Billing API calls).</summary>
		public static int AvailabilityCacheSeconds = 300;

		/// <summary>Units on the call pushed when a confident change is advised (the transporting crew is one of them).</summary>
		public static int MaxUnitsNotified = 10;
	}
}
