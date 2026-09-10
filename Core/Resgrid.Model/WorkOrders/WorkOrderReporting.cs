using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.WorkOrders
{
    public sealed class WorkOrderReportQuery
    {
        [Newtonsoft.Json.JsonIgnore] public bool PreventiveDueCohort { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? UntilUtc { get; set; }
        public WorkOrderStatus? Status { get; set; }
        public WorkOrderPriority? Priority { get; set; }
        public int? UnitId { get; set; }
        public int? GroupId { get; set; }
        public string AssetId { get; set; }
        public int AfterId { get; set; }
    }
    public sealed class WorkOrderCostTotal
    {
        public string Currency { get; set; }
        public decimal Labor { get; set; }
        public decimal Parts { get; set; }
        public decimal Vendor { get; set; }
        public decimal Total => Labor + Parts + Vendor;
        public int UnknownLabor { get; set; }
        public int UnknownParts { get; set; }
    }
    public sealed class WorkOrderReportEntry
    {
        public WorkOrderSummary Order { get; set; }
        public DateTime? StartedOn { get; set; }
        public DateTime? CompletedOn { get; set; }
        public DateTime? ClosedOn { get; set; }
        public decimal? RepairHours { get; set; }
        public decimal? ActiveRepairHours { get; set; }
        public decimal? WaitingHours { get; set; }
        public decimal DowntimeHours { get; set; }
        public DateTime? ResponseDueOn { get; set; }
        public DateTime? RepairDueOn { get; set; }
        public DateTime? ResponseOn { get; set; }
        public List<WorkOrderCostTotal> Costs { get; set; } = new();
    }
    public sealed class WorkOrderHistoryPage
    {
        public List<WorkOrderReportEntry> Items { get; set; } = new();
        public int? NextAfterId { get; set; }
    }
    public sealed class WorkOrderEvidencePage<T>
    {
        public List<T> Items { get; set; } = new();
        public int? NextAfterId { get; set; }
    }
    public sealed class WorkOrderStats
    {
        public DateTime FromUtc { get; set; }
        public DateTime UntilUtc { get; set; }
        public DateTime AsOfUtc { get; set; }
        public int Total { get; set; }
        public int Open { get; set; }
        public int Overdue { get; set; }
        public int RepairSamples { get; set; }
        public int MissingRepairTimes { get; set; }
        public decimal? MeanTimeToRepairHours { get; set; }
        public decimal ActiveRepairHours { get; set; }
        public decimal WaitingHours { get; set; }
        public decimal DowntimeHours { get; set; }
        public int MissingTimeHistory { get; set; }
        public int PreventiveDue { get; set; }
        public int PreventiveOnTime { get; set; }
        public decimal? PreventiveCompliancePercent => PreventiveDue == 0 ? null : 100m * PreventiveOnTime / PreventiveDue;
        public int RepeatedFailures { get; set; }
        public int ResponseSlaBreaches { get; set; }
        public int RepairSlaBreaches { get; set; }
        public int[] OpenByPriority { get; set; } = new int[4];
        // 0-6, 7-29, 30-89, 90+ elapsed days since creation, evaluated at AsOfUtc.
        public int[] OpenByAge { get; set; } = new int[4];
        public List<WorkOrderCostTotal> Costs { get; set; } = new();
    }
    /// <summary>Immutable operational metadata only. Sensitive fields are resolved from the
    /// referenced immutable activity/template through ADP; ciphertext is never copied between rows.</summary>
    public sealed class WorkOrderReportSnapshot
    {
        public long Id { get; set; }
        public int DepartmentId { get; set; }
        public int WorkOrderId { get; set; }
        public int Revision { get; set; }
        public DateTime? ResponseDueOn { get; set; }
        public DateTime? RepairDueOn { get; set; }
        public DateTime? ResponseOn { get; set; }
        public DateTime? ResponseBreachedOn { get; set; }
        public DateTime? RepairBreachedOn { get; set; }
        public int? SlaPolicyRevision { get; set; }
        public DateTime RecordedOn { get; set; }
        public int? SourceActivityId { get; set; }
        public int? RecurrenceVersionId { get; set; }
        public int SourceType { get; set; }
        public int Status { get; set; }
        public int Priority { get; set; }
        public int? TargetUnitId { get; set; }
        public int? TargetGroupId { get; set; }
        public string InventoryAssetId { get; set; }
        public DateTime? DueOn { get; set; }
        public DateTime? StartedOn { get; set; }
        public DateTime? CompletedOn { get; set; }
        public DateTime? ClosedOn { get; set; }
    }
    public sealed class ReadinessWorkOrderEvidence
    {
        public int WorkOrderId { get; set; }
        public int Revision { get; set; }
        public long? SnapshotId { get; set; }
        public int? SourceActivityId { get; set; }
        public int? RecurrenceVersionId { get; set; }
        public DateTime RecordedOn { get; set; }
        public string Title { get; set; }
        public int Status { get; set; }
        public int Priority { get; set; }
        public int? UnitId { get; set; }
        public string AssetId { get; set; }
        public DateTime? DueOn { get; set; }
        public string ChecklistCompletionId { get; set; }
        public string ChecklistItemId { get; set; }
        public List<int> ActiveSafetyHoldIds { get; set; } = new();
    }
    public sealed class ReadinessWorkOrderSection
    {
        public List<ReadinessWorkOrderEvidence> Items { get; set; } = new();
        public bool HistoryUnavailable { get; set; }
        public bool RestrictedScope { get; set; }
    }
}
namespace Resgrid.Model.Services
{
    public interface IWorkOrderReportingService
    {
        Task<WorkOrders.WorkOrderEvidencePage<WorkOrders.WorkOrderActivityView>> GetWorkOrderActivityAsync(ChecklistActor actor, int id, int afterId = 0);
        Task<WorkOrders.WorkOrderEvidencePage<WorkOrders.WorkOrderHoldView>> GetWorkOrderHoldsAsync(ChecklistActor actor, int id, int afterId = 0);
        Task<WorkOrders.WorkOrderStats> GetWorkOrderStatsAsync(ChecklistActor actor, WorkOrders.WorkOrderReportQuery query);
        Task<WorkOrders.WorkOrderHistoryPage> GetWorkOrderHistoryAsync(ChecklistActor actor, WorkOrders.WorkOrderReportQuery query);
        Task<byte[]> ExportWorkOrdersAsync(ChecklistActor actor, WorkOrders.WorkOrderReportQuery query);
        Task<WorkOrders.ReadinessWorkOrderSection> ReadinessEvidenceAsync(ChecklistActor actor, DateTime fromUtc, DateTime callUtc, int[] unitIds, string[] assetIds);
    }
}
