using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;

namespace Resgrid.Model.WorkOrders
{
    public enum WorkOrderApprovalState { None = 0, Requested = 1, Approved = 2, Rejected = 3 }
    public enum WorkOrderPartMovementKind { Reserve = 0, Issue = 1, Consume = 2, ReturnUnused = 3, ReleaseReservation = 4 }
    public sealed class WorkOrderApproval
    {
        public WorkOrderApprovalState State { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string RequestedBy { get; set; }
        public DateTime RequestedOn { get; set; }
        public string DecidedBy { get; set; }
        public DateTime? DecidedOn { get; set; }
        public string Reason { get; set; }
    }
    public sealed class WorkOrderSpendingRule { public string Currency { get; set; } public decimal Threshold { get; set; } }
    public sealed class WorkOrderSlaTarget { public WorkOrderPriority Priority { get; set; } public int ResponseMinutes { get; set; } public int RepairMinutes { get; set; } }
    public sealed class WorkOrderBusinessCalendar
    {
        public string TimeZoneId { get; set; } = "UTC";
        public int Weekdays { get; set; } = 62;
        public int StartMinute { get; set; } = 540;
        public int EndMinute { get; set; } = 1020;
        public List<string> Holidays { get; set; } = new();
        public List<WorkOrderSlaTarget> Targets { get; set; } = new();
    }
    public sealed class WorkOrderPolicy : WorkOrderRow
    {
        // Equipment service clocks contain only validated time zones, dates and durations.
        public string CalendarJson { get; set; }
    }
    public sealed class WorkOrderPolicyInput
    {
        public int Revision { get; set; }
        public bool ApprovalsEnabled { get; set; }
        public List<WorkOrderSpendingRule> SpendingRules { get; set; } = new();
        public WorkOrderBusinessCalendar Calendar { get; set; } = new();
    }
    public sealed class WorkOrderApprovalInput { public int Revision { get; set; } public decimal Amount { get; set; } public bool Approve { get; set; } public string Reason { get; set; } }
    public sealed class WorkOrderOperationReceipt : WorkOrderRow { public string RequestId { get; set; } public int Kind { get; set; } }
    public sealed class WorkOrderBulkRow
    {
        public int RowNumber { get; set; }
        public string ParseError { get; set; }
        public int? WorkOrderId { get; set; }
        public WorkOrderInput Import { get; set; }
        public WorkOrderAssignment Assignment { get; set; }
    }
    public sealed class WorkOrderBulkInput { public string RequestId { get; set; } public string PreviewHash { get; set; } public List<WorkOrderBulkRow> Rows { get; set; } = new(); }
    public sealed class WorkOrderBulkRowResult { public int RowNumber { get; set; } public int? WorkOrderId { get; set; } public string Title { get; set; } public string ErrorCode { get; set; } public bool Applied { get; set; } }
    public sealed class WorkOrderBulkResult { public string PreviewHash { get; set; } public List<WorkOrderBulkRowResult> Rows { get; set; } = new(); }
    public sealed class WorkOrderVendorCharge : WorkOrderRow { public string RequestId { get; set; } public DateTime? VoidedOn { get; set; } }
    public sealed class WorkOrderVendorChargeContent
    {
        public string VendorId { get; set; }
        public string VendorName { get; set; }
        public string InvoiceReference { get; set; }
        public string Description { get; set; }
        public string Currency { get; set; }
        public decimal Amount { get; set; }
        public DateTime ServiceDate { get; set; }
        public string RequestHash { get; set; }
    }
    public sealed class WorkOrderVendorChargeInput { public int Revision { get; set; } public string RequestId { get; set; } public WorkOrderVendorChargeContent Content { get; set; } = new(); }
    public sealed class WorkOrderVendorChargeView { public int Id { get; set; } public DateTime? VoidedOn { get; set; } public WorkOrderVendorChargeContent Content { get; set; } }
    public sealed class WorkOrderPartMovement : WorkOrderRow
    {
        public int PartId { get; set; }
        public string RequestId { get; set; }
        public int Kind { get; set; }
        public decimal Quantity { get; set; }
        public string FromLocationId { get; set; }
        public string ToLocationId { get; set; }
        public string InventoryOperationId { get; set; }
        public string InventoryTransactionId { get; set; }
        public bool Cancelled { get; set; }
    }
    public sealed class WorkOrderPartMovementInput { public int Revision { get; set; } public string RequestId { get; set; } public WorkOrderPartMovementKind Kind { get; set; } public decimal Quantity { get; set; } public string LocationId { get; set; } public string Reason { get; set; } }
    public sealed class WorkOrderPartQuote { public decimal? UnitCost { get; set; } public string Currency { get; set; } }
}

namespace Resgrid.Model.Services
{
    using Resgrid.Model.WorkOrders;
    public interface IWorkOrderOperationsService
    {
        Task<WorkOrderPolicyInput> PolicyAsync(ChecklistActor actor);
        Task SavePolicyAsync(ChecklistActor actor, WorkOrderPolicyInput input);
        Task<WorkOrderBulkResult> PreviewBulkAsync(ChecklistActor actor, WorkOrderBulkInput input);
        Task<WorkOrderBulkResult> ApplyBulkAsync(ChecklistActor actor, WorkOrderBulkInput input);
        Task RequestApprovalAsync(ChecklistActor actor, int id, WorkOrderApprovalInput input);
        Task DecideApprovalAsync(ChecklistActor actor, int id, WorkOrderApprovalInput input);
        Task AddVendorChargeAsync(ChecklistActor actor, int id, WorkOrderVendorChargeInput input);
        Task VoidVendorChargeAsync(ChecklistActor actor, int id, int chargeId, int revision, string reason);
        Task<List<WorkOrderVendorChargeView>> VendorChargesAsync(ChecklistActor actor, int id, int afterId = 0);
        Task ReservePartAsync(ChecklistActor actor, int id, WorkOrderPartInput input);
        Task MovePartAsync(ChecklistActor actor, int id, int partId, WorkOrderPartMovementInput input);
        Task CancelPartMovementAsync(ChecklistActor actor, int id, int movementId, int revision, string reason);
        Task<List<WorkOrderPartMovement>> PartMovementsAsync(ChecklistActor actor, int id, int afterId = 0);
    }
}
