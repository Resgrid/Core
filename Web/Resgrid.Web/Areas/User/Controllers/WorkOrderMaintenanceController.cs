using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.WorkOrders;
using Resgrid.Web.Areas.User.Models.WorkOrders;
namespace Resgrid.Web.Areas.User.Controllers
{
    public sealed partial class WorkOrdersController
    {
        private void MaintenanceAvailable() { if (_maintenance == null) throw new WorkOrderException(503, "MaintenanceUnavailable"); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelPartWitness(int id, int partId, int revision, string reason) { MaintenanceAvailable(); await _maintenance.CancelPartWitnessAsync(Actor, id, partId, revision, reason); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> PartChoices(string kind, string itemId, int page = 0) { MaintenanceAvailable(); return Json(await _maintenance.InventoryChoicesAsync(Actor, kind, itemId, page)); }
        [HttpGet]
        public async Task<IActionResult> Recurrences(int page = 0)
        {
            MaintenanceAvailable();
            return View("Recurrences", new WorkOrderRecurrencePageView { Rows = await _maintenance.RecurrencesAsync(Actor, page), Page = page, CanWrite = await _access.CanUseMaintenanceAsync(DepartmentId) });
        }
        [HttpGet]
        public async Task<IActionResult> NewRecurrence()
        {
            MaintenanceAvailable();
            if (!await _access.CanUseMaintenanceAsync(DepartmentId)) throw new WorkOrderException(402, "ReadinessProRequired");
            return View("EditRecurrence", new WorkOrderRecurrenceEditView { Input = new WorkOrderRecurrenceInput { AnchorLocal = DateTime.UtcNow.Date.AddDays(1).AddHours(9), Template = new WorkOrderInput { RequestId = Guid.NewGuid().ToString("D"), Type = WorkOrderType.Preventive } }, Choices = await _orders.ChoicesAsync(Actor) });
        }
        [HttpGet]
        public async Task<IActionResult> EditRecurrence(int id)
        {
            MaintenanceAvailable();
            if (!await _access.CanUseMaintenanceAsync(DepartmentId)) throw new WorkOrderException(402, "ReadinessProRequired");
            return View("EditRecurrence", new WorkOrderRecurrenceEditView { Input = (await _maintenance.RecurrenceAsync(Actor, id)).Settings, Choices = await _orders.ChoicesAsync(Actor) });
        }
        [HttpGet]
        public async Task<IActionResult> Recurrence(int id, int historyPage = 0)
        {
            MaintenanceAvailable(); return View("Recurrence", new WorkOrderRecurrenceDetailView { Detail = await _maintenance.RecurrenceAsync(Actor, id, historyPage), CanWrite = await _access.CanUseMaintenanceAsync(DepartmentId) });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveRecurrence(WorkOrderRecurrenceInput input, string serviceStart, string serviceEnd)
        {
            MaintenanceAvailable();
            if (!TimeOnly.TryParse(serviceStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || !TimeOnly.TryParse(serviceEnd, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) throw new WorkOrderException(400, "RecurrenceInvalid");
            input.ServiceStartMinute = start.Hour * 60 + start.Minute; input.ServiceEndMinute = end.Hour * 60 + end.Minute;
            return Json(new { id = await _maintenance.SaveRecurrenceAsync(Actor, input) });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reading(int id, WorkOrderReadingInput input) { MaintenanceAvailable(); await _maintenance.RecordReadingAsync(Actor, id, input); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Hold(int id, WorkOrderHoldInput input) { MaintenanceAvailable(); await _maintenance.AddHoldAsync(Actor, id, input); return Json(new { id }); }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ReleaseHold(int id, int holdId, WorkOrderReleaseInput input)
        {
            MaintenanceAvailable();
            if (!(await _maintenance.HoldsAsync(Actor, id)).Any(h => h.Hold.Id == holdId)) throw new WorkOrderException(404, "Unavailable");
            await _maintenance.ReleaseHoldAsync(Actor, holdId, input); return Json(new { id });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Defer(int id, WorkOrderDeferralInput input) { MaintenanceAvailable(); await _maintenance.DeferAsync(Actor, id, input); return Json(new { id }); }
    }
}
