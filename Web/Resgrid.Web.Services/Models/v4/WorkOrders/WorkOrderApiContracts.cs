using Resgrid.Model.WorkOrders;

namespace Resgrid.Web.Services.Models.v4.WorkOrders
{
	public sealed class WorkOrderApiResult<T> : StandardApiResponseV4Base { public T Data { get; set; } public bool HasMore { get; set; } public int ContractVersion { get; set; } = 1; }
	public sealed class WorkOrderCommandInput { public string Id { get; set; } public int Revision { get; set; } public string ChildId { get; set; } public string Note { get; set; } }
	public sealed class WorkOrderUpdateInput { public string Id { get; set; } public WorkOrderInput Input { get; set; } }
	public sealed class WorkOrderStatusInput { public string Id { get; set; } public WorkOrderTransition Input { get; set; } }
	public sealed class WorkOrderAssignInput { public string Id { get; set; } public WorkOrderAssignment Input { get; set; } }
	public sealed class WorkOrderApiLaborInput { public string Id { get; set; } public WorkOrderLaborInput Input { get; set; } }
	public sealed class WorkOrderApiPartInput { public string Id { get; set; } public WorkOrderPartInput Input { get; set; } }
}
