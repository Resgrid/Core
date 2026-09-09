using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Model.Services
{
	public interface IWorkOrdersService
	{
		Task<WorkOrderPage> ListAsync(ChecklistActor actor, WorkOrderFilter filter);
		Task<WorkOrderDetail> GetAsync(ChecklistActor actor, int id);
		Task<WorkOrderDetail> CreateAsync(ChecklistActor actor, WorkOrderInput input);
		Task UpdateAsync(ChecklistActor actor, int id, WorkOrderInput input);
		Task TransitionAsync(ChecklistActor actor, int id, WorkOrderTransition input);
		Task AssignAsync(ChecklistActor actor, int id, WorkOrderAssignment input);
		Task AcceptAssignmentAsync(ChecklistActor actor, int id, int revision);
		Task CommentAsync(ChecklistActor actor, int id, int revision, string note);
		Task AddLaborAsync(ChecklistActor actor, int id, WorkOrderLaborInput input);
		Task AddPartAsync(ChecklistActor actor, int id, WorkOrderPartInput input);
		Task VoidPartAsync(ChecklistActor actor, int id, int partId, int revision, string reason);
		Task AddFileAsync(ChecklistActor actor, int id, int revision, string filename, string contentType, byte[] data);
		Task<WorkOrderFile> GetFileAsync(ChecklistActor actor, int id);
		Task WithdrawFileAsync(ChecklistActor actor, int id, int fileId, int revision, string reason);
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
		public bool Allows(WorkOrder row) => All || row.CreatedBy == UserId || row.AssignedToUserId == UserId || GroupId.HasValue && row.TargetGroupId == GroupId || row.AssignedToRoleId.HasValue && Array.IndexOf(RoleIds ?? Array.Empty<int>(), row.AssignedToRoleId.Value) >= 0;
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
		public int? DuplicateOfId { get; set; }
		public string Resolution { get; set; }
		public string Cause { get; set; }
		public string VerificationEvidence { get; set; }
		public bool ConfirmTasksComplete { get; set; }
	}
	public sealed class WorkOrderAssignment { public int Revision { get; set; } public string UserId { get; set; } public int? RoleId { get; set; } }
	public sealed class WorkOrderLaborInput { public int Revision { get; set; } public string UserId { get; set; } public DateTime WorkDate { get; set; } public WorkOrderLaborContent Content { get; set; } = new WorkOrderLaborContent(); }
	public sealed class WorkOrderPartInput { public int Revision { get; set; } public WorkOrderPartContent Content { get; set; } = new WorkOrderPartContent(); }
	public sealed class WorkOrderSummary
	{
		public WorkOrderStatus? QuickStatus { get; set; }
		public int Id { get; set; }
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
		public int? UnitId { get; set; }
		public int? GroupId { get; set; }
		public string AssetId { get; set; }
	}
	public sealed class WorkOrderPage { public List<WorkOrderSummary> Items { get; set; } = new List<WorkOrderSummary>(); public bool HasMore { get; set; } public bool CanWrite { get; set; } }
	public sealed class WorkOrderDetail
	{
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
		public List<WorkOrderActivityView> Activities { get; set; } = new List<WorkOrderActivityView>();
		public List<WorkOrderLaborView> Labor { get; set; } = new List<WorkOrderLaborView>();
		public List<WorkOrderPartView> Parts { get; set; } = new List<WorkOrderPartView>();
		public List<WorkOrderFileView> Files { get; set; } = new List<WorkOrderFileView>();
	}
	public sealed class WorkOrderActivityView { public WorkOrderContent Snapshot { get; set; } public string AssignedToUserId { get; set; } public int? AssignedToRoleId { get; set; } public int Id { get; set; } public WorkOrderActivityType Type { get; set; } public string UserId { get; set; } public DateTime CreatedOn { get; set; } public string Note { get; set; } public int? OldStatus { get; set; } public int? NewStatus { get; set; } }
	public sealed class WorkOrderLaborView { public int Id { get; set; } public string UserId { get; set; } public DateTime WorkDate { get; set; } public WorkOrderLaborContent Content { get; set; } }
	public sealed class WorkOrderPartView { public int Id { get; set; } public DateTime? VoidedOn { get; set; } public WorkOrderPartContent Content { get; set; } }
	public sealed class WorkOrderFileView { public int Id { get; set; } public string Name { get; set; } public string ContentType { get; set; } public int Size { get; set; } public DateTime? WithdrawnOn { get; set; } }
	public sealed class WorkOrderChoice { public string Id { get; set; } public string Name { get; set; } }
	public sealed class WorkOrderChoices
	{
		public List<WorkOrderChoice> Users { get; set; } = new List<WorkOrderChoice>();
		public List<WorkOrderChoice> Roles { get; set; } = new List<WorkOrderChoice>();
		public List<WorkOrderChoice> Units { get; set; } = new List<WorkOrderChoice>();
		public List<WorkOrderChoice> Groups { get; set; } = new List<WorkOrderChoice>();
		public List<WorkOrderChoice> Assets { get; set; } = new List<WorkOrderChoice>();
	}
}
