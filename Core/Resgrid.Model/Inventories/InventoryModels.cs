using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Inventories
{
	public enum InventoryTrackingMode { Bulk = 0, Serialized = 1 }
	public enum InventoryLocationType { Facility = 0, Station = 1, Unit = 2, Personnel = 3, Container = 4, External = 5 }
	public enum InventoryTransactionType { Migrated = 0, Receive = 1, Consume = 2, Transfer = 3, Issue = 4, Return = 5, Adjust = 6, Count = 7, WriteOff = 8, StatusChange = 9 }
	public enum InventoryAssetStatus { InService = 0, Issued = 1, OutForRepair = 2, Damaged = 3, Lost = 4, Consumed = 5, Retired = 6 }
	public enum InventoryReferenceType { None = 0, LegacyLog = 1, RmsRecord = 2, Call = 3, Transfer = 4, Issuance = 5, PurchaseOrder = 6, Count = 7, Legacy = 8, WorkOrder = 9, Deployment = 10 }
	public enum InventoryIssuanceStatus { Outstanding = 0, Returned = 1, PartiallyReturned = 2, Lost = 3, Consumed = 4 }

	/// <summary>Stable GUID public identities also support checklist/work-order soft references. User-authored data is stored in cataloged Content.</summary>
	public abstract class InventoryRow : IEntity
	{
		public string Id { get; set; } = Guid.NewGuid().ToString("D");
		public int DepartmentId { get; set; }
		public int Revision { get; set; } = 1;
		public DateTime CreatedOn { get; set; }
		public DateTime? ModifiedOn { get; set; }
		public string CreatedBy { get; set; }
		public string Content { get; set; }
		public bool IsProtected { get; set; }
		[NotMapped, JsonIgnore] public object IdValue { get => Id; set => Id = (string)value; }
		[NotMapped, JsonIgnore] public string TableName => InventoryTables.All[GetType()];
		[NotMapped, JsonIgnore] public string IdName => "Id";
		[NotMapped, JsonIgnore] public int IdType => 1;
		[NotMapped, JsonIgnore] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "TableName", "IdName", "IdType", "IgnoredProperties" };
	}
	public abstract class InventoryMutableRow : InventoryRow, IChangeTracked { public bool IsDeleted { get; set; } }
	/// <summary>
	/// A mixed table (ADP catalog review §8, option a): <c>Content</c> keeps plaintext metadata and <c>ProtectedContent</c>,
	/// the row's ADP slot, keeps the private members listed in <see cref="InventoryTables.PrivateMembers"/>.
	/// </summary>
	public abstract class InventorySplitRow : InventoryRow, ISplitContentRow
	{
		public string ProtectedContent { get; set; }
		[NotMapped, JsonIgnore] public IReadOnlyCollection<string> PrivateMemberPaths => InventoryTables.PrivatePaths(TableName);
		[NotMapped, JsonIgnore] public IReadOnlyCollection<string> PrivateTextMemberPaths => InventoryTables.PrivatePaths(TableName);
	}
	/// <inheritdoc cref="InventorySplitRow"/>
	public abstract class InventoryMutableSplitRow : InventoryMutableRow, ISplitContentRow
	{
		public string ProtectedContent { get; set; }
		[NotMapped, JsonIgnore] public IReadOnlyCollection<string> PrivateMemberPaths => InventoryTables.PrivatePaths(TableName);
		[NotMapped, JsonIgnore] public IReadOnlyCollection<string> PrivateTextMemberPaths => InventoryTables.PrivatePaths(TableName);
	}
	public sealed class InventoryCategory : InventoryMutableRow { public string ParentCategoryId { get; set; } }
	public sealed class InventoryItem : InventoryMutableRow
	{
		public string CategoryId { get; set; }
		public int TrackingMode { get; set; }
		public bool IsKit { get; set; }
		public bool RequiresLotTracking { get; set; }
		public bool RequiresExpiration { get; set; }
		public bool IsControlledSubstance { get; set; }
		public bool IsActive { get; set; } = true;
		public int? LegacyInventoryTypeId { get; set; }
	}
	public sealed class InventoryLocation : InventoryMutableRow
	{
		public int LocationType { get; set; }
		public int? GroupId { get; set; }
		public int? UnitId { get; set; }
		public string UserId { get; set; }
		public string ContainerAssetId { get; set; }
		public string ParentLocationId { get; set; }
		public bool IsDefault { get; set; }
	}
	public sealed class InventoryLot : InventoryMutableRow { public string ItemId { get; set; } public DateTime? ExpiresOn { get; set; } public DateTime ReceivedOn { get; set; } }
	public sealed class InventoryStock : InventoryMutableRow
	{
		public string ItemId { get; set; }
		public string LocationId { get; set; }
		public string LotId { get; set; }
		public decimal Quantity { get; set; }
	}
	public sealed class InventoryAsset : InventoryMutableRow
	{
		public string ItemId { get; set; }
		public string LotId { get; set; }
		public int Status { get; set; }
		public string CurrentLocationId { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public DateTime? AcquiredOn { get; set; }
	}
	/// <summary>EntryId is the bigint database primary key; Id is the stable GUID exposed to integration consumers. Append-only.</summary>
	public sealed class InventoryTransaction : InventorySplitRow
	{
		public string WorkOrderPartMovementId { get; set; }
		public long EntryId { get; set; }
		public string OperationId { get; set; }
		public int LineNumber { get; set; }
		public int TransactionType { get; set; }
		public string ItemId { get; set; }
		public string AssetId { get; set; }
		public string LotId { get; set; }
		public string FromLocationId { get; set; }
		public string ToLocationId { get; set; }
		public decimal Quantity { get; set; }
		public decimal? FromQuantityBefore { get; set; }
		public decimal? FromQuantityAfter { get; set; }
		public decimal? ToQuantityBefore { get; set; }
		public decimal? ToQuantityAfter { get; set; }
		public int? OldStatus { get; set; }
		public int? NewStatus { get; set; }
		public int ReferenceType { get; set; }
		public string ReferenceId { get; set; }
		public string ReversesTransactionId { get; set; }
		public string IssuanceId { get; set; }
		public string PurchaseOrderItemId { get; set; }
		public string CountItemId { get; set; }
		public string WorkOrderPartId { get; set; }
		public int? LegacyInventoryId { get; set; }
		public DateTime OccurredOn { get; set; }
	}
	public sealed class InventoryOperation : InventorySplitRow { public int State { get; set; } public string RequestId { get; set; } public string WitnessUserId { get; set; } }
	public sealed class InventoryTransfer : InventoryMutableRow { public string FromLocationId { get; set; } public string ToLocationId { get; set; } public int Status { get; set; } public string OperationId { get; set; } }
	public sealed class InventoryTransferItem : InventoryRow { public string TransferId { get; set; } public string TransactionId { get; set; } public string ItemId { get; set; } public string AssetId { get; set; } public string LotId { get; set; } public decimal Quantity { get; set; } }
	public sealed class InventoryIssuance : InventoryMutableSplitRow
	{
		public string ItemId { get; set; }
		public string AssetId { get; set; }
		public string LotId { get; set; }
		public decimal Quantity { get; set; }
		public decimal ReturnedQuantity { get; set; }
		public string IssuedToUserId { get; set; }
		public int? IssuedToUnitId { get; set; }
		public string LocationId { get; set; }
		public string ReturnedToLocationId { get; set; }
		public DateTime IssuedOn { get; set; }
		public DateTime? ExpectedReturnOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public int Status { get; set; }
		public int ReferenceType { get; set; }
		public string ReferenceId { get; set; }
	}
	public sealed class InventoryKit : InventoryMutableRow { }
	public sealed class InventoryKitItem : InventoryMutableRow { public string KitId { get; set; } public string ItemId { get; set; } public decimal Quantity { get; set; } }
	public sealed class InventoryItemContent
	{
		public string Name { get; set; }
		public string Description { get; set; }
		public string Code { get; set; }
		public string Barcode { get; set; }
		public string UnitOfMeasure { get; set; }
		public string DeaSchedule { get; set; }
		public int? DefaultExpirationDays { get; set; }
		public decimal? MinLevel { get; set; }
		public decimal? MaxLevel { get; set; }
		public decimal? ReorderPoint { get; set; }
		public decimal? ReorderQuantity { get; set; }
		public decimal? DefaultUnitCost { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public decimal? AverageUnitCost { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string CurrencyCode { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string PreferredVendorId { get; set; }
	}
	public sealed class InventoryLabel { public string Name { get; set; } public string Note { get; set; } }
	public sealed class InventoryLotContent { public string LotNumber { get; set; } public decimal? UnitCost { get; set; } public string VendorId { get; set; } }
	public sealed class InventoryAssetContent { public string SerialNumber { get; set; } public string AssetTag { get; set; } public string Barcode { get; set; } public decimal? AcquisitionCost { get; set; } public DateTime? WarrantyExpiresOn { get; set; } }
	public static class InventoryTables
	{
		public const int CatalogVersion = 19;
		public const int UsageCatalogVersion = 20;
		public const int PurchasingCatalogVersion = 21;
		public const int OperationsCatalogVersion = 22;
		public static readonly IReadOnlyDictionary<Type, string> All = new Dictionary<Type, string>
		{
			[typeof(InventoryCategory)] = "InventoryCategories", [typeof(InventoryItem)] = "InventoryItems", [typeof(InventoryLocation)] = "InventoryLocations",
			[typeof(InventoryLot)] = "InventoryLots", [typeof(InventoryStock)] = "InventoryStocks", [typeof(InventoryAsset)] = "InventoryAssets", [typeof(InventoryTransaction)] = "InventoryTransactions",
			[typeof(InventoryOperation)] = "InventoryOperations", [typeof(InventoryTransfer)] = "InventoryTransfers", [typeof(InventoryTransferItem)] = "InventoryTransferItems",
			[typeof(InventoryIssuance)] = "InventoryIssuances", [typeof(InventoryKit)] = "InventoryKits", [typeof(InventoryKitItem)] = "InventoryKitItems",
			[typeof(RecordInventoryUsage)] = "RecordInventoryUsages",
			[typeof(InventoryVendor)] = "InventoryVendors", [typeof(InventoryPurchaseOrder)] = "InventoryPurchaseOrders", [typeof(InventoryPurchaseOrderItem)] = "InventoryPurchaseOrderItems",
			[typeof(InventoryCount)] = "InventoryCounts", [typeof(InventoryCountItem)] = "InventoryCountItems",
			[typeof(InventoryAlert)] = "InventoryAlerts", [typeof(InventoryAlertDelivery)] = "InventoryAlertDeliveries"
		};
		/// <summary>
		/// Tables whose Content can hold free text about people (notes, attestations, account numbers) and stays ADP-protected.
		/// Every other table's Content is metadata — names of things, codes, levels, costs, serials, tags, lots — or is never
		/// written, and is not protected (2026-10-17: operational data is never in the ADP catalog).
		/// </summary>
		public static readonly IReadOnlyCollection<string> ProtectedContent = new HashSet<string>(StringComparer.Ordinal)
		{
			"InventoryTransactions", "InventoryOperations", "InventoryIssuances", "RecordInventoryUsages",
			"InventoryVendors", "InventoryPurchaseOrders", "InventoryPurchaseOrderItems", "InventoryCounts"
		};
		public static bool IsProtected(Type type) => ProtectedContent.Contains(All[type]);
		/// <summary>
		/// The private members of each mixed table's Content (ADP catalog review §8, option a, 2026-10-17): notes and
		/// attestations. They are stored, and sealed, in ProtectedContent; the rest of Content (item names, units, serials,
		/// costs, performer and witness ids, PO numbers, supplier names, count names and totals, pending commands and
		/// results) is plaintext metadata. Record usages and vendors stay wholly protected in Content. M0274 split the
		/// existing rows with a frozen copy of this map: change it only together with a migration.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string[]> PrivateMembers = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["InventoryTransactions"] = new[] { "Note", "Attestation", "LegacySource.Note" },
			["InventoryOperations"] = new[] { "Attestation", "PendingCommand.Lines[].Note", "PendingIssues[].Note", "PendingReturn.Note" },
			["InventoryIssuances"] = new[] { "Note" },
			["InventoryPurchaseOrders"] = new[] { "Note" },
			["InventoryPurchaseOrderItems"] = new[] { "Note" },
			["InventoryCounts"] = new[] { "Note" }
		};
		public static IReadOnlyCollection<string> PrivatePaths(string table) => table != null && PrivateMembers.TryGetValue(table, out var paths) ? paths : Array.Empty<string>();
		/// <summary>The column a protected table seals: ProtectedContent for a mixed table, Content for a wholly private one.</summary>
		public static string SealedColumn(string table) => PrivateMembers.ContainsKey(table) ? "ProtectedContent" : "Content";
		public static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Fields<T>() where T : InventoryRow
		{
			if (!IsProtected(typeof(T)))
				return new Dictionary<string, (Func<T, string>, Action<T, string>)>();
			var table = All[typeof(T)];
			return PrivateMembers.ContainsKey(table)
				? new Dictionary<string, (Func<T, string>, Action<T, string>)> { [table.ToLowerInvariant() + ".protectedcontent"] = (x => ((ISplitContentRow)x).ProtectedContent, (x, v) => ((ISplitContentRow)x).ProtectedContent = v) }
				: new Dictionary<string, (Func<T, string>, Action<T, string>)> { [table.ToLowerInvariant() + ".content"] = (x => x.Content, (x, v) => x.Content = v) };
		}
	}
}
