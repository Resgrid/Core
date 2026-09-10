using System;
using System.Collections.Generic;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Web.Areas.User.Models.WorkOrders
{
    public sealed class WorkOrderOperationsView
    {
        public WorkOrderDetail Detail { get; set; }
        public WorkOrderChoices Choices { get; set; }
        public List<WorkOrderVendorChargeView> VendorCharges { get; set; } = new();
        public List<WorkOrderPartMovement> Movements { get; set; } = new();
    }
    public sealed class WorkOrderBulkSelection { public bool Selected { get; set; } public int Id { get; set; } public int Revision { get; set; } }
    public sealed class WorkOrderBulkForm
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString("D");
        public string Csv { get; set; }
        public string UserId { get; set; }
        public int? RoleId { get; set; }
        public List<WorkOrderBulkSelection> Selections { get; set; } = new();
    }
    public sealed class WorkOrderBulkView
    {
        public WorkOrderBulkForm Form { get; set; } = new();
        public WorkOrderPage Orders { get; set; }
        public WorkOrderChoices Choices { get; set; }
        public WorkOrderBulkInput Input { get; set; }
        public WorkOrderBulkResult Result { get; set; }
        public int Page { get; set; }
        public bool Applied { get; set; }
    }
    public sealed class WorkOrderPolicyView { public WorkOrderPolicyInput Input { get; set; } public bool CanWrite { get; set; } }
}
