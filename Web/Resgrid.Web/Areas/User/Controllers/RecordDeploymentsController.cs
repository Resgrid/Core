using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Deployments;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
    /// <summary>Read-only deployment reporting. Commands live in Deployments / DeploymentOrders.</summary>
    [Area("User"), Authorize(Policy = ResgridResources.Record_View)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Resgrid.Web.Helpers.DepartmentLocalTime]
    public class RecordDeploymentsController : SecureBaseController
    {
        private readonly IRecordDeploymentsService _orders;
        private readonly IDeploymentService _deployments;
        private readonly ITimeTrackingService _time;
        private readonly IDepartmentsService _departments;
        private readonly IRecordsCutoverService _cutover;

        public RecordDeploymentsController(IRecordDeploymentsService orders, IDeploymentService deployments,
            ITimeTrackingService time, IDepartmentsService departments, IRecordsCutoverService cutover)
        {
            _orders = orders; _deployments = deployments; _time = time; _departments = departments; _cutover = cutover;
        }

        private static bool CanManage => ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanManageDeployments();
        private static bool CanViewAll => CanManage || ClaimsAuthorizationHelper.CanViewDeployments();

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Retained reports remain available when operational features are disabled.
            if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) { context.Result = NotFound(); return; }
            await next();
        }

        private async Task<Deployment> AccessibleAsync(string id)
        {
            var deployment = await _deployments.GetDeploymentByIdAsync(id, DepartmentId);
            return deployment != null && deployment.DepartmentId == DepartmentId && !deployment.IsDeleted &&
                (CanViewAll || deployment.Personnel.Any(p => p.UserId == UserId)) ? deployment : null;
        }

        private async Task<Dictionary<string, string>> NamesAsync() =>
            (await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
                .GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First().Name);

        [HttpGet]
        public async Task<IActionResult> Index(bool includeClosed = true, int page = 1)
        {
            var model = new DeploymentReportsView { Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false) };
            var list = model.Operations;
            list.Page = Math.Clamp(page, 1, int.MaxValue / list.PageSize);
            list.OpenOnly = !includeClosed;
            if (CanViewAll)
            {
                list.Total = await _deployments.CountDeploymentsForDepartmentAsync(DepartmentId, list.OpenOnly);
                list.Deployments = await _deployments.GetDeploymentsForDepartmentAsync(DepartmentId, list.OpenOnly, (list.Page - 1) * list.PageSize, list.PageSize);
            }
            else
            {
                var mine = await _deployments.GetDeploymentsForUserAsync(DepartmentId, UserId, list.OpenOnly);
                list.Total = mine.Count;
                list.Deployments = mine.Skip((list.Page - 1) * list.PageSize).Take(list.PageSize).ToList();
            }
            // Historical external orders remain readable before they are linked into the workspace.
            foreach (var order in await _orders.ListAsync(DepartmentId, UserId, includeClosed))
            {
                var linked = await _deployments.GetDeploymentByExternalOrderIdAsync(order.RmsExternalOrderId, DepartmentId);
                if (linked == null || await AccessibleAsync(linked.DeploymentId) == null)
                    model.Orders.Add(order);
            }
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Report(string id)
        {
            var deployment = await AccessibleAsync(id);
            if (deployment == null) return NotFound();
            return View(new DeploymentReportView
            {
                Deployment = deployment, Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false),
                TimeReports = await _time.GetTimeReportsAsync(id, DepartmentId), Expenses = await _time.GetExpensesAsync(id, DepartmentId),
                Attachments = await _deployments.GetAttachmentsAsync(id, DepartmentId), PersonnelNames = await NamesAsync(), CanManage = CanManage
            });
        }

        [HttpGet]
        public async Task<IActionResult> Print(string id)
        {
            var result = await Report(id);
            if (result is ViewResult view)
            {
                view.ViewName = "Report";
                view.ViewData["StandaloneReport"] = true;
            }
            return result;
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            try
            {
                var order = await _orders.GetAsync(DepartmentId, UserId, id);
                if (order == null) return NotFound();
                var linked = await _deployments.GetDeploymentByExternalOrderIdAsync(id, DepartmentId);
                return View(new RecordDeploymentDetailsView
                {
                    Deployment = order, Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false),
                    PersonnelNames = await NamesAsync(), CanEdit = false,
                    OperationalDeploymentId = linked != null && await AccessibleAsync(linked.DeploymentId) != null ? linked.DeploymentId : null
                });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        [HttpGet]
        public IActionResult New() => RedirectToAction("New", "DeploymentOrders");

        [HttpGet]
        public async Task<IActionResult> ForRecord(string id)
        {
            try
            {
                var order = await _orders.GetForRecordAsync(DepartmentId, UserId, id);
                return order == null ? NotFound() : RedirectToAction("Details", new { id = order.Order.RmsExternalOrderId });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        [HttpGet]
        public async Task<IActionResult> Artifact(string id)
        {
            try
            {
                var order = await _orders.GetAsync(DepartmentId, UserId, id, true);
                if (order?.Order?.ArtifactData == null) return NotFound();
                return File(order.Order.ArtifactData, "application/octet-stream", order.Order.ArtifactFileName ?? "external-order");
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        [HttpGet]
        [Authorize(Policy = ResgridResources.Record_Export)]
        public async Task<IActionResult> ExportTimeEntries(string id)
        {
            if (await AccessibleAsync(id) == null) return NotFound();
            return Csv(await _time.ExportTimeEntriesCsvAsync(id, DepartmentId), "deployment-time.csv");
        }

        [HttpGet]
        public async Task<IActionResult> Manifest(string id)
        {
            if (await AccessibleAsync(id) == null) return NotFound();
            // Render only; GenerateManifestAsync would persist a new attachment.
            return Content(await _deployments.RenderManifestHtmlAsync(id, DepartmentId), "text/html", Encoding.UTF8);
        }

        [HttpGet]
        public async Task<IActionResult> TimeReport(string id)
        {
            var report = await _time.GetTimeReportByIdAsync(id, DepartmentId);
            if (report == null || await AccessibleAsync(report.DeploymentId) == null) return NotFound();
            return Content(await _time.RenderTimeReportHtmlAsync(id, DepartmentId), "text/html", Encoding.UTF8);
        }

        [HttpGet]
        public async Task<IActionResult> Attachment(int id)
        {
            var file = await _deployments.GetAttachmentAsync(id, DepartmentId, false);
            if (file == null || await AccessibleAsync(file.DeploymentId) == null) return NotFound();
            file = await _deployments.GetAttachmentAsync(id, DepartmentId, true);
            if (file?.Data == null) return NotFound();
            return File(file.Data, "application/octet-stream", file.FileName ?? "deployment-document");
        }

        [HttpGet]
        [Authorize(Policy = ResgridResources.Record_Export)]
        public async Task<IActionResult> Export(string id)
        {
            var d = await AccessibleAsync(id);
            if (d == null) return NotFound();
            // Explicit projection excludes protected notes, document bytes and ORM metadata.
            var reports = await _time.GetTimeReportsAsync(id, DepartmentId);
            var time = new List<object>();
            foreach (var summary in reports)
            {
                var report = await _time.GetTimeReportByIdAsync(summary.DeploymentTimeReportId, DepartmentId);
                if (report != null) time.Add(new { report.ReportNumber, report.ReportDate, report.Status,
                    Entries = report.Entries.Select(e => new { e.SubjectType, e.SubjectId, e.EntryType, e.StartTime, e.EndTime, e.Hours, e.MileageKm }) });
            }
            var expenses = await _time.GetExpensesAsync(id, DepartmentId);
            var data = new
            {
                d.DeploymentId, d.Name, d.Status, d.FinanceMode, d.IncidentNumber, d.ResourceOrderNumber, d.StartOn, d.EndOn, d.Currency,
                Units = d.Units.Select(u => new { u.UnitId, u.UnitName, u.CallSign, u.AddedOn, u.RemovedOn }),
                Personnel = d.Personnel.Select(p => new { p.UserId, p.CertificationCode, p.CallSign, p.AddedOn, p.RemovedOn }),
                Equipment = d.Equipment.Select(e => new { e.DeploymentEquipmentId, e.FreeTextName, e.InventoryAssetId, e.InventoryItemId, e.IssuedOn, e.ReturnedOn }),
                TimeReports = time,
                Expenses = expenses.Select(e => new { e.ExpenseDate, e.ExpenseType, e.Amount, e.Currency, e.Billable })
            };
            return File(Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented)), "application/json", "deployment-report.json");
        }

        [HttpGet]
        [Authorize(Policy = ResgridResources.Record_Export)]
        public async Task<IActionResult> ExportOrders(string id)
        {
            try
            {
                var order = await _orders.GetAsync(DepartmentId, UserId, id);
                if (order == null) return NotFound();
                var csv = new StringBuilder("Order,Incident,Request,Resource,Position,User,Unit,Status,MobilizedUtc,CheckedInUtc,ReleasedUtc,ReturnedUtc\r\n");
                foreach (var f in order.Fills)
                    csv.AppendLine(string.Join(",", new object[] { order.Order.OrderNumber, order.Order.IncidentName, f.RequestNumber, f.ResourceKind,
                        f.Position, f.AssignedUserId, f.AssignedUnitId, (RmsDeploymentFillStatus)f.Status, f.MobilizedOn, f.CheckedInOn, f.ReleasedOn, f.ReturnedOn }.Select(Cell)));
                return Csv(csv.ToString(), "deployment-resources.csv");
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        [HttpGet]
        [Authorize(Policy = ResgridResources.Record_Export)]
        public async Task<IActionResult> ExportSummary(bool includeClosed = true)
        {
            var csv = new StringBuilder("DeploymentId,Name,Status,FinanceMode,Incident,Order,StartUtc,EndUtc,Currency\r\n");
            var skip = 0;
            while (true)
            {
                HttpContext.RequestAborted.ThrowIfCancellationRequested();
                var rows = CanViewAll ? await _deployments.GetDeploymentsForDepartmentAsync(DepartmentId, !includeClosed, skip, 200)
                    : await _deployments.GetDeploymentsForUserAsync(DepartmentId, UserId, !includeClosed);
                foreach (var d in rows)
                    csv.AppendLine(string.Join(",", new object[] { d.DeploymentId, d.Name, (DeploymentStatuses)d.Status, (DeploymentFinanceModes)d.FinanceMode,
                        d.IncidentNumber, d.ResourceOrderNumber, d.StartOn, d.EndOn, d.Currency }.Select(Cell)));
                if (!CanViewAll || rows.Count < 200) break;
                skip += rows.Count;
            }
            return Csv(csv.ToString(), "deployment-summary.csv");
        }

        private FileContentResult Csv(string value, string name) => File(Encoding.UTF8.GetBytes(value), "text/csv; charset=utf-8", name);
        internal static string Cell(object value)
        {
            var text = value is DateTime date ? date.ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith('=') || trimmed.StartsWith('+') || trimmed.StartsWith('-') || trimmed.StartsWith('@') || text.StartsWith('\t') || text.StartsWith('\r')) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
