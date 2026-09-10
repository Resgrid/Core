using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Readiness (RMS-6, third bullet): apparatus and equipment readiness composed from the checklists, maintenance
	/// and inventory modules through their own authorized read seams, plus the readiness-packet evidence Records
	/// already capture. RMS computes nothing readiness-specific itself — every figure here is a count over what those
	/// modules returned to this viewer, so a member who cannot read work orders or an inventory location does not
	/// see them counted either. Each module section is present only while its module is enabled for the department
	/// and readable by the viewer; the flags say which.
	/// </summary>
	public class RecordsReadiness : RecordsAnalyticsBase
	{
		public bool ChecklistsEnabled { get; set; }
		public bool MaintenanceEnabled { get; set; }
		public bool InventoryEnabled { get; set; }

		/// <summary>The module is on but refused this viewer; the section is null and nothing was counted.</summary>
		public bool ChecklistsForbidden { get; set; }
		public bool MaintenanceForbidden { get; set; }
		public bool InventoryForbidden { get; set; }

		public RecordsChecklistMetrics Checklists { get; set; }
		public RecordsWorkOrderMetrics WorkOrders { get; set; }
		public RecordsEquipmentMetrics Equipment { get; set; }
		public RecordsReadinessEvidenceMetrics Evidence { get; set; } = new RecordsReadinessEvidenceMetrics();

		/// <summary>One row per unit the viewer may see, joining the module figures on the unit id.</summary>
		public List<RecordsUnitReadinessRow> Units { get; set; } = new List<RecordsUnitReadinessRow>();
	}

	/// <summary>Checklist compliance for one target (unit, station group, person, inventory asset).</summary>
	public class RecordsReadinessTargetRow
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public int Expected { get; set; }
		public int Completed { get; set; }
		public int OnTime { get; set; }
		public int Missed { get; set; }
		public int Skipped { get; set; }
		public double? CompletionRatePercent { get; set; }
		public double? OnTimePercent { get; set; }
	}

	public class RecordsChecklistTrendPoint
	{
		public DateTime Day { get; set; }
		public int Expected { get; set; }
		public int Missed { get; set; }
	}

	/// <summary>
	/// Checklist compliance from <c>IChecklistsService.GetComplianceSummaryAsync</c>. The checklists module accepts a
	/// cohort of at most 93 days, so the cohort is the tail of the dashboard window; <see cref="CohortStart"/> and
	/// <see cref="CohortEnd"/> say exactly what was measured.
	/// </summary>
	public class RecordsChecklistMetrics
	{
		public DateTime CohortStart { get; set; }
		public DateTime CohortEnd { get; set; }
		public int Expected { get; set; }
		public int Completed { get; set; }
		public int OnTime { get; set; }
		public int Missed { get; set; }
		public int Skipped { get; set; }
		public double? CompletionRatePercent { get; set; }
		public double? OnTimePercent { get; set; }

		/// <summary>True when the module withheld answer-level detail from this viewer; counts are still exact.</summary>
		public bool IsRedacted { get; set; }
		public List<string> UnavailableSources { get; set; } = new List<string>();
		public List<RecordsReadinessTargetRow> ByUnit { get; set; } = new List<RecordsReadinessTargetRow>();
		public List<RecordsReadinessTargetRow> ByStationGroup { get; set; } = new List<RecordsReadinessTargetRow>();
		public List<RecordsReadinessTargetRow> ByPerson { get; set; } = new List<RecordsReadinessTargetRow>();
		public List<RecordsReadinessTargetRow> ByAsset { get; set; } = new List<RecordsReadinessTargetRow>();
		public List<RecordsChecklistTrendPoint> Trend { get; set; } = new List<RecordsChecklistTrendPoint>();
	}

	public class RecordsWorkOrderCost
	{
		public string Currency { get; set; }
		public decimal Labor { get; set; }
		public decimal Parts { get; set; }
		public decimal Total => Labor + Parts;

		/// <summary>Lines whose cost the module could not price; the totals above exclude them.</summary>
		public int Unknown { get; set; }
	}

	/// <summary>Per-unit work-order load from the maintenance module's history feed.</summary>
	public class RecordsWorkOrderUnitRow
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public int Total { get; set; }
		public int Open { get; set; }
		public int Overdue { get; set; }
		public int OnHold { get; set; }
		public int Emergency { get; set; }
	}

	/// <summary>
	/// Maintenance from <c>IWorkOrderReportingService</c>: the module's own statistics over the window (created in
	/// window, open and overdue evaluated now, mean time to repair, priority and age bands, costs by currency) and
	/// a per-unit roll-up read from its paged history, bounded by <see cref="HistoryTruncated"/>.
	/// </summary>
	public class RecordsWorkOrderMetrics
	{
		public DateTime CohortStart { get; set; }
		public DateTime CohortEnd { get; set; }
		public int Total { get; set; }
		public int Open { get; set; }
		public int Overdue { get; set; }
		public int OnHold { get; set; }
		public double? MeanTimeToRepairHours { get; set; }
		public int RepairSamples { get; set; }
		public int MissingRepairTimes { get; set; }
		public List<RecordsAnalyticsCount> OpenByPriority { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> OpenByAge { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsWorkOrderCost> Costs { get; set; } = new List<RecordsWorkOrderCost>();
		public List<RecordsWorkOrderUnitRow> ByUnit { get; set; } = new List<RecordsWorkOrderUnitRow>();

		/// <summary>The per-unit roll-up stopped at the page cap; the module's own totals above are still complete.</summary>
		public bool HistoryTruncated { get; set; }
	}

	/// <summary>Assets and issuances per unit, from the inventory catalog the viewer may read.</summary>
	public class RecordsEquipmentUnitRow
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public int Assets { get; set; }
		public int Issued { get; set; }
		public int OutForRepair { get; set; }
		public int Damaged { get; set; }
		public int Expired { get; set; }
		public int OverdueReturns { get; set; }
	}

	/// <summary>
	/// Equipment from <c>IInventoryCatalogService</c>: serialized assets by status and expiry, outstanding and overdue
	/// issuances, and the per-unit view through the asset's location and the issuance's unit. Only rows the
	/// inventory module reveals to this viewer are counted.
	/// </summary>
	public class RecordsEquipmentMetrics
	{
		public int Assets { get; set; }
		public int InService { get; set; }
		public int Issued { get; set; }
		public int OutForRepair { get; set; }
		public int Damaged { get; set; }
		public int Expired { get; set; }
		public int ExpiringWithin30Days { get; set; }
		public int IssuancesOutstanding { get; set; }
		public int IssuancesOverdueReturn { get; set; }
		public List<RecordsAnalyticsCount> ByStatus { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsEquipmentUnitRow> ByUnit { get; set; } = new List<RecordsEquipmentUnitRow>();

		/// <summary>The catalog read stopped at the page cap.</summary>
		public bool Truncated { get; set; }
	}

	/// <summary>Readiness-packet evidence captured on Records in the window (plan section 4.5).</summary>
	public class RecordsReadinessEvidenceMetrics
	{
		/// <summary>Current (not superseded) readiness packets captured in the window.</summary>
		public int PacketsCaptured { get; set; }

		/// <summary>Finalized Records in the window that carry a readiness packet.</summary>
		public int RecordsWithPacket { get; set; }

		/// <summary>Finalized Records in the window linked to a call — the ones a packet could describe.</summary>
		public int CallLinkedRecords { get; set; }
		public double? CoveragePercent { get; set; }
		public List<RecordsAnalyticsCount> ByMonth { get; set; } = new List<RecordsAnalyticsCount>();
	}

	/// <summary>One unit's readiness at a glance: current state and the module figures joined on the unit id.</summary>
	public class RecordsUnitReadinessRow
	{
		public int UnitId { get; set; }
		public string Label { get; set; }
		public int? StationGroupId { get; set; }
		public string StationGroupLabel { get; set; }

		/// <summary>Latest unit state (UnitStateTypes / custom state value) and when it was set; null when none.</summary>
		public int? State { get; set; }
		public DateTime? StateOn { get; set; }
		public int ChecklistExpected { get; set; }
		public int ChecklistCompleted { get; set; }
		public int ChecklistMissed { get; set; }
		public double? ChecklistCompletionRatePercent { get; set; }
		public int OpenWorkOrders { get; set; }
		public int OverdueWorkOrders { get; set; }
		public int OnHoldWorkOrders { get; set; }
		public int AssetsAssigned { get; set; }
		public int AssetsOutForRepair { get; set; }
		public int AssetsExpired { get; set; }
		public int ReadinessPackets { get; set; }

		/// <summary>True when any figure on the row asks for attention: missed checks, overdue or on-hold orders, out-for-repair or expired assets.</summary>
		public bool NeedsAttention => ChecklistMissed > 0 || OverdueWorkOrders > 0 || OnHoldWorkOrders > 0 || AssetsOutForRepair > 0 || AssetsExpired > 0;
	}

	/// <summary>Header columns of an evidence artifact — never the manifest, the storage reference or the envelope.</summary>
	public class RmsEvidenceArtifactHeader
	{
		public string RmsEvidenceArtifactId { get; set; }
		public string RecordId { get; set; }
		public int RecordKind { get; set; }
		public string RevisionId { get; set; }
		public int Kind { get; set; }
		public int Classification { get; set; }
		public DateTime CapturedOn { get; set; }
		public DateTime? SupersededOn { get; set; }
	}
}
