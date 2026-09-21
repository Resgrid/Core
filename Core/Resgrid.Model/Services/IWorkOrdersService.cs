using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Model.Services
{
	public interface IWorkOrdersService
	{
		Task<WorkOrderPage> ListAsync(ChecklistActor actor, WorkOrderFilter filter);
		Task<WorkOrderDetail> GetAsync(ChecklistActor actor, string id);
		Task<WorkOrderDetail> CreateAsync(ChecklistActor actor, WorkOrderInput input);
		Task UpdateAsync(ChecklistActor actor, string id, WorkOrderInput input);
		Task TransitionAsync(ChecklistActor actor, string id, WorkOrderTransition input);
		Task AssignAsync(ChecklistActor actor, string id, WorkOrderAssignment input);
		Task AcceptAssignmentAsync(ChecklistActor actor, string id, int revision);
		Task CommentAsync(ChecklistActor actor, string id, int revision, string note);
		Task AddLaborAsync(ChecklistActor actor, string id, WorkOrderLaborInput input);
		Task AddPartAsync(ChecklistActor actor, string id, WorkOrderPartInput input);
		Task VoidPartAsync(ChecklistActor actor, string id, string partId, int revision, string reason);
		Task AddFileAsync(ChecklistActor actor, string id, int revision, string filename, string contentType, byte[] data);
		Task<WorkOrderFile> GetFileAsync(ChecklistActor actor, string id);
		Task WithdrawFileAsync(ChecklistActor actor, string id, string fileId, int revision, string reason);
		Task<WorkOrderChoices> ChoicesAsync(ChecklistActor actor);
	}
	public interface IWorkOrderAuthorizationService
	{
		Task RequireMemberAsync(ChecklistActor actor);
		Task<WorkOrderReadScope> ScopeAsync(ChecklistActor actor);
		Task<bool> CanManageAsync(ChecklistActor actor, int? groupId);
		Task<bool> CanContributeAsync(ChecklistActor actor, WorkOrder row);
		Task ValidateTargetAsync(ChecklistActor actor, WorkOrderInput input);
		Task ValidateAssignmentAsync(ChecklistActor actor, WorkOrder row, string userId, int? roleId);
		Task<WorkOrderChoices> ChoicesAsync(ChecklistActor actor);
		Task<List<string>> RecipientsAsync(int departmentId, WorkOrder row);
	}
}

