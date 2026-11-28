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
        public string CurrentVersionId { get; set; }
        public int? TargetUnitId { get; set; }
        public int? TargetGroupId { get; set; }
        public string InventoryAssetId { get; set; }
        public string AssignedToUserId { get; set; }
        public int? AssignedToRoleId { get; set; }
        public string AssignedToUserIdsJson { get; set; }
        public string AssignedToRoleIdsJson { get; set; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped] public List<string> AssignedToUserIds { get => WorkOrderAssignees.Users(AssignedToUserIdsJson, AssignedToUserId); set => AssignedToUserIdsJson = value == null ? null : Newtonsoft.Json.JsonConvert.SerializeObject(value); }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped] public List<int> AssignedToRoleIds { get => WorkOrderAssignees.Roles(AssignedToRoleIdsJson, AssignedToRoleId); set => AssignedToRoleIdsJson = value == null ? null : Newtonsoft.Json.JsonConvert.SerializeObject(value); }
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
        public string PendingWorkOrderId { get; set; }
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
    public sealed class WorkOrderRecurrenceVersion : WorkOrderSplitRow { public string RecurrenceId { get; set; } }
    public sealed class WorkOrderMeterReading : WorkOrderSplitRow
    {
        public string RecurrenceId { get; set; }
        public string RequestId { get; set; }
        public int MeterEpoch { get; set; }
        public DateTime ObservedOn { get; set; }
    }
    public sealed class WorkOrderRecurrenceChange : WorkOrderSplitRow { public string RecurrenceId { get; set; } public int ChangeType { get; set; } public DateTime? OriginalDueOn { get; set; } public DateTime? RevisedDueOn { get; set; } }
    public sealed class WorkOrderRecurrenceInput
    {
        public string Id { get; set; }
        public int Revision { get; set; }
        public bool IsActive { get; set; } = true;
        public WorkOrderInput Template { get; set; } = new();
        public string AssignedToUserId { get; set; }
        public int? AssignedToRoleId { get; set; }
        public List<string> AssignedToUserIds { get; set; }
        public List<int> AssignedToRoleIds { get; set; }
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
    public sealed class WorkOrderMaintenanceSweep { public int Generated { get; set; } public int Held { get; set; } public int Escalated { get; set; } public int Deferred { get; set; } public int Errors { get; set; }
        /// <summary>Failure intents older than <see cref="Resgrid.Config.ReadinessProConfig.FailureIntentMaxAgeHours"/>, left for the review queue (RF-4).</summary>
        public int Stale { get; set; } }

    /// <summary>
    /// RF-4 / ERT-D5 (owner 2026-11-27): a checklist failure intent is only turned into a work order and holds by the unattended sweep
    /// while it is fresh. Older intents (typically recorded while the department had no Readiness Pro) wait in a review queue, where a
    /// manager creates a work order (never an automatic hold) or dismisses them. Zero hours turns expiry off (replay everything).
    /// </summary>
    public static class FailureIntentAging
    {
        public static DateTime? StaleBefore(DateTime nowUtc) => Resgrid.Config.ReadinessProConfig.FailureIntentMaxAgeHours > 0 ? nowUtc.AddHours(-Resgrid.Config.ReadinessProConfig.FailureIntentMaxAgeHours) : null;
        public static bool IsStale(WorkOrderFailureIntent intent, DateTime nowUtc) { var cutoff = StaleBefore(nowUtc); return intent != null && cutoff.HasValue && intent.CreatedOn < cutoff.Value; }
    }
    /// <summary>
    /// Industrial ERT G6 WE1 (Readiness Pro): the long-interval service an extinguisher or similar asset needs from a qualified technician,
    /// as a completion-based yearly preventive-maintenance schedule. Which presets apply to a given asset is the department's call (by
    /// type and manufacturer), never inferred. The monthly visual stays a checklist.
    /// </summary>
    public enum AssetServicePreset { AnnualMaintenance = 1, SixYearMaintenance = 2, HydrostaticFiveYear = 3, HydrostaticTwelveYear = 4 }
    public static class AssetServicePresets
    {
        /// <summary>The yearly interval of each preset.</summary>
        public static int Years(AssetServicePreset preset) => preset switch
        {
            AssetServicePreset.AnnualMaintenance => 1, AssetServicePreset.SixYearMaintenance => 6,
            AssetServicePreset.HydrostaticFiveYear => 5, AssetServicePreset.HydrostaticTwelveYear => 12, _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
        /// <summary>The localization key of the generated work order's title (WorkOrders resx).</summary>
        public static string TitleKey(AssetServicePreset preset) => "AssetServicePreset" + preset;
    }
    /// <summary>WE1: one preventive-maintenance schedule per preset for every serialized asset of an inventory item.</summary>
    public sealed class AssetServiceSetupInput
    {
        public string ItemId { get; set; }
        public List<AssetServicePreset> Presets { get; set; } = new();
        /// <summary>The first due date in the department's zone; later cycles count from each completion.</summary>
        public DateTime AnchorLocal { get; set; }
        public string TimeZoneId { get; set; } = "UTC";
        public int LeadDays { get; set; } = 30;
        public int? AssignedToRoleId { get; set; }
        public WorkOrderPriority Priority { get; set; } = WorkOrderPriority.Normal;
    }
    public sealed class AssetServiceSetupResult
    {
        public int Assets { get; set; }
        public int Created { get; set; }
        /// <summary>Already set up from an earlier run (same asset and preset).</summary>
        public int Existing { get; set; }
        /// <summary>Retired, lost or consumed assets left out.</summary>
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public bool Truncated { get; set; }
    }
    /// <summary>One stale failure intent in the activation review queue. Routing metadata only (no answers or findings).</summary>
    public sealed class WorkOrderFailureReviewRow
    {
        public string IntentId { get; set; }
        public DateTime RecordedOn { get; set; }
        public string CompletionId { get; set; }
        public string ItemId { get; set; }
        public int TargetType { get; set; }
        public string TargetId { get; set; }
        public int? TargetGroupId { get; set; }
        public int? UnitId { get; set; }
        public string UnitName { get; set; }
        public int Priority { get; set; }
        public bool HoldUnit { get; set; }
        public bool HoldAsset { get; set; }
        /// <summary>False when the source run is gone, reopened or on another version: the intent can only be dismissed.</summary>
        public bool CanCreate { get; set; }
    }
    public sealed class WorkOrderFailureReviewPage
    {
        public List<WorkOrderFailureReviewRow> Rows { get; set; } = new();
        public bool HasMore { get; set; }
        public int Total { get; set; }
        public int MaxAgeHours { get; set; }
    }
    public sealed class WorkOrderAssetState { public List<long> OutboxIds { get; set; } = new(); public int State { get; set; } public int Revision { get; set; } }
}

namespace Resgrid.Model.Services
{
    using Resgrid.Model.WorkOrders;
    public interface IWorkOrderMaintenanceService
    {
        Task<InventoryPage<WorkOrderChoice>> InventoryChoicesAsync(ChecklistActor actor, string kind, string itemId = null, int page = 0);
        Task<List<WorkOrderHoldView>> HoldsAsync(ChecklistActor actor, string orderId);
        Task CancelPartWitnessAsync(ChecklistActor actor, string orderId, string partId, int revision, string reason);
        Task AddHoldAsync(ChecklistActor actor, string orderId, WorkOrderHoldInput input);
        Task ReleaseHoldAsync(ChecklistActor actor, string holdId, WorkOrderReleaseInput input);
        Task<List<WorkOrderRecurrenceView>> RecurrencesAsync(ChecklistActor actor, int page = 0);
        Task<WorkOrderRecurrenceView> RecurrenceAsync(ChecklistActor actor, string id, int historyPage = 0);
        Task<string> SaveRecurrenceAsync(ChecklistActor actor, WorkOrderRecurrenceInput input);
        Task RecordReadingAsync(ChecklistActor actor, string id, WorkOrderReadingInput input);
        Task DeferAsync(ChecklistActor actor, string orderId, WorkOrderDeferralInput input);
        Task<WorkOrderMaintenanceSweep> GenerateMaintenanceAsync(int departmentId);
        Task<WorkOrderMaintenanceSweep> EscalateMaintenanceAsync(int departmentId);
        Task ValidateFailureOptionsAsync(ChecklistActor actor, ChecklistForm form);
        Task RecordFailureIntentAsync(ChecklistActor actor, ChecklistCompletion completion, ChecklistDefinitionVersion version, ChecklistItem item);
        /// <summary>RF-4: stale failure intents waiting for a manager (Readiness Pro and department-wide Manage Work Orders).</summary>
        Task<WorkOrderFailureReviewPage> FailureReviewAsync(ChecklistActor actor, int page = 0);
        /// <summary>RF-4: creates a work order for a stale intent (no hold) or dismisses it. Returns the work order id, or null when dismissed.</summary>
        Task<string> ResolveFailureReviewAsync(ChecklistActor actor, string intentId, bool createWorkOrder);
        /// <summary>G6 WE1: creates the chosen service schedules for every serialized asset of an item (idempotent per asset and preset).</summary>
        Task<AssetServiceSetupResult> SetupAssetServiceAsync(ChecklistActor actor, AssetServiceSetupInput input);
        Task CompleteInventoryPartAsync(InventoryActor actor, string partId, string transactionId, bool reversal, List<long> events);
    }
    /// <summary>Inventory owns its permissions, controlled witnesses, ledger and asset state. Caller owns the shared transaction.</summary>
    public interface IInventoryWorkOrderAdapter
    {
        Task<WorkOrderPartQuote> QuotePartAsync(InventoryActor actor, string orderId, WorkOrderPartInput input);
        Task<InventoryResult> PostPartAsync(InventoryActor actor, string partId, InventoryCommand command);
        Task CancelPendingPartAsync(InventoryActor actor, string partId, string operationId);
        Task<WorkOrderAssetState> ApplyHoldAsync(InventoryActor actor, string orderId, string assetId, int? restoreState = null, int? expectedRevision = null, bool safetyRelease = false);
    }
}
