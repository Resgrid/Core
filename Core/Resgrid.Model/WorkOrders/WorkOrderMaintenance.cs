using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;

namespace Resgrid.Model.WorkOrders
{
    public enum MaintenanceCalendar { None = 0, Daily = 1, Weekly = 2, Monthly = 3, Quarterly = 4, Yearly = 5 }
    public enum MaintenanceMeterUnit { None = 0, Hours = 1, Kilometers = 2, Miles = 3, Cycles = 4 }
    public enum MaintenanceCondition { None = 0, AtOrAbove = 1, AtOrBelow = 2 }
    public enum MaintenanceChangeType { Configured = 0, Reading = 1, MeterReset = 2, Deferred = 3, Generated = 4, Overdue = 5 }

    /// <summary>Routing only. Never persist findings, answers or a grant in an unattended intent.</summary>
    public sealed class WorkOrderFailureIntent : WorkOrderRow
    {
        public string CompletionId { get; set; }
        public string ItemId { get; set; }
        public string VersionId { get; set; }
        public string OccurrenceId { get; set; }
        public string AuthorizedBy { get; set; }
        public int TargetType { get; set; }
        public string TargetId { get; set; }
        public int? TargetGroupId { get; set; }
        public int Priority { get; set; }
        public bool HoldUnit { get; set; }
        public bool HoldAsset { get; set; }
        public DateTime? ProcessedOn { get; set; }
    }
    public sealed class WorkOrderSafetyHold : WorkOrderRow
    {
        public int? UnitId { get; set; }
        public string AssetId { get; set; }
        public int? PreviousState { get; set; }
        public int? AppliedStateId { get; set; }
        public int? AppliedAssetRevision { get; set; }
        public DateTime? ReleasedOn { get; set; }
        public string ReleasedBy { get; set; }
        public bool StateRestored { get; set; }
    }
    public sealed class WorkOrderHoldContent { public string Reason { get; set; } public string ReleaseEvidence { get; set; } public string Qualification { get; set; } }
    public sealed class WorkOrderHoldInput { public int Revision { get; set; } public bool Unit { get; set; } public bool Asset { get; set; } public string Reason { get; set; } }
    public sealed class WorkOrderReleaseInput { public int Revision { get; set; } public string Evidence { get; set; } public string Qualification { get; set; } public bool RestoreState { get; set; } }
    public sealed class WorkOrderHoldView { public WorkOrderSafetyHold Hold { get; set; } public WorkOrderHoldContent Content { get; set; } }

