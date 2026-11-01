using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Readiness
{
	/// <summary>inventory.alerts (plan §7.7): open alerts by type and the most urgent items.</summary>
	public sealed class InventoryAlertsWidgetData
	{
		/// <summary>
		/// False before the department initialized the modern inventory: only LowStock is filled, from the legacy consolidated
		/// inventory (amount at or below zero), and the module page offers Initialize.
		/// </summary>
		public bool Migrated { get; set; }

		public int LowStock { get; set; }

		public int ExpiringSoon { get; set; }

		public int Expired { get; set; }

		public int OverdueReturn { get; set; }

		public int DepartedHolder { get; set; }

		public int Total { get; set; }

		/// <summary>The department has more alerts than one summary reads; counts are a lower bound.</summary>
		public bool Truncated { get; set; }

		public List<InventoryAlertsItem> Items { get; set; } = new List<InventoryAlertsItem>();
	}

	public sealed class InventoryAlertsItem
	{
		/// <summary>InventoryAlertType: 0 low stock, 1 expiring soon, 2 expired, 3 overdue return, 4 departed holder.</summary>
		public int AlertType { get; set; }

		public string ItemId { get; set; }

		public string ItemName { get; set; }

		/// <summary>Null for a department-wide alert.</summary>
		public string LocationId { get; set; }

		/// <summary>Open alerts of this type for the item at that holder (lots, assets or issuances).</summary>
		public int Count { get; set; }

		public decimal? Quantity { get; set; }

		public DateTime? DueOn { get; set; }

		public DateTime? OpenedOn { get; set; }

		/// <summary>"danger" expired/overdue/departed, "warning" low stock, "info" expiring soon.</summary>
		public string Tone { get; set; }
	}
}
