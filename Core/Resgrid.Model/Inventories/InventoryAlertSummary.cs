using System;
using System.Collections.Generic;

namespace Resgrid.Model.Inventories
{
	/// <summary>One open-alert bucket as the store counts it: every open alert of one type for one item at one holder (or department-wide).</summary>
	public sealed class InventoryAlertGroupCount
	{
		public int AlertType { get; set; }
		public string ItemId { get; set; }
		/// <summary>Null for a department-wide alert (low stock across every holder).</summary>
		public string LocationId { get; set; }
		public int AlertCount { get; set; }
		public decimal? Quantity { get; set; }
		public DateTime? EarliestDueOn { get; set; }
		public DateTime LatestOpenedOn { get; set; }
	}

	/// <summary>Open inventory alerts the actor may see, counted by type (the Alerts tab's row rule), plus the most urgent buckets.</summary>
	public sealed class InventoryAlertSummary
	{
		public DateTime AsOfUtc { get; set; }
		/// <summary>Visible open alerts keyed by <see cref="InventoryAlertType"/>.</summary>
		public Dictionary<int, int> CountsByType { get; set; } = new Dictionary<int, int>();
		public int Total { get; set; }
		/// <summary>The store returned its cap of buckets; counts are a lower bound.</summary>
		public bool Truncated { get; set; }
		public List<InventoryAlertSummaryItem> Top { get; set; } = new List<InventoryAlertSummaryItem>();
	}

	public sealed class InventoryAlertSummaryItem
	{
		public int AlertType { get; set; }
		public string ItemId { get; set; }
		public string ItemName { get; set; }
		public string LocationId { get; set; }
		public int Count { get; set; }
		public decimal? Quantity { get; set; }
		public DateTime? DueOn { get; set; }
		public DateTime OpenedOn { get; set; }
	}
}
