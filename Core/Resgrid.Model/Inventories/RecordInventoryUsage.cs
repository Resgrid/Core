using System.Collections.Generic;

namespace Resgrid.Model.Inventories
{
	public enum InventoryUsageSourceType { LegacyLog = 0, RmsRecord = 1 }
	public enum InventoryUsageType { Used = 0, ConsumedOnPatient = 1, LeftAtScene = 2, Damaged = 3 }

	/// <summary>Immutable source-owned usage. Corrections are separate rows and ledger entries; Records freezes references in its revision evidence.</summary>
	public sealed class RecordInventoryUsage : InventoryRow
	{
		public int SourceType { get; set; }
		public string SourceId { get; set; }
		public int? RecordKind { get; set; }
		public int? CallId { get; set; }
		/// <summary>The revision being amended when posted; null for an initial draft. Final revision membership is frozen by Records evidence.</summary>
		public string RmsRevisionId { get; set; }
		public string ItemId { get; set; }
		public string AssetId { get; set; }
		public string LotId { get; set; }
		public string SourceLocationId { get; set; }
		public decimal Quantity { get; set; }
		public int UsageType { get; set; }
		public string TransactionId { get; set; }
		public string ReversesUsageId { get; set; }
	}

	public sealed class RecordInventoryUsageLine
	{
		public string ItemId { get; set; }
		public string AssetId { get; set; }
		public string LotId { get; set; }
		public string LocationId { get; set; }
		public decimal Quantity { get; set; }
		public int? ExpectedAssetRevision { get; set; }
		public InventoryUsageType UsageType { get; set; }
		public string Note { get; set; }
		/// <summary>Attach an already witnessed, unclaimed Inventory consumption without posting it again.</summary>
		public string ExistingTransactionId { get; set; }
	}
	public sealed class RecordInventoryUsageRequest
	{
		public string RequestId { get; set; }
		public List<RecordInventoryUsageLine> Lines { get; set; } = new();
	}
	public sealed class RecordInventoryUsageCorrection
	{
		public string RequestId { get; set; }
		public string UsageId { get; set; }
		public string Reason { get; set; }
		/// <summary>For controlled stock, attach the independently witnessed reversal from Inventory.</summary>
		public string ExistingTransactionId { get; set; }
	}
}
