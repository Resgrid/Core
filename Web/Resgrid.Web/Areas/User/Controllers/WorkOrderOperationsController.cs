using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Models.WorkOrders;

namespace Resgrid.Web.Areas.User.Controllers
{
    [WorkOrderFormValues]
    public sealed partial class WorkOrdersController
    {
        [HttpGet]
        public async Task<IActionResult> Operations(int id, int vendorAfterId = 0, int movementAfterId = 0) => View("Operations", new WorkOrderOperationsView { Detail = await _orders.GetAsync(Actor, id), Choices = await _orders.ChoicesAsync(Actor), VendorCharges = await OperationsService.VendorChargesAsync(Actor, id, vendorAfterId), Movements = await OperationsService.PartMovementsAsync(Actor, id, movementAfterId) });
        [HttpGet]
        public async Task<IActionResult> Policy() => View("Policy", new WorkOrderPolicyView { Input = await OperationsService.PolicyAsync(Actor), CanWrite = await _access.CanUseMaintenanceAsync(DepartmentId) });
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePolicy(WorkOrderPolicyInput input, string holidays, string businessStart, string businessEnd, List<int> weekdays)
        {
            if (input?.Calendar == null || input.SpendingRules == null || input.Calendar.Targets == null) throw new WorkOrderException(400, "OperationsPolicyInvalid");
            if (!TimeOnly.TryParseExact(businessStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || !TimeOnly.TryParseExact(businessEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                || weekdays == null || weekdays.Any(d => !new[] { 1, 2, 4, 8, 16, 32, 64 }.Contains(d))) throw new WorkOrderException(400, "OperationsPolicyInvalid");
            input.Calendar.StartMinute = start.Hour * 60 + start.Minute; input.Calendar.EndMinute = end == TimeOnly.MinValue ? 1440 : end.Hour * 60 + end.Minute;
            input.Calendar.Weekdays = weekdays.Aggregate(0, (mask, day) => mask | day);
            input.SpendingRules = input.SpendingRules.Where(r => !string.IsNullOrWhiteSpace(r.Currency)).ToList();
            input.Calendar.Targets = input.Calendar.Targets.Where(t => t.ResponseMinutes != 0 || t.RepairMinutes != 0).ToList();
            input.Calendar.Holidays = (holidays ?? "").Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            await OperationsService.SavePolicyAsync(Actor, input); return Json(new { id = 0 });
        }
        [HttpGet]
        public async Task<IActionResult> Bulk(int page = 0)
        {
            if (!await _access.CanUseMaintenanceAsync(DepartmentId)) throw new WorkOrderException(402, "ReadinessProRequired");
            return View("Bulk", new WorkOrderBulkView { Orders = await _orders.ListAsync(Actor, new WorkOrderFilter { Page = page }), Choices = await _orders.ChoicesAsync(Actor), Page = page });
        }
        [HttpGet]
        public IActionResult ImportTemplate() => File(Encoding.UTF8.GetBytes(WorkOrderCsvImport.Header + "\r\n"), "text/csv; charset=utf-8", "work-orders-template.csv");
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> PreviewBulk(WorkOrderBulkForm form)
        {
            if (form == null || form.Selections == null) throw new WorkOrderException(400, "BulkInputInvalid");
            var input = !string.IsNullOrWhiteSpace(form.Csv) ? WorkOrderCsvImport.Parse(form.Csv, form.RequestId) : new WorkOrderBulkInput { RequestId = form.RequestId,
                Rows = form.Selections.Where(s => s.Selected).Select((s, i) => new WorkOrderBulkRow { RowNumber = i + 1, WorkOrderId = s.Id, Assignment = new WorkOrderAssignment { Revision = s.Revision, UserId = form.UserId, RoleId = form.RoleId } }).ToList() };
            var result = await OperationsService.PreviewBulkAsync(Actor, input); input.PreviewHash = result.PreviewHash;
            return View("Bulk", new WorkOrderBulkView { Input = input, Result = result });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyBulk(string payload)
        {
            WorkOrderBulkInput input;
            try { input = JsonConvert.DeserializeObject<WorkOrderBulkInput>(payload ?? "", new JsonSerializerSettings { MaxDepth = 16 }); }
            catch (JsonException) { throw new WorkOrderException(400, "BulkInputInvalid"); }
            return View("Bulk", new WorkOrderBulkView { Input = input, Result = await OperationsService.ApplyBulkAsync(Actor, input), Applied = true });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestApproval(int id, WorkOrderApprovalInput input) { await OperationsService.RequestApprovalAsync(Actor, id, input); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DecideApproval(int id, WorkOrderApprovalInput input) { await OperationsService.DecideApprovalAsync(Actor, id, input); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddVendorCharge(int id, WorkOrderVendorChargeInput input) { await OperationsService.AddVendorChargeAsync(Actor, id, input); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> VoidVendorCharge(int id, int chargeId, int revision, string reason) { await OperationsService.VoidVendorChargeAsync(Actor, id, chargeId, revision, reason); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ReservePart(int id, WorkOrderPartInput input) { await OperationsService.ReservePartAsync(Actor, id, input); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MovePart(int id, int partId, WorkOrderPartMovementInput input) { await OperationsService.MovePartAsync(Actor, id, partId, input); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelPartMovement(int id, int movementId, int revision, string reason) { await OperationsService.CancelPartMovementAsync(Actor, id, movementId, revision, reason); return Json(new { id }); }
    }
}
