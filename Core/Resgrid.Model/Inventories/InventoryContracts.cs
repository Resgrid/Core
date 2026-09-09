using System;
using System.Collections.Generic;

namespace Resgrid.Model.Inventories
{
	public sealed class InventoryActor { public int DepartmentId { get; set; } public string UserId { get; set; } public string GrantToken { get; set; } }
	public sealed class InventoryException : Exception { public int StatusCode { get; } public string Code { get; } public InventoryException(int status, string code) : base(code) { StatusCode = status; Code = code; } }
	public sealed class InventoryItemInput
	{
		public string Id { get; set; }
		public int Revision { get; set; }
		public string CategoryId { get; set; }
		public InventoryTrackingMode TrackingMode { get; set; }
		public bool IsKit { get; set; }
		public bool RequiresLotTracking { get; set; }
		public bool RequiresExpiration { get; set; }
		public bool IsControlledSubstance { get; set; }
		public bool IsActive { get; set; } = true;
		public InventoryItemContent Details { get; set; } = new();
	}
	public sealed class InventoryLocationInput { public string Id { get; set; } public int Revision { get; set; } public InventoryLocationType Type { get; set; } public int? GroupId { get; set; } public int? UnitId { get; set; } public string UserId { get; set; } public string ContainerAssetId { get; set; } public string ParentLocationId { get; set; } public bool IsDefault { get; set; } public string Name { get; set; } }
	public sealed class InventoryAssetInput { public string Id { get; set; } public string RequestId { get; set; } public string ItemId { get; set; } public string LocationId { get; set; } public string LotId { get; set; } public DateTime? ExpiresOn { get; set; } public InventoryAssetContent Details { get; set; } = new(); }
	public sealed class InventoryPosting
	{
		public string ItemId { get; set; }
		public string AssetId { get; set; }
		public string LotId { get; set; }
		public string FromLocationId { get; set; }
		public string ToLocationId { get; set; }
		public decimal Quantity { get; set; }
		public InventoryTransactionType Type { get; set; }
		public InventoryAssetStatus? Status { get; set; }
		public int? ExpectedAssetRevision { get; set; }
		public InventoryReferenceType ReferenceType { get; set; }
		public string ReferenceId { get; set; }
		public string ReversesTransactionId { get; set; }
		public string IssuanceId { get; set; }
		public string Note { get; set; }
		public decimal? UnitCost { get; set; }
	}
	public sealed class InventoryCommand { public string RequestId { get; set; } public List<InventoryPosting> Lines { get; set; } = new(); }
	public sealed class InventoryResult { public string OperationId { get; set; } public bool AwaitingWitness { get; set; } public List<string> TransactionIds { get; set; } = new(); public List<long> OutboxIds { get; set; } = new(); public string TransferId { get; set; } public string IssuanceId { get; set; } public string AssetId { get; set; } public List<string> IssuanceIds { get; set; } = new(); }
	public sealed class InventoryIssueInput { public string RequestId { get; set; } public string ItemId { get; set; } public string AssetId { get; set; } public string LotId { get; set; } public string FromLocationId { get; set; } public decimal Quantity { get; set; } public string UserId { get; set; } public int? UnitId { get; set; } public DateTime? ExpectedReturnOn { get; set; } public InventoryReferenceType ReferenceType { get; set; } public string ReferenceId { get; set; } public string Note { get; set; } }
	public sealed class InventoryReturnInput { public string RequestId { get; set; } public string IssuanceId { get; set; } public int Revision { get; set; } public string ToLocationId { get; set; } public decimal Quantity { get; set; } public InventoryAssetStatus Condition { get; set; } public string Note { get; set; } }
	public sealed class InventoryEquipment { public InventoryAsset Asset { get; set; } public InventoryStock Stock { get; set; } public string ItemName { get; set; } public int? UnitId { get; set; } public int? GroupId { get; set; } public string UserId { get; set; } public List<InventoryIssuance> Issuances { get; set; } = new(); }
	public sealed class InventoryPage<T> { public List<T> Items { get; set; } = new(); public bool HasMore { get; set; } }
	public sealed class InventoryOperationContent { public string Fingerprint { get; set; } public InventoryResult Result { get; set; } public InventoryCommand PendingCommand { get; set; } public string PendingKind { get; set; } public List<InventoryIssueInput> PendingIssues { get; set; } public InventoryReturnInput PendingReturn { get; set; } public string PerformerId { get; set; } public string WitnessId { get; set; } public DateTime? WitnessedOn { get; set; } public string Attestation { get; set; } }
	public sealed class InventoryKitInput { public string Id { get; set; } public int Revision { get; set; } public string Name { get; set; } public List<InventoryKitLine> Lines { get; set; } = new(); }
	public sealed class InventoryKitLine { public string ItemId { get; set; } public decimal Quantity { get; set; } }
	public sealed class InventoryKitIssueInput { public string KitId { get; set; } public string RequestId { get; set; } public List<InventoryIssueInput> Lines { get; set; } = new(); }
	public sealed class InventoryMigrationResult { public bool AlreadyMigrated { get; set; } public int Items { get; set; } public int Transactions { get; set; } public List<string> Warnings { get; set; } = new(); }
}
