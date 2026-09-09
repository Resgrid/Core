using Resgrid.Model.WorkOrders;

namespace Resgrid.Web.Areas.User.Models.WorkOrders
{
	public sealed class WorkOrderIndexView { public WorkOrderPage Orders { get; set; } public WorkOrderFilter Filter { get; set; } public WorkOrderChoices Choices { get; set; } }
	public sealed class WorkOrderEditView { public int Id { get; set; } public WorkOrderInput Input { get; set; } public WorkOrderChoices Choices { get; set; } public bool CanManage { get; set; } }
	public sealed class WorkOrderDetailView { public WorkOrderDetail Detail { get; set; } public WorkOrderChoices Choices { get; set; } }
	public sealed class WorkOrderLockedView { public WorkOrderFilter Filter { get; set; } = new WorkOrderFilter(); public string Page { get; set; } public int Id { get; set; } }
}
