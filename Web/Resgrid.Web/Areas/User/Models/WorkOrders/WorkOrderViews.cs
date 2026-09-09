using Resgrid.Model.WorkOrders;

namespace Resgrid.Web.Areas.User.Models.WorkOrders
{
	public sealed class WorkOrderIndexView { public WorkOrderPage Orders { get; set; } public WorkOrderFilter Filter { get; set; } public WorkOrderChoices Choices { get; set; } }
	public sealed class WorkOrderEditView { public int Id { get; set; } public WorkOrderInput Input { get; set; } public WorkOrderChoices Choices { get; set; } public bool CanManage { get; set; } }
	public sealed class WorkOrderDetailView { public System.Collections.Generic.List<WorkOrderHoldView> Holds { get; set; } = new(); public bool CanRelease { get; set; } public WorkOrderDetail Detail { get; set; } public WorkOrderChoices Choices { get; set; } }
	public sealed class WorkOrderLockedView { public WorkOrderFilter Filter { get; set; } = new WorkOrderFilter(); public string Page { get; set; } public int Id { get; set; } }
	public sealed class WorkOrderRecurrencePageView { public System.Collections.Generic.List<WorkOrderRecurrenceView> Rows { get; set; } = new(); public int Page { get; set; } public bool CanWrite { get; set; } }
	public sealed class WorkOrderRecurrenceEditView { public WorkOrderRecurrenceInput Input { get; set; } = new(); public WorkOrderChoices Choices { get; set; } = new(); }
	public sealed class WorkOrderRecurrenceDetailView { public WorkOrderRecurrenceView Detail { get; set; } public bool CanWrite { get; set; } }
}
