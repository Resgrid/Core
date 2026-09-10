using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.WorkOrders;
namespace Resgrid.Web.Services.Controllers.v4
{
    public sealed class WorkOrderMaintenanceCommand<T> { public int Id { get; set; } public T Input { get; set; } }
    public sealed class WorkOrderCancelPartWitness { public int Id { get; set; } public int PartId { get; set; } public int Revision { get; set; } public string Reason { get; set; } }
    public sealed partial class WorkOrdersController
    {
        private void MaintenanceAvailable() { if (_maintenance == null) throw new WorkOrderException(503, "MaintenanceUnavailable"); }
        [HttpPost("CancelWorkOrderPartWitness")]
        public async Task<IActionResult> CancelWorkOrderPartWitness([FromBody] WorkOrderCancelPartWitness command) { MaintenanceAvailable(); Required(command); await _maintenance.CancelPartWitnessAsync(Actor, command.Id, command.PartId, command.Revision, command.Reason); return Reply(await _orders.GetAsync(Actor, command.Id)); }
        [HttpGet("GetWorkOrderInventoryChoices")]
        public async Task<IActionResult> GetWorkOrderInventoryChoices(string kind, string itemId = null, int page = 0) { MaintenanceAvailable(); return Reply(await _maintenance.InventoryChoicesAsync(Actor, kind, itemId, page)); }
        [HttpGet("GetWorkOrderHolds")]
        public async Task<IActionResult> GetWorkOrderHolds(int id) { MaintenanceAvailable(); return Reply(await _maintenance.HoldsAsync(Actor, id)); }
        [HttpPost("AddWorkOrderHold")]
        public async Task<IActionResult> AddWorkOrderHold([FromBody] WorkOrderMaintenanceCommand<WorkOrderHoldInput> command) { MaintenanceAvailable(); Required(command); await _maintenance.AddHoldAsync(Actor, command.Id, Required(command.Input)); return Reply(await _maintenance.HoldsAsync(Actor, command.Id)); }
        [HttpPost("ReleaseWorkOrderHold")]
        public async Task<IActionResult> ReleaseWorkOrderHold([FromBody] WorkOrderMaintenanceCommand<WorkOrderReleaseInput> command) { MaintenanceAvailable(); Required(command); await _maintenance.ReleaseHoldAsync(Actor, command.Id, Required(command.Input)); return Reply(new { Released = true }); }
        [HttpGet("GetWorkOrderRecurrences")]
        public async Task<IActionResult> GetWorkOrderRecurrences(int page = 0) { MaintenanceAvailable(); var rows = await _maintenance.RecurrencesAsync(Actor, page); return Reply(rows.Take(50), System.Math.Min(rows.Count, 50), rows.Count > 50); }
        [HttpGet("GetWorkOrderRecurrence")]
        public async Task<IActionResult> GetWorkOrderRecurrence(int id, int historyPage = 0) { MaintenanceAvailable(); return Reply(await _maintenance.RecurrenceAsync(Actor, id, historyPage)); }
        [HttpPost("SaveWorkOrderRecurrence")]
        public async Task<IActionResult> SaveWorkOrderRecurrence([FromBody] WorkOrderRecurrenceInput input) { MaintenanceAvailable(); var id = await _maintenance.SaveRecurrenceAsync(Actor, Required(input)); return Reply(await _maintenance.RecurrenceAsync(Actor, id)); }
        [HttpPost("RecordWorkOrderReading")]
        public async Task<IActionResult> RecordWorkOrderReading([FromBody] WorkOrderMaintenanceCommand<WorkOrderReadingInput> command) { MaintenanceAvailable(); Required(command); await _maintenance.RecordReadingAsync(Actor, command.Id, Required(command.Input)); return Reply(await _maintenance.RecurrenceAsync(Actor, command.Id)); }
        [HttpPost("DeferWorkOrder")]
        public async Task<IActionResult> DeferWorkOrder([FromBody] WorkOrderMaintenanceCommand<WorkOrderDeferralInput> command) { MaintenanceAvailable(); Required(command); await _maintenance.DeferAsync(Actor, command.Id, Required(command.Input)); return Reply(await _orders.GetAsync(Actor, command.Id)); }
    }
}