    public sealed class WorkOrderRecurrence : WorkOrderRow
    {
        public string RequestId { get; set; }
        public int CurrentVersionId { get; set; }
        public int? TargetUnitId { get; set; }
        public int? TargetGroupId { get; set; }
        public string InventoryAssetId { get; set; }
        public string AssignedToUserId { get; set; }
        public int? AssignedToRoleId { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public int Calendar { get; set; }
        public int Interval { get; set; }
        public string TimeZoneId { get; set; }
        public DateTime AnchorLocal { get; set; }
        public DateTime? EndOn { get; set; }
        public int LeadDays { get; set; }
        public bool CompletionBased { get; set; }
        public int ServiceWeekdays { get; set; }
        public int ServiceStartMinute { get; set; }
        public int ServiceEndMinute { get; set; }
        public DateTime? BlackoutFrom { get; set; }
        public DateTime? BlackoutUntil { get; set; }
        public DateTime? NextDueOn { get; set; }
        public long Cycle { get; set; }
        public int? PendingWorkOrderId { get; set; }
        public bool ReadingDue { get; set; }
        public DateTime? ReadingDueOn { get; set; }
        public int MeterUnit { get; set; }
        public int MeterEpoch { get; set; }
        public decimal? MeterInterval { get; set; }
        public decimal? MeterBaseline { get; set; }
        public decimal? LastMeterValue { get; set; }
        public DateTime? LastReadingOn { get; set; }
        public int Condition { get; set; }
        public decimal? ConditionThreshold { get; set; }
        public bool ConditionLatched { get; set; }
        public int EscalateAfterMinutes { get; set; }
        public int? EscalationRoleId { get; set; }
    }
    /// <summary>Immutable protected instructions/configuration; generated orders retain this identity.</summary>
    public sealed class WorkOrderRecurrenceVersion : WorkOrderRow { public int RecurrenceId { get; set; } }
    public sealed class WorkOrderMeterReading : WorkOrderRow
    {
        public int RecurrenceId { get; set; }
        public string RequestId { get; set; }
        public int MeterEpoch { get; set; }
        public DateTime ObservedOn { get; set; }
    }
    public sealed class WorkOrderRecurrenceChange : WorkOrderRow { public int RecurrenceId { get; set; } public int ChangeType { get; set; } public DateTime? OriginalDueOn { get; set; } public DateTime? RevisedDueOn { get; set; } }
    public sealed class WorkOrderRecurrenceInput
    {
        public int Id { get; set; }
        public int Revision { get; set; }
        public bool IsActive { get; set; } = true;
        public WorkOrderInput Template { get; set; } = new();
        public string AssignedToUserId { get; set; }
        public int? AssignedToRoleId { get; set; }
        public MaintenanceCalendar Calendar { get; set; } = MaintenanceCalendar.Monthly;
        public int Interval { get; set; } = 1;
        public string TimeZoneId { get; set; } = "UTC";
        public DateTime AnchorLocal { get; set; }
        public DateTime? EndOn { get; set; }
        public int LeadDays { get; set; } = 3;
        public bool CompletionBased { get; set; }
        public int ServiceWeekdays { get; set; } = 127;
        public int ServiceStartMinute { get; set; }
        public int ServiceEndMinute { get; set; } = 1439;
        public DateTime? BlackoutFrom { get; set; }
        public DateTime? BlackoutUntil { get; set; }
        public MaintenanceMeterUnit MeterUnit { get; set; }
        public decimal? MeterInterval { get; set; }
        public decimal? MeterBaseline { get; set; }
        public MaintenanceCondition Condition { get; set; }
        public decimal? ConditionThreshold { get; set; }
        public string ConditionUnit { get; set; }
        public int EscalateAfterMinutes { get; set; }
        public int? EscalationRoleId { get; set; }
        public string Reason { get; set; }
    }
    public sealed class WorkOrderReadingInput { public int Revision { get; set; } public string RequestId { get; set; } public DateTime ObservedOn { get; set; } public decimal? MeterValue { get; set; } public decimal? ConditionValue { get; set; } public bool ResetMeter { get; set; } public string Source { get; set; } public string Note { get; set; } }
    public sealed class WorkOrderDeferralInput { public int Revision { get; set; } public DateTime DueOn { get; set; } public string Reason { get; set; } }
    public sealed class WorkOrderRecurrenceView { public int HistoryPage { get; set; } public bool HasMoreHistory { get; set; } public WorkOrderRecurrence Schedule { get; set; } public WorkOrderRecurrenceInput Settings { get; set; } public List<WorkOrderRecurrenceChange> Changes { get; set; } = new(); public List<WorkOrderMeterReading> Readings { get; set; } = new(); }
    public sealed class WorkOrderMaintenanceSweep { public int Generated { get; set; } public int Held { get; set; } public int Escalated { get; set; } public int Deferred { get; set; } public int Errors { get; set; } }
    public sealed class WorkOrderAssetState { public List<long> OutboxIds { get; set; } = new(); public int State { get; set; } public int Revision { get; set; } }
}

namespace Resgrid.Model.Services
{
    using Resgrid.Model.WorkOrders;
    public interface IWorkOrderMaintenanceService
    {
        Task<InventoryPage<WorkOrderChoice>> InventoryChoicesAsync(ChecklistActor actor, string kind, string itemId = null, int page = 0);
        Task<List<WorkOrderHoldView>> HoldsAsync(ChecklistActor actor, int orderId);
        Task CancelPartWitnessAsync(ChecklistActor actor, int orderId, int partId, int revision, string reason);
        Task AddHoldAsync(ChecklistActor actor, int orderId, WorkOrderHoldInput input);
        Task ReleaseHoldAsync(ChecklistActor actor, int holdId, WorkOrderReleaseInput input);
        Task<List<WorkOrderRecurrenceView>> RecurrencesAsync(ChecklistActor actor, int page = 0);
        Task<WorkOrderRecurrenceView> RecurrenceAsync(ChecklistActor actor, int id, int historyPage = 0);
        Task<int> SaveRecurrenceAsync(ChecklistActor actor, WorkOrderRecurrenceInput input);
        Task RecordReadingAsync(ChecklistActor actor, int id, WorkOrderReadingInput input);
        Task DeferAsync(ChecklistActor actor, int orderId, WorkOrderDeferralInput input);
        Task<WorkOrderMaintenanceSweep> GenerateMaintenanceAsync(int departmentId);
        Task<WorkOrderMaintenanceSweep> EscalateMaintenanceAsync(int departmentId);
        Task ValidateFailureOptionsAsync(ChecklistActor actor, ChecklistForm form);
        Task RecordFailureIntentAsync(ChecklistActor actor, ChecklistCompletion completion, ChecklistDefinitionVersion version, ChecklistItem item);
        Task CompleteInventoryPartAsync(InventoryActor actor, int partId, string transactionId, bool reversal, List<long> events);
    }
    /// <summary>Inventory owns its permissions, controlled witnesses, ledger and asset state. Caller owns the shared transaction.</summary>
    public interface IInventoryWorkOrderAdapter
    {
        Task<InventoryResult> PostPartAsync(InventoryActor actor, int partId, InventoryCommand command);
        Task CancelPendingPartAsync(InventoryActor actor, int partId, string operationId);
        Task<WorkOrderAssetState> ApplyHoldAsync(InventoryActor actor, int orderId, string assetId, int? restoreState = null, int? expectedRevision = null, bool safetyRelease = false);
    }
}
