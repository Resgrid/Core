using System;
using System.Collections.Generic;

namespace Resgrid.Model.AiDispatch
{
	/// <summary>
	/// One AI dispatch enrichment on a call that is still active (home dashboard AI Dispatch Triage widget). Metadata only, like the
	/// audit row it reads: the call number, never the call's name, message or extracted values.
	/// </summary>
	public sealed class AiDispatchTriageItem
	{
		public string AiDispatchAuditId { get; set; }
		public int CallId { get; set; }
		public string CallNumber { get; set; }
		public string Outcome { get; set; }
		public decimal? Confidence { get; set; }
		public int? RelatedCallId { get; set; }
		public string RelatedCallNumber { get; set; }
		public DateTime CreatedOnUtc { get; set; }

		/// <summary>The dispatcher should look at the call: the model did not apply its proposal, or flagged a possible duplicate.</summary>
		public bool NeedsReview => AiDispatchTriage.NeedsReview(Outcome, RelatedCallId);
	}

	/// <summary>Recent AI dispatch activity for a department (home dashboard AI Dispatch Triage widget).</summary>
	public sealed class AiDispatchTriageSummary
	{
		public DateTime SinceUtc { get; set; }

		/// <summary>Audit rows per outcome since <see cref="SinceUtc"/>, closed calls included.</summary>
		public Dictionary<string, int> OutcomeCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Enrichments on calls that are still active, newest first.</summary>
		public List<AiDispatchTriageItem> ActiveCalls { get; set; } = new List<AiDispatchTriageItem>();
	}

	public static class AiDispatchTriage
	{
		/// <summary>
		/// Every outcome except Applied and InProgress leaves the deterministic call as the parser made it (placeholder title, no type),
		/// and an Applied enrichment that names a related call asks the dispatcher to check for a duplicate: both need a look.
		/// </summary>
		public static bool NeedsReview(string outcome, int? relatedCallId) =>
			relatedCallId.HasValue || (!string.Equals(outcome, AiDispatchOutcomes.Applied, StringComparison.Ordinal) &&
				!string.Equals(outcome, AiDispatchOutcomes.InProgress, StringComparison.Ordinal));
	}
}
