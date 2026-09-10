using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Resgrid.Model.WorkOrders
{
	public enum WorkOrderStatus { Requested = 0, Accepted = 1, Assigned = 2, InProgress = 3, OnHold = 4, Completed = 5, Closed = 6, Rejected = 7, Duplicate = 8, Cancelled = 9 }
	public enum WorkOrderPriority { Low = 0, Normal = 1, High = 2, Emergency = 3 }
	public enum WorkOrderType { Corrective = 0, Preventive = 1, Inspection = 2, Facility = 3, Other = 4 }
	public enum WorkOrderActivityType { Created = 0, Updated = 1, StatusChanged = 2, Assigned = 3, AssignmentAccepted = 4, Comment = 5, LaborAdded = 6, PartAdded = 7, PartVoided = 8, FileAdded = 9, FileWithdrawn = 10, SafetyHold = 11, SafetyReleased = 12, Deferred = 13, Escalated = 14 }
	/// <summary>Free text, monetary details and file names live only in cataloged Content. Numeric identities are allocated with empty content before sealing.</summary>
	public abstract class WorkOrderRow : IEntity
	{
		[NotMapped, Newtonsoft.Json.JsonIgnore] public object IdValue { get => Id; set => Id = Convert.ToInt32(value); }
		[NotMapped, Newtonsoft.Json.JsonIgnore] public string TableName => WorkOrderTables.All[GetType()];
		[NotMapped, Newtonsoft.Json.JsonIgnore] public string IdName => "Id";
		[NotMapped, Newtonsoft.Json.JsonIgnore] public int IdType => 0;
		[NotMapped, Newtonsoft.Json.JsonIgnore] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "TableName", "IdName", "IdType", "IgnoredProperties" };
		public int Id { get; set; }
		public int DepartmentId { get; set; }
		public int? WorkOrderId { get; set; }
		public string Content { get; set; }
		public int Revision { get; set; } = 1;
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
		public string CreatedBy { get; set; }
		public bool IsProtected { get; set; }
	}
	public sealed class WorkOrder : WorkOrderRow
	{
		public DateTime? ResponseDueOn { get; set; }
		public DateTime? RepairDueOn { get; set; }
		public DateTime? ResponseOn { get; set; }
		public DateTime? ResponseBreachedOn { get; set; }
		public DateTime? RepairBreachedOn { get; set; }
		public int? SlaPolicyRevision { get; set; }
		public string RequestId { get; set; }
		public int NumberYear { get; set; }
		public int NumberSequence { get; set; }
		public int Type { get; set; }
		public int Priority { get; set; }
		public int Status { get; set; }
		public int SourceType { get; set; }
		public string SourceChecklistCompletionId { get; set; }
		public string SourceChecklistItemId { get; set; }
		public string SourceOccurrenceId { get; set; }
		public int? TargetUnitId { get; set; }
		public int? TargetGroupId { get; set; }
		public string InventoryAssetId { get; set; }
		public string AssignedToUserId { get; set; }
		public int? AssignedToRoleId { get; set; }
		public DateTime? DueOn { get; set; }
		public DateTime? TriagedOn { get; set; }
		public DateTime? AssignedOn { get; set; }
		public DateTime? AssignmentAcceptedOn { get; set; }
		public string AssignmentAcceptedBy { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string CompletedBy { get; set; }
		public DateTime? ClosedOn { get; set; }
		public string VerifiedBy { get; set; }
		public int? DuplicateOfId { get; set; }
		public bool SetUnitOutOfService { get; set; }
		public bool RestoreUnitStateOnClose { get; set; }
		public int? PreviousUnitStateType { get; set; }
		public string WorkOrderRecurrenceId { get; set; }
		public int? RecurrenceVersionId { get; set; }
		public long? RecurrenceCycle { get; set; }
		public DateTime? OriginalDueOn { get; set; }
		public DateTime? EscalatedOn { get; set; }
		public int EscalateAfterMinutes { get; set; }
		public int? EscalationRoleId { get; set; }
		public bool IsDeleted { get; set; }
	}
	public sealed class WorkOrderActivity : WorkOrderRow
	{
		public int ActivityType { get; set; }
		public int? OldStatus { get; set; }
		public int? NewStatus { get; set; }
	}
	public sealed class WorkOrderLabor : WorkOrderRow { public string UserId { get; set; } public DateTime WorkDate { get; set; } }
	public sealed class WorkOrderPart : WorkOrderRow
	{
		public bool Staged { get; set; }
		public string ReservedLocationId { get; set; }
		public string IssuedLocationId { get; set; }
		public string ReservedAssetId { get; set; }
		public string ReservedLotId { get; set; }
		public decimal ReservedQuantity { get; set; }
		public decimal IssuedQuantity { get; set; }
		public decimal ConsumedQuantity { get; set; }
		public decimal ReturnedQuantity { get; set; }
		public string InventoryItemId { get; set; }
		public string InventoryTransactionId { get; set; }
		public string InventoryOperationId { get; set; }
		public string InventoryReversalId { get; set; }
		public string InventoryRequestId { get; set; }
		public DateTime? VoidedOn { get; set; }
	}
	public sealed class WorkOrderFile : WorkOrderRow
	{
		public string ContentType { get; set; }
		public int Size { get; set; }
		public string Sha256 { get; set; }
		public byte[] Data { get; set; }
		public int ScanState { get; set; }
		public DateTime? WithdrawnOn { get; set; }
	}
	public sealed class WorkOrderContent
	{
		[Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)] public WorkOrderApproval Approval { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationText { get; set; }
		public string CostCenter { get; set; }
		public string Currency { get; set; } = "USD";
		public decimal? EstimatedCost { get; set; }
		public decimal? ApprovedCost { get; set; }
		public string VendorDetails { get; set; }
		public string WarrantyReference { get; set; }
		public string ProcedureReference { get; set; }
		public string ProcedureVersion { get; set; }
		public string PermitReference { get; set; }
		public string IsolationReference { get; set; }
		public string QualifiedPersonnel { get; set; }
		public bool SafetyCritical { get; set; }
		public bool HazardousWork { get; set; }
		public string Resolution { get; set; }
		public string Cause { get; set; }
		public string VerificationEvidence { get; set; }
		public List<WorkOrderTaskStep> Steps { get; set; } = new List<WorkOrderTaskStep>();
	}
	public sealed class WorkOrderTaskStep { public string Text { get; set; } public bool Completed { get; set; } }
	public sealed class WorkOrderLaborContent { [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)] public string Currency { get; set; } public decimal Hours { get; set; } public decimal? RatePerHour { get; set; } public string Note { get; set; } }
	public sealed class WorkOrderPartContent { [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)] public decimal? ConsumedCost { get; set; } [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)] public string RequestFingerprint { get; set; } public string Currency { get; set; } public string Description { get; set; } public decimal Quantity { get; set; } public decimal? UnitCost { get; set; } public string VoidReason { get; set; } }
	public static class WorkOrderTables
	{
		public const int CatalogVersion = 18;
		public const int IntegrationCatalogVersion = 23;
		public const int RecurrenceCatalogVersion = 24;
		public const int OperationsCatalogVersion = 25;
		public static int VersionFor(string table) => table is "WorkOrderPolicies" or "WorkOrderOperationReceipts" or "WorkOrderVendorCharges" or "WorkOrderPartMovements" ? OperationsCatalogVersion : table is "WorkOrderSafetyHolds" or "WorkOrderFailureIntents" ? IntegrationCatalogVersion : table is "WorkOrderRecurrences" or "WorkOrderRecurrenceVersions" or "WorkOrderMeterReadings" or "WorkOrderRecurrenceChanges" ? RecurrenceCatalogVersion : CatalogVersion;
		public static readonly IReadOnlyDictionary<Type, string> All = new Dictionary<Type, string>
		{
			[typeof(WorkOrderPolicy)] = "WorkOrderPolicies", [typeof(WorkOrderOperationReceipt)] = "WorkOrderOperationReceipts",
			[typeof(WorkOrderVendorCharge)] = "WorkOrderVendorCharges", [typeof(WorkOrderPartMovement)] = "WorkOrderPartMovements",
			[typeof(WorkOrder)] = "WorkOrders", [typeof(WorkOrderActivity)] = "WorkOrderActivities", [typeof(WorkOrderLabor)] = "WorkOrderLabors",
			[typeof(WorkOrderPart)] = "WorkOrderParts", [typeof(WorkOrderFile)] = "WorkOrderFiles",
			[typeof(WorkOrderFailureIntent)] = "WorkOrderFailureIntents", [typeof(WorkOrderSafetyHold)] = "WorkOrderSafetyHolds",
			[typeof(WorkOrderRecurrence)] = "WorkOrderRecurrences", [typeof(WorkOrderRecurrenceVersion)] = "WorkOrderRecurrenceVersions",
			[typeof(WorkOrderMeterReading)] = "WorkOrderMeterReadings", [typeof(WorkOrderRecurrenceChange)] = "WorkOrderRecurrenceChanges"
		};
		public static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Fields<T>() where T : WorkOrderRow =>
			new Dictionary<string, (Func<T, string>, Action<T, string>)> { [All[typeof(T)].ToLowerInvariant() + ".content"] = (x => x.Content, (x, v) => x.Content = v) };
	}
}
