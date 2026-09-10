using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Web.Services.Controllers.v4
{
    public sealed partial class WorkOrdersController
    {
        [HttpGet("GetWorkOrderStats")]
        public async Task<IActionResult> GetWorkOrderStats([FromQuery] WorkOrderReportQuery query)
        {
            if (_reports == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
            return Reply(await _reports.GetWorkOrderStatsAsync(Actor, query));
        }
        [HttpGet("GetWorkOrderServiceHistory")]
        public async Task<IActionResult> GetWorkOrderServiceHistory([FromQuery] WorkOrderReportQuery query)
        {
            if (_reports == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
            var page = await _reports.GetWorkOrderHistoryAsync(Actor, query);
            return Reply(page, page.Items.Count, page.NextAfterId.HasValue);
        }
        [HttpPost("ExportWorkOrderHistory")]
        public async Task<IActionResult> ExportWorkOrderHistory([FromBody] WorkOrderReportQuery query)
        {
            if (_reports == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(await _reports.ExportWorkOrdersAsync(Actor, Required(query)), "text/csv; charset=utf-8", "work-order-history.csv");
        }
    }
}
