using System;

namespace Resgrid.Model.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// ibo.finance (plan §7.10): cost to date against budget and the burn rate, from the latest cost projection (IncidentCost_View) and
	/// the budget lines (shown only to IncidentBudget_View, as the finance desk shows them). Budget-level totals are operational
	/// metadata; the per-person protected amounts are never read.
	/// </summary>
	public sealed class IncidentFinanceWidgetData : IncidentWidgetDataBase
	{
		/// <summary>The latest cost projection's cumulative cost; null before the first projection runs.</summary>
		public decimal? CostToDate { get; set; }

		public decimal? DailyBurn { get; set; }

		public decimal? ForecastAtCompletion { get; set; }

		public DateTime? ProjectedEndOn { get; set; }

		/// <summary>When the projection used was computed.</summary>
		public DateTime? ProjectionOn { get; set; }

		/// <summary>The viewer may see budget lines (IncidentBudget_View); Budget is null otherwise.</summary>
		public bool CanViewBudget { get; set; }

		/// <summary>Sum of the budget lines' revised amounts.</summary>
		public decimal? Budget { get; set; }

		/// <summary>CostToDate / Budget × 100, rounded; null without both.</summary>
		public int? BudgetUsedPercent { get; set; }
	}
}
