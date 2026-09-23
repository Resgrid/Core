using System;
using System.Collections.Generic;

namespace Resgrid.Model.Inventories
{
	public enum InventoryCountStatus { Draft = 0, AwaitingWitness = 1, Completed = 2, Cancelled = 3 }
	/// <summary>DepartedHolder: stock or assets still at a personnel location whose member was removed, disabled or hidden (a recovery task).</summary>
	public enum InventoryAlertType { LowStock = 0, ExpiringSoon = 1, Expired = 2, OverdueReturn = 3, DepartedHolder = 4 }
	public sealed class InventoryCount : InventoryMutableRow
	{
		public string LocationId { get; set; }
		public int Status { get; set; }
		public string SnapshotFingerprint { get; set; }
		public DateTime SnapshotOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string OperationId { get; set; }
	}
	public sealed class InventoryCountItem : InventoryRow
	{
		public string CountId { get; set; }
		public string ItemId { get; set; }
		public string LocationId { get; set; }
		public string LotId { get; set; }
		public string AssetId { get; set; }
		public decimal ExpectedQuantity { get; set; }
		public decimal? CountedQuantity { get; set; }
		public string TransactionId { get; set; }
	}
	public sealed class InventoryCountContent
	{
		public string Name { get; set; }
		public string Note { get; set; }
		public int VarianceLineCount { get; set; }
		/// <summary>The locations a location-scoped count fences (root plus, when asked, its compartments and kit containers).</summary>
		public List<string> ScopeLocationIds { get; set; }
		public List<InventoryValuationTotal> Totals { get; set; } = new();
	}
	public sealed class InventoryCountItemContent
	{
		public string ItemName { get; set; }
		public string UnitOfMeasure { get; set; }
		public string SerialNumber { get; set; }
		public string LotNumber { get; set; }
		public decimal? UnitCost { get; set; }
		public string CurrencyCode { get; set; }
	}
	public sealed class InventoryCountInput
	{
		public string Id { get; set; }
		public string LocationId { get; set; }
		public string Name { get; set; }
		public string Note { get; set; }
		/// <summary>
		/// Add a zero-expected line for every active bulk catalog item not already at the location, so a surplus of an unlisted
		/// item can be counted. Null keeps the original behaviour (on); a field count of an apparatus turns it off because the
		/// whole catalog would blow the 100-line count limit.
		/// </summary>
		public bool? IncludeCatalogItems { get; set; }
		/// <summary>Also count the location's compartments (child locations) and the kit containers sitting in it.</summary>
		public bool IncludeChildLocations { get; set; }
	}
	/// <summary>What a field app may do with inventory (and, for a unit, the unit's own locations to count).</summary>
	public sealed class InventoryFieldAccess
	{
		/// <summary>Module on for the department (writes also need <see cref="Migrated"/>).</summary>
		public bool Enabled { get; set; }
		public bool Migrated { get; set; }
		/// <summary>AdjustInventory at the unit's holder location (or department-wide when no unit was asked for).</summary>
		public bool CanCount { get; set; }
		public bool CanIssue { get; set; }
		public bool CanTransfer { get; set; }
		public List<InventoryFieldLocation> UnitLocations { get; set; } = new();
	}
	public sealed class InventoryFieldLocation
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string ParentLocationId { get; set; }
		public int LocationType { get; set; }
		/// <summary>The unit's holder location itself (compartments and containers hang off it).</summary>
		public bool IsRoot { get; set; }
	}
	public sealed class InventoryCountObservation { public string Id { get; set; } public decimal Quantity { get; set; } }
	public sealed class InventoryCountUpdate { public string CountId { get; set; } public int Revision { get; set; } public List<InventoryCountObservation> Lines { get; set; } = new(); }
	public sealed class InventoryCountComplete { public string CountId { get; set; } public int Revision { get; set; } public string RequestId { get; set; } }
	public sealed class InventoryCountDetail { public InventoryCount Count { get; set; } public List<InventoryCountItem> Lines { get; set; } = new(); }
	/// <summary>Content is empty: unattended evaluation uses dates/statuses and attended low-stock projections only.</summary>
	public sealed class InventoryAlert : InventoryRow
	{
		public int AlertType { get; set; }
		public string DedupKey { get; set; }
		public string ItemId { get; set; }
		public string LocationId { get; set; }
		public string LotId { get; set; }
		public string AssetId { get; set; }
		public string IssuanceId { get; set; }
		public int Status { get; set; }
		public DateTime OpenedOn { get; set; }
		public DateTime? ResolvedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public decimal? Quantity { get; set; }
	}
	public sealed class InventoryAlertDelivery : InventoryRow
	{
		public string AlertId { get; set; }
		public string UserId { get; set; }
		public int State { get; set; }
		public DateTime NextAttemptOn { get; set; }
		public DateTime? LeaseUntil { get; set; }
		public string ClaimToken { get; set; }
		public int AttemptCount { get; set; }
		public DateTime? HandedOffOn { get; set; }
	}
	public enum InventoryReportKind { OnHand = 0, Usage = 1, Expiration = 2, LowStock = 3, TransferHistory = 4, Issuance = 5, Valuation = 6, ControlledSubstanceLog = 7 }
	public sealed class InventoryReportInput
	{
		public InventoryReportKind Kind { get; set; }
		public DateTime? FromUtc { get; set; }
		public DateTime? UntilUtc { get; set; }
		public string LocationId { get; set; }
		public string ItemId { get; set; }
		public string UserId { get; set; }
		public int? UnitId { get; set; }
	}
	public sealed class InventoryReport
	{
		public InventoryReportKind Kind { get; set; }
		public DateTime GeneratedOn { get; set; }
		public DateTime? FromUtc { get; set; }
		public DateTime? UntilUtc { get; set; }
		public List<string> Columns { get; set; } = new();
		public List<Dictionary<string, object>> Rows { get; set; } = new();
		public List<InventoryValuationTotal> Totals { get; set; } = new();
	}
}
