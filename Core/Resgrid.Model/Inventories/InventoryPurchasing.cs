using System;
using System.Collections.Generic;

namespace Resgrid.Model.Inventories
{
	public enum InventoryPurchaseOrderStatus { Draft = 0, Ordered = 1, PartiallyReceived = 2, Received = 3, Cancelled = 4 }
	public sealed class InventoryVendor : InventoryMutableRow { public string ContactId { get; set; } }
	public sealed class InventoryVendorContent { public string AccountNumber { get; set; } public string Note { get; set; } }
	public sealed class InventoryPurchaseOrder : InventoryMutableRow
	{
		public string VendorId { get; set; }
		public int Status { get; set; }
		public string CurrencyCode { get; set; }
		public DateTime? OrderedOn { get; set; }
		public DateTime? ReceivedOn { get; set; }
	}
	public sealed class InventoryPurchaseOrderContent { public string Number { get; set; } public string Note { get; set; } public string SupplierName { get; set; } }
	public sealed class InventoryPurchaseOrderItem : InventoryMutableRow
	{
		public string PurchaseOrderId { get; set; }
		public string ItemId { get; set; }
		public int LineNumber { get; set; }
		public decimal QuantityOrdered { get; set; }
		public decimal QuantityReceived { get; set; }
	}
	public sealed class InventoryPurchaseOrderItemContent { public string ItemName { get; set; } public string UnitOfMeasure { get; set; } public decimal UnitCost { get; set; } public string Note { get; set; } }
	public sealed class InventoryVendorInput { public string Id { get; set; } public int Revision { get; set; } public string ContactId { get; set; } public InventoryVendorContent Details { get; set; } = new(); }
	public sealed class InventoryPurchaseOrderInput
	{
		public string Id { get; set; }
		public int Revision { get; set; }
		public string VendorId { get; set; }
		public string CurrencyCode { get; set; }
		public string Number { get; set; }
		public string Note { get; set; }
		public List<InventoryPurchaseOrderLineInput> Lines { get; set; } = new();
	}
	public sealed class InventoryPurchaseOrderLineInput { public string Id { get; set; } public string ItemId { get; set; } public decimal QuantityOrdered { get; set; } public decimal UnitCost { get; set; } public string Note { get; set; } }
	public sealed class InventoryPurchaseOrderChange { public string Id { get; set; } public int Revision { get; set; } public string RequestId { get; set; } }
	public sealed class InventoryPurchaseReceiptInput { public string PurchaseOrderId { get; set; } public int Revision { get; set; } public string RequestId { get; set; } public List<InventoryPurchaseReceiptLine> Lines { get; set; } = new(); }
	public sealed class InventoryPurchaseReceiptLine
	{
		public string PurchaseOrderItemId { get; set; }
		public string LocationId { get; set; }
		public string LotId { get; set; }
		public decimal Quantity { get; set; }
		public InventoryPurchaseAssetInput Asset { get; set; }
	}
	public sealed class InventoryPurchaseAssetInput { public string SerialNumber { get; set; } public string AssetTag { get; set; } public string Barcode { get; set; } public DateTime? ExpiresOn { get; set; } }
	public sealed class InventoryPurchaseOrderDetail { public InventoryPurchaseOrder Order { get; set; } public List<InventoryPurchaseOrderItem> Lines { get; set; } = new(); public List<InventoryTransaction> Receipts { get; set; } = new(); }
	public sealed class InventoryVendorChoice { public string Id { get; set; } public string Name { get; set; } }
	public sealed class InventoryValuationLine
	{
		public string ItemId { get; set; } public string AssetId { get; set; } public string LotId { get; set; } public string LocationId { get; set; }
		public string ItemName { get; set; } public string CurrencyCode { get; set; } public decimal Quantity { get; set; }
		public decimal? UnitCost { get; set; } public decimal? Value { get; set; }
	}
	public sealed class InventoryValuationTotal { public string CurrencyCode { get; set; } public decimal KnownValue { get; set; } public int UncostedRows { get; set; } }
	public sealed class InventoryValuation { public List<InventoryValuationLine> Lines { get; set; } = new(); public List<InventoryValuationTotal> Totals { get; set; } = new(); public bool HasNegativeStock { get; set; } public DateTime AsOfUtc { get; set; } }
}
