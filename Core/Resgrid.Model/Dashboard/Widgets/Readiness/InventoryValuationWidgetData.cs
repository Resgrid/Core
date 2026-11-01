using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Readiness
{
	/// <summary>inventory.valuation (plan §7.7): value on hand per currency and the highest-value holders (Purchasing → Valuation).</summary>
	public sealed class InventoryValuationWidgetData
	{
		/// <summary>False before the department initialized the modern inventory (no valuation exists).</summary>
		public bool Migrated { get; set; }

		public DateTime? AsOfUtc { get; set; }

		public bool HasNegativeStock { get; set; }

		public List<InventoryValuationTotalItem> Totals { get; set; } = new List<InventoryValuationTotalItem>();

		/// <summary>Holders by known value, highest first, at most maxRows.</summary>
		public List<InventoryValuationLocationItem> Locations { get; set; } = new List<InventoryValuationLocationItem>();
	}

	public sealed class InventoryValuationTotalItem
	{
		/// <summary>ISO currency; null for items whose currency is not set.</summary>
		public string CurrencyCode { get; set; }

		public decimal KnownValue { get; set; }

		/// <summary>Lines without a known cost (not included in KnownValue).</summary>
		public int UncostedRows { get; set; }
	}

	public sealed class InventoryValuationLocationItem
	{
		public string LocationId { get; set; }

		public string Name { get; set; }

		public string CurrencyCode { get; set; }

		public decimal KnownValue { get; set; }

		public int Lines { get; set; }
	}
}
