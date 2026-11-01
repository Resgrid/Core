using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>
	/// invoicing.receivables (plan §7.9): accounts-receivable aging. Visible whenever Invoicing is (flag + module); a lapsed Business
	/// Operations add-on leaves it read-only, exactly as the Invoicing pages.
	/// </summary>
	public sealed class InvoicingReceivablesWidgetData
	{
		/// <summary>The add-on lapsed: data reads, nothing can be created (the Invoicing banner).</summary>
		public bool ReadOnly { get; set; }

		/// <summary>Current, 1-30, 31-60, 61-90, 90+ in that order.</summary>
		public List<InvoicingAgingBucket> Buckets { get; set; } = new List<InvoicingAgingBucket>();

		/// <summary>Every open balance per currency.</summary>
		public List<DashboardCurrencyAmount> Outstanding { get; set; } = new List<DashboardCurrencyAmount>();

		/// <summary>Balances past due (every bucket but Current) per currency.</summary>
		public List<DashboardCurrencyAmount> Overdue { get; set; } = new List<DashboardCurrencyAmount>();

		public int OpenCount { get; set; }

		public int OverdueCount { get; set; }

		/// <summary>Invoices still in Draft.</summary>
		public int DraftCount { get; set; }
	}

	public sealed class InvoicingAgingBucket
	{
		/// <summary>"Current", "1-30", "31-60", "61-90", "90+" — W.invoicing.receivables.Bucket.{Key}.</summary>
		public string Key { get; set; }

		public int Count { get; set; }

		public List<DashboardCurrencyAmount> Balances { get; set; } = new List<DashboardCurrencyAmount>();
	}
}
