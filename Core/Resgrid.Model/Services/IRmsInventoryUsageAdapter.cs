using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Inventories;

namespace Resgrid.Model.Services
{
	/// <summary>One inventory item consumed against a Record (or, in principle, a legacy Log).</summary>
	public class RmsInventoryUsage
	{
		public const string SourceRecord = "Record";
		public const string SourceLegacyLog = "LegacyLog";

		/// <summary>RmsExternalReference ID for Record-sourced usage; null for legacy usage.</summary>
		public string ReferenceId { get; set; }
		public string Source { get; set; }
		public string RecordId { get; set; }
		public int? LegacyLogId { get; set; }
		public int InventoryId { get; set; }
		public string TransactionId { get; set; }
		public string ItemId { get; set; }
		public string UsageId { get; set; }
		public string AssetId { get; set; }
		public string LotId { get; set; }
		public string LocationId { get; set; }
		public InventoryUsageType UsageType { get; set; }
		public string ReversesUsageId { get; set; }
		public bool IsReversed { get; set; }
		public string PendingReversalTransactionId { get; set; }
		public decimal Quantity { get; set; }
		public string Note { get; set; }
		public string ItemName { get; set; }
		public string UnitOfMeasure { get; set; }
		public string SourceChecksum { get; set; }
		public string ReferenceChecksum { get; set; }
		public string CapturedByUserId { get; set; }
		public DateTime CapturedOn { get; set; }
	}

	/// <summary>
	/// Source-agnostic inventory-usage adapter (RMS plan RMS-1 package: read legacy Log usage, write new usage
	/// against RmsRecord/RmsRevision without a legacy row). Usage against a Record is an RmsExternalReference with
	/// the InventoryUsage semantic role, so no Records table changes when inventory is modernized. The legacy read
	/// exists so a caller never has to know which side a subject lives on; the current Logs schema carries no
	/// inventory linkage (Inventory rows have no LogId), so it answers empty today and a Logs-side source can
	/// plug in without changing callers.
	/// </summary>
	public interface IRmsInventoryUsageAdapter
	{
		Task<RmsInventoryUsage> ConsumeAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, long expectedRowVersion, int typeId, int groupId, int? unitId, decimal quantity, string note, CancellationToken cancellationToken = default, string grantToken = null);
		Task<RmsInventoryUsage> ConsumeModernAsync(InventoryActor actor, string recordId, RmsRecordKind kind, long expectedRowVersion, InventoryCommand command, CancellationToken cancellationToken = default);
		Task<List<RmsInventoryUsage>> RecordModernUsageAsync(InventoryActor actor, string recordId, RmsRecordKind kind, long expectedRowVersion, RecordInventoryUsageRequest request, CancellationToken cancellationToken = default);
		Task<RmsInventoryUsage> ReverseModernUsageAsync(InventoryActor actor, string recordId, RmsRecordKind kind, long expectedRowVersion, RecordInventoryUsageCorrection correction, CancellationToken cancellationToken = default);
		Task<List<RmsInventoryUsage>> GetAuthorizedUsageAsync(InventoryActor actor, string recordId, RmsRecordKind kind);
		/// <summary>Checks the live source and fences its version in Inventory's active transaction before a new linked stock correction.</summary>
		Task RequireUsageCorrectionAccessAsync(InventoryActor actor, string usageId);
		Task<List<RmsInventoryUsage>> GetUsageForRecordAsync(int departmentId, string recordId);

		Task<List<RmsInventoryUsage>> GetUsageForLegacyLogAsync(int departmentId, int logId);

		Task<RmsInventoryUsage> RecordUsageAsync(int departmentId, string userId, string recordId, int inventoryId, decimal quantity, string note, CancellationToken cancellationToken = default);
	}
}
