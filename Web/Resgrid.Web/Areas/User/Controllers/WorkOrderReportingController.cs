using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.WorkOrders;
using Resgrid.Web.Areas.User.Models.WorkOrders;

namespace Resgrid.Web.Areas.User.Controllers
{
    public sealed partial class WorkOrdersController
    {
        [HttpGet]
        public async Task<IActionResult> Reports(WorkOrderReportQuery query)
        {
            if (_reports == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
            query.FromUtc = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(query.FromUtc);
            query.UntilUtc = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(query.UntilUtc);
            var stats = await _reports.GetWorkOrderStatsAsync(Actor, query);
            query.FromUtc = stats.FromUtc; query.UntilUtc = stats.UntilUtc;
            return View("Reports", new WorkOrderReportView { Query = query, Stats = stats, History = await _reports.GetWorkOrderHistoryAsync(Actor, query), Choices = await _orders.ChoicesAsync(Actor), CanWrite = await _access.CanUseMaintenanceAsync(DepartmentId) });
        }
        [HttpGet]
        public async Task<IActionResult> History(WorkOrderReportQuery query)
        {
            if (_reports == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
            query.FromUtc = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(query.FromUtc);
            query.UntilUtc = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(query.UntilUtc);
            return View("Reports", new WorkOrderReportView { Query = query, History = await _reports.GetWorkOrderHistoryAsync(Actor, query), Choices = await _orders.ChoicesAsync(Actor), CanWrite = await _access.CanUseMaintenanceAsync(DepartmentId) });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportCsv(WorkOrderReportQuery query)
        {
            if (_reports == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
            query.FromUtc = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(query.FromUtc);
            query.UntilUtc = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(query.UntilUtc);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(await _reports.ExportWorkOrdersAsync(Actor, query), "text/csv; charset=utf-8", "work-order-history.csv");
        }
    }
}
