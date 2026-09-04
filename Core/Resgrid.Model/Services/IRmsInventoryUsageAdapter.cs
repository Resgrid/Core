using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
		public decimal Quantity { get; set; }
		public string Note { get; set; }
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
		Task<List<RmsInventoryUsage>> GetUsageForRecordAsync(int departmentId, string recordId);

		Task<List<RmsInventoryUsage>> GetUsageForLegacyLogAsync(int departmentId, int logId);

		Task<RmsInventoryUsage> RecordUsageAsync(int departmentId, string userId, string recordId, int inventoryId, decimal quantity, string note, CancellationToken cancellationToken = default);
	}
}
