using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Intelligence
{
	/// <summary>
	/// aiDispatch.triage (plan §7.1): calls created from inbound AI-format email/SMS that are still active, with what the model did,
	/// its confidence and age. Call numbers only (the audit is metadata-only); calls outside the viewer's dispatch scope are left out.
	/// </summary>
	public sealed class AiDispatchTriageWidgetData
	{
		/// <summary>The hours setting the counts cover.</summary>
		public int Hours { get; set; }

		/// <summary>Active calls (in the viewer's scope) the dispatcher should look at.</summary>
		public int NeedsReview { get; set; }

		/// <summary>Enrichments still running (department-wide).</summary>
		public int InProgress { get; set; }

		/// <summary>Enrichments applied in the window (department-wide).</summary>
		public int Applied { get; set; }

		/// <summary>Enrichment attempts in the window (department-wide).</summary>
		public int Total { get; set; }

		/// <summary>Needs-review first, then newest; at most the maxRows setting.</summary>
		public List<AiDispatchTriageRow> Rows { get; set; } = new List<AiDispatchTriageRow>();
	}

	public sealed class AiDispatchTriageRow
	{
		public int CallId { get; set; }

		public string CallNumber { get; set; }

		/// <summary>AiDispatchOutcomes value (Applied, LowConfidence, …) — W.aiDispatch.triage.Outcome.{value}.</summary>
		public string Outcome { get; set; }

		/// <summary>0–100; null when the model returned none.</summary>
		public int? ConfidencePercent { get; set; }

		public int? RelatedCallId { get; set; }

		public string RelatedCallNumber { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		public bool NeedsReview { get; set; }
	}
}
