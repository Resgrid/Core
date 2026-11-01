using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>
	/// workforce.fieldCosting (plan §7.9): the latest field cost runs and their totals. Run totals are metadata (the Workforce catalog
	/// protects per-person pay and cost components, not run totals).
	/// </summary>
	public sealed class WorkforceFieldCostingWidgetData
	{
		/// <summary>Newest first; at most the maxRows setting.</summary>
		public List<WorkforceFieldCostRunRow> Runs { get; set; } = new List<WorkforceFieldCostRunRow>();

		/// <summary>Runs still in Draft or NeedsReview among those listed.</summary>
		public int OpenRuns { get; set; }

		/// <summary>Missing inputs across the listed runs.</summary>
		public int MissingInputs { get; set; }
	}

	public sealed class WorkforceFieldCostRunRow
	{
		public string FieldCostRunId { get; set; }

		/// <summary>FieldCostContextTypes name (Bid, Call, Deployment…) — W.workforce.fieldCosting.Context.{name}.</summary>
		public string ContextType { get; set; }

		/// <summary>FieldCostRunStatuses name (Draft, NeedsReview, Frozen, Superseded) — W.workforce.fieldCosting.Status.{name}.</summary>
		public string Status { get; set; }

		public string Currency { get; set; }

		public decimal TotalLoadedCost { get; set; }

		public decimal? RevenueAmount { get; set; }

		public decimal? ContributionMargin { get; set; }

		public decimal? ContributionMarginPercent { get; set; }

		public int MissingInputCount { get; set; }

		public DateTime? ThroughDate { get; set; }

		public DateTime AddedOn { get; set; }
	}
}
