using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;
using Resgrid.Web.Services.Models.v4.WorkOrders;

namespace Resgrid.Web.Services.Controllers.v4
{
    public sealed partial class WorkOrdersController
    {
        private IWorkOrderOperationsService OperationsService => _orders as IWorkOrderOperationsService ?? throw new WorkOrderException(503, "MaintenanceUnavailable");
        [HttpGet("GetWorkOrderPolicy")]
        public async Task<IActionResult> GetWorkOrderPolicy() => Reply(await OperationsService.PolicyAsync(Actor));
        [HttpPost("SaveWorkOrderPolicy")]
        public async Task<IActionResult> SaveWorkOrderPolicy([FromBody] WorkOrderPolicyInput input) { await OperationsService.SavePolicyAsync(Actor, Required(input)); return Reply(await OperationsService.PolicyAsync(Actor)); }
        [HttpPost("PreviewWorkOrderBatch")]
        public async Task<IActionResult> PreviewWorkOrderBatch([FromBody] WorkOrderBulkInput input) => Reply(await OperationsService.PreviewBulkAsync(Actor, Required(input)));
        [HttpPost("PreviewWorkOrderImport")]
        public async Task<IActionResult> PreviewWorkOrderImport([FromBody] WorkOrderCsvInput input) { Required(input); var batch = WorkOrderCsvImport.Parse(input.Csv, input.RequestId); var preview = await OperationsService.PreviewBulkAsync(Actor, batch); batch.PreviewHash = preview.PreviewHash; return Reply(new { Input = batch, Preview = preview }); }
        [HttpPost("ApplyWorkOrderBatch")]
        public async Task<IActionResult> ApplyWorkOrderBatch([FromBody] WorkOrderBulkInput input) => Reply(await OperationsService.ApplyBulkAsync(Actor, Required(input)));
        [HttpPost("RequestWorkOrderApproval")]
        public async Task<IActionResult> RequestWorkOrderApproval([FromBody] WorkOrderOperationsInput<WorkOrderApprovalInput> input) { Required(input); await OperationsService.RequestApprovalAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
        [HttpPost("DecideWorkOrderApproval")]
        public async Task<IActionResult> DecideWorkOrderApproval([FromBody] WorkOrderOperationsInput<WorkOrderApprovalInput> input) { Required(input); await OperationsService.DecideApprovalAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
        [HttpGet("GetWorkOrderVendorCharges")]
        public async Task<IActionResult> GetWorkOrderVendorCharges(int id, int afterId = 0) { var rows = await OperationsService.VendorChargesAsync(Actor, id, afterId); return Reply(rows, rows.Count, rows.Count == 50); }
        [HttpPost("AddWorkOrderVendorCharge")]
        public async Task<IActionResult> AddWorkOrderVendorCharge([FromBody] WorkOrderOperationsInput<WorkOrderVendorChargeInput> input) { Required(input); await OperationsService.AddVendorChargeAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
        [HttpPost("VoidWorkOrderVendorCharge")]
        public async Task<IActionResult> VoidWorkOrderVendorCharge([FromBody] WorkOrderCommandInput input) { Required(input); await OperationsService.VoidVendorChargeAsync(Actor, input.Id, input.ChildId, input.Revision, input.Note); return Reply(await _orders.GetAsync(Actor, input.Id)); }
        [HttpPost("ReserveWorkOrderPart")]
        public async Task<IActionResult> ReserveWorkOrderPart([FromBody] WorkOrderApiPartInput input) { Required(input); await OperationsService.ReservePartAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
        [HttpPost("MoveWorkOrderPart")]
        public async Task<IActionResult> MoveWorkOrderPart([FromBody] WorkOrderOperationsInput<WorkOrderPartMovementInput> input) { Required(input); await OperationsService.MovePartAsync(Actor, input.Id, input.ChildId, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
        [HttpGet("GetWorkOrderPartMovements")]
        public async Task<IActionResult> GetWorkOrderPartMovements(int id, int afterId = 0) { var rows = await OperationsService.PartMovementsAsync(Actor, id, afterId); return Reply(rows, rows.Count, rows.Count == 50); }
        [HttpPost("CancelWorkOrderPartMovement")]
        public async Task<IActionResult> CancelWorkOrderPartMovement([FromBody] WorkOrderCommandInput input) { Required(input); await OperationsService.CancelPartMovementAsync(Actor, input.Id, input.ChildId, input.Revision, input.Note); return Reply(await _orders.GetAsync(Actor, input.Id)); }
    }
    public sealed class WorkOrderOperationsInput<T> where T : class { public int Id { get; set; } public int ChildId { get; set; } public T Input { get; set; } }
    public sealed class WorkOrderCsvInput { public string RequestId { get; set; } public string Csv { get; set; } }
}
