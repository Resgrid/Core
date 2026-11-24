using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model.Inventories
{
	/// <summary>
	/// One row of an inventory import file (see <c>InventoryImportCsv</c>): an item, optionally a quantity on hand at a
	/// location (or one serialized asset), and the catalog details that seed the item. A row without a quantity or serial
	/// number only creates or updates the item. The file states what is on hand: importing sets each location's quantity
	/// to the file's quantity, so a file can be corrected and imported again.
	/// </summary>
	public sealed class InventoryImportRow
	{
		/// <summary>1-based data row (the header is row 0), as shown to the person who uploaded the file.</summary>
		public int RowNumber { get; set; }
		/// <summary>A value that could not be read; the row is refused with this code (and <see cref="ParseColumn"/>).</summary>
		public string ParseError { get; set; }
		public string ParseColumn { get; set; }
		public string Item { get; set; }
		public string Code { get; set; }
		public string Barcode { get; set; }
		public string Description { get; set; }
		/// <summary>A category name, or a path such as "Medical &gt; Airway"; missing categories are created.</summary>
		public string Category { get; set; }
		public string UnitOfMeasure { get; set; }
		public InventoryTrackingMode? Tracking { get; set; }
		public bool? RequiresLot { get; set; }
		public bool? RequiresExpiration { get; set; }
		/// <summary>The cost of one unit on this row: the receipt cost of stock added, or a serialized asset's acquisition cost.</summary>
		public decimal? UnitCost { get; set; }
		public string Currency { get; set; }
		public decimal? MinLevel { get; set; }
		public decimal? MaxLevel { get; set; }
		public decimal? ReorderPoint { get; set; }
		public decimal? ReorderQuantity { get; set; }
		/// <summary>A location name, or a path such as "Warehouse &gt; Shelf A"; under a station or unit it names a compartment.</summary>
		public string Location { get; set; }
		/// <summary>A station (department group) name: the location is that station's storage.</summary>
		public string Station { get; set; }
		/// <summary>A unit (apparatus) name: the location is that unit's storage.</summary>
		public string Unit { get; set; }
		/// <summary>The quantity on hand after the import. Blank leaves stock alone.</summary>
		public decimal? Quantity { get; set; }
		/// <summary>The item's minimum at this row's location.</summary>
		public decimal? ParLevel { get; set; }
		public string Lot { get; set; }
		public DateTime? Expires { get; set; }
		public string SerialNumber { get; set; }
		public string AssetTag { get; set; }
		public string Note { get; set; }
	}

	public sealed class InventoryImportInput
	{
		/// <summary>Idempotency key: applying the same file again with this id returns the first result.</summary>
		public string RequestId { get; set; }
		/// <summary>The hash the preview returned; apply refuses to run when the plan has changed since.</summary>
		public string PreviewHash { get; set; }
		public List<InventoryImportRow> Rows { get; set; } = new();
		/// <summary>File columns the import did not recognize (reported, never read).</summary>
		public List<string> IgnoredColumns { get; set; } = new();
	}

	public enum InventoryImportItemAction { None = 0, Create = 1, Update = 2, Existing = 3 }

	public enum InventoryImportStockAction { None = 0, Receive = 1, Decrease = 2, Unchanged = 3, CreateAsset = 4, AssetExists = 5 }

	public sealed class InventoryImportRowResult
	{
		public int RowNumber { get; set; }
		public string ItemName { get; set; }
		public InventoryImportItemAction ItemAction { get; set; }
		/// <summary>The item fields this row's file values change on an existing item.</summary>
		public List<string> ChangedFields { get; set; } = new();
		public string LocationName { get; set; }
		public bool CreatesLocation { get; set; }
		public bool CreatesCategory { get; set; }
		public string LotNumber { get; set; }
		public bool CreatesLot { get; set; }
		public string SerialNumber { get; set; }
		public InventoryImportStockAction StockAction { get; set; }
		public decimal? CurrentQuantity { get; set; }
		public decimal? NewQuantity { get; set; }
		/// <summary>Signed change to the quantity on hand.</summary>
		public decimal Change { get; set; }
		public decimal? UnitCost { get; set; }
		public string CurrencyCode { get; set; }
		public decimal? ParLevel { get; set; }
		public string ErrorCode { get; set; }
		/// <summary>The file column the error is about, when there is one.</summary>
		public string ErrorColumn { get; set; }
		/// <summary>Codes for things worth knowing that do not stop the row (an asset already elsewhere, for instance).</summary>
		public List<string> Warnings { get; set; } = new();
	}

	public sealed class InventoryImportPreview
	{
		public string PreviewHash { get; set; }
		public List<InventoryImportRowResult> Rows { get; set; } = new();
		public List<string> IgnoredColumns { get; set; } = new();
		public InventoryImportTotals Totals { get; set; } = new();
		/// <summary>The value of stock the import adds, by currency (rows without a cost are counted, not priced).</summary>
		public List<InventoryValuationTotal> AddedValue { get; set; } = new();
		public int ErrorRows { get; set; }
		[JsonIgnore] public bool CanApply => ErrorRows == 0 && Rows.Count > 0;
	}

	/// <summary>What an import created and posted. Kept on the operation receipt, so a replay reports the first run.</summary>
	public sealed class InventoryImportTotals
	{
		public int ItemsCreated { get; set; }
		public int ItemsUpdated { get; set; }
		public int CategoriesCreated { get; set; }
		public int LocationsCreated { get; set; }
		public int LotsCreated { get; set; }
		public int AssetsCreated { get; set; }
		public int ParLevelsSet { get; set; }
		public int Receipts { get; set; }
		public int Decreases { get; set; }
		public int Unchanged { get; set; }
	}

	public sealed class InventoryImportResult
	{
		public string OperationId { get; set; }
		/// <summary>True when this request id had already been applied and the first run's result was returned.</summary>
		public bool AlreadyApplied { get; set; }
		public InventoryImportTotals Totals { get; set; } = new();
		public int TransactionCount { get; set; }
	}

	public static class InventoryImportLimits
	{
		public const int MaxRows = 1000;
		public const int MaxFileBytes = 1024 * 1024;
	}
}