namespace Resgrid.Model.WorkOrders
{
	public sealed class WorkOrderException : Exception
	{
		public int StatusCode { get; }
		public string Code { get; }
		public WorkOrderException(int status, string code) : base(code) { StatusCode = status; Code = code; }
	}
	public sealed class WorkOrderFilter
	{
        public string ChecklistCompletionId { get; set; }
		public int Page { get; set; }
		public WorkOrderStatus? Status { get; set; }
		public WorkOrderPriority? Priority { get; set; }
		public bool AssignedToMe { get; set; }
		public int? UnitId { get; set; }
		public int? GroupId { get; set; }
		public string AssetId { get; set; }
	}
	public sealed class WorkOrderReadScope
	{
		public string UserId { get; set; }
		public bool All { get; set; }
		public int? GroupId { get; set; }
		public int[] RoleIds { get; set; } = Array.Empty<int>();
		public bool Allows(WorkOrder row) => All || row.CreatedBy == UserId || row.AssignedToUserIds.Contains(UserId) || GroupId.HasValue && row.TargetGroupId == GroupId || row.AssignedToRoleIds.Intersect(RoleIds ?? Array.Empty<int>()).Any();
	}
	public sealed class WorkOrderInput
	{
		public string RequestId { get; set; }
		public int Revision { get; set; }
		public WorkOrderType Type { get; set; }
		public WorkOrderPriority Priority { get; set; } = WorkOrderPriority.Normal;
		public int? TargetUnitId { get; set; }
		public int? TargetGroupId { get; set; }
		public string InventoryAssetId { get; set; }
		public DateTime? DueOn { get; set; }
		public WorkOrderContent Content { get; set; } = new WorkOrderContent();
	}
	public sealed class WorkOrderTransition
	{
		public int Revision { get; set; }
		public WorkOrderStatus Status { get; set; }
		public string Reason { get; set; }
		public string DuplicateOfId { get; set; }
		public string Resolution { get; set; }
		public string Cause { get; set; }
		public string VerificationEvidence { get; set; }
		public bool ConfirmTasksComplete { get; set; }
	}
	public sealed class WorkOrderAssignment { public int Revision { get; set; } public string UserId { get; set; } public int? RoleId { get; set; } }
	public sealed class WorkOrderLaborInput { public int Revision { get; set; } public string UserId { get; set; } public DateTime WorkDate { get; set; } public WorkOrderLaborContent Content { get; set; } = new WorkOrderLaborContent(); }
	public sealed class WorkOrderPartInput { public string RequestId { get; set; } public string InventoryItemId { get; set; } public string InventoryAssetId { get; set; } public string InventoryLotId { get; set; } public string InventoryLocationId { get; set; } public int Revision { get; set; } public WorkOrderPartContent Content { get; set; } = new WorkOrderPartContent(); }
	public sealed class WorkOrderSummary
	{
		public WorkOrderStatus? QuickStatus { get; set; }
		public string Id { get; set; }
		public string Number { get; set; }
		public string Title { get; set; }
		public WorkOrderStatus Status { get; set; }
		public WorkOrderPriority Priority { get; set; }
		public int Revision { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public string AssignedToUserId { get; set; }
		public int? AssignedToRoleId { get; set; }
		public List<string> AssignedToUserIds { get; set; } = new();
		public List<int> AssignedToRoleIds { get; set; } = new();
		public int? UnitId { get; set; }
		public int? GroupId { get; set; }
		public string AssetId { get; set; }
	}
	public sealed class WorkOrderPage { public List<WorkOrderSummary> Items { get; set; } = new List<WorkOrderSummary>(); public bool HasMore { get; set; } public bool CanWrite { get; set; } }
	public sealed class WorkOrderDetail
	{
		public DateTime? ResponseDueOn { get; set; }
		public DateTime? RepairDueOn { get; set; }
		public DateTime? ResponseOn { get; set; }
		public WorkOrderSummary Order { get; set; }
		public WorkOrderInput Input { get; set; }
		public bool CanWrite { get; set; }
		public bool CanManage { get; set; }
		public bool CanEdit { get; set; }
		public bool CanContribute { get; set; }
		public bool CanAccept { get; set; }
		public WorkOrderStatus[] Transitions { get; set; } = Array.Empty<WorkOrderStatus>();
		public string ReportedBy { get; set; }
		public string VerifiedBy { get; set; }
		public DateTime? CompletedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public string SourceChecklistCompletionId { get; set; }
		public string SourceChecklistItemId { get; set; }
		public string RecurrenceId { get; set; }
		public DateTime? OriginalDueOn { get; set; }
		public DateTime? EscalatedOn { get; set; }
		public List<WorkOrderActivityView> Activities { get; set; } = new List<WorkOrderActivityView>();
		public List<WorkOrderLaborView> Labor { get; set; } = new List<WorkOrderLaborView>();
		public List<WorkOrderPartView> Parts { get; set; } = new List<WorkOrderPartView>();
		public List<WorkOrderFileView> Files { get; set; } = new List<WorkOrderFileView>();
	}
	public sealed class WorkOrderActivityView { public DateTime? OriginalDueOn { get; set; } public DateTime? RevisedDueOn { get; set; } public WorkOrderContent Snapshot { get; set; } public string AssignedToUserId { get; set; } public int? AssignedToRoleId { get; set; } public string Id { get; set; } public WorkOrderActivityType Type { get; set; } public string UserId { get; set; } public DateTime CreatedOn { get; set; } public string Note { get; set; } public int? OldStatus { get; set; } public int? NewStatus { get; set; } }
	public sealed class WorkOrderLaborView { public string Id { get; set; } public string UserId { get; set; } public DateTime WorkDate { get; set; } public WorkOrderLaborContent Content { get; set; } }
	public sealed class WorkOrderPartView { public bool Staged { get; set; } public decimal ReservedQuantity { get; set; } public decimal IssuedQuantity { get; set; } public decimal ConsumedQuantity { get; set; } public decimal ReturnedQuantity { get; set; } public string InventoryWitnessRequestId { get; set; } public string InventoryItemId { get; set; } public string InventoryTransactionId { get; set; } public string InventoryOperationId { get; set; } public bool AwaitingWitness { get; set; } public string Id { get; set; } public DateTime? VoidedOn { get; set; } public WorkOrderPartContent Content { get; set; } }
	public sealed class WorkOrderFileView { public string Id { get; set; } public string Name { get; set; } public string ContentType { get; set; } public int Size { get; set; } public DateTime? WithdrawnOn { get; set; } }
	public sealed class WorkOrderChoice { public string Id { get; set; } public string Name { get; set; } }
	public sealed class WorkOrderChoices
	{
		public string Currency { get; set; } = "USD";
		public List<WorkOrderChoice> Users { get; set; } = new List<WorkOrderChoice>();
		public List<WorkOrderChoice> Roles { get; set; } = new List<WorkOrderChoice>();
		public List<WorkOrderChoice> Units { get; set; } = new List<WorkOrderChoice>();
		public List<WorkOrderChoice> Groups { get; set; } = new List<WorkOrderChoice>();
		public List<WorkOrderChoice> Assets { get; set; } = new List<WorkOrderChoice>();
	}
}
