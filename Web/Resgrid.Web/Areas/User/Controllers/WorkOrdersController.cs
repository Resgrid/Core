using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Web.Areas.User.Models.WorkOrders;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[WorkOrderFormCulture]
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(1024 * 1024)]
	public sealed partial class WorkOrdersController : SecureBaseController
	{
		private readonly IWorkOrdersService _orders;
		private readonly IWorkOrderMaintenanceService _maintenance;
		private readonly IWorkOrderAuthorizationService _authorization;
		private readonly IReadinessAccessService _access;
		private readonly IProtectedGrantContext _grant;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> _strings;
		public WorkOrdersController(IWorkOrdersService orders, IWorkOrderAuthorizationService authorization, IReadinessAccessService access, IProtectedGrantContext grant,
			IDepartmentDataProtectionService protection, IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> strings, IWorkOrderMaintenanceService maintenance = null)
		{ _orders = orders; _authorization = authorization; _access = access; _grant = grant; _protection = protection; _strings = strings; _maintenance = maintenance; }
		private ChecklistActor Actor => new ChecklistActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = _grant.GrantToken };
		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			try { await _authorization.RequireMemberAsync(Actor); } catch (WorkOrderException membershipError) { context.Result = StatusCode(membershipError.StatusCode, new { message = _strings[membershipError.Code].Value }); return; }
			ViewBag.ProtectionEnforced = await _protection.IsProtectionEnforcedAsync(DepartmentId); ViewBag.ProtectedGrant = _grant.GrantToken; ViewBag.GrantExpiresOn = HttpProtectedGrantContext.ReadExpiry(Request);
			if (!ModelState.IsValid) { context.Result = StatusCode(400, new { message = _strings["InvalidInput"].Value }); return; }
			var executed = await next();
			if (executed.Exception is WorkOrderException ex)
			{
				executed.ExceptionHandled = true;
				if (HttpMethods.IsGet(Request.Method) && ex.Code == "ProtectedDataRequired")
				{
					int.TryParse(Request.Query["id"].ToString() is { Length: > 0 } id ? id : context.RouteData.Values["id"]?.ToString(), out var orderId);
					executed.Result = View("Locked", new WorkOrderLockedView { Page = context.RouteData.Values["action"]?.ToString(), Id = orderId, Filter = context.ActionArguments.TryGetValue("filter", out var filters) && filters is WorkOrderFilter workOrderFilter ? workOrderFilter : new WorkOrderFilter() });
				}
				else executed.Result = StatusCode(ex.StatusCode, new { message = _strings[ex.Code].Value, code = ex.Code });
			}
		}
		[HttpGet]
		public async Task<IActionResult> Index(WorkOrderFilter filter) => View("Index", new WorkOrderIndexView { Orders = await _orders.ListAsync(Actor, filter), Filter = filter, Choices = await _orders.ChoicesAsync(Actor) });
		[HttpGet]
		public async Task<IActionResult> New()
		{
			if (!await _access.CanUseMaintenanceAsync(DepartmentId)) throw new WorkOrderException(402, "ReadinessProRequired");
			return View("Edit", new WorkOrderEditView { Input = new WorkOrderInput { RequestId = Guid.NewGuid().ToString("D") }, Choices = await _orders.ChoicesAsync(Actor), CanManage = await _authorization.CanManageAsync(Actor, null) });
		}
		[HttpGet]
		public async Task<IActionResult> Detail(int id) { var detail = await _orders.GetAsync(Actor, id); return View("Detail", new WorkOrderDetailView { Detail = detail, Choices = await _orders.ChoicesAsync(Actor), Holds = _maintenance == null ? new() : await _maintenance.HoldsAsync(Actor, id), CanRelease = await _authorization.CanManageAsync(Actor, detail.Order.GroupId) }); }
		[HttpGet]
		public async Task<IActionResult> Edit(int id)
		{
			var detail = await _orders.GetAsync(Actor, id); if (!detail.CanEdit) throw new WorkOrderException(403, "PermissionRequired");
			return View("Edit", new WorkOrderEditView { Id = id, Input = detail.Input, Choices = await _orders.ChoicesAsync(Actor), CanManage = detail.CanManage });
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Save(int id, WorkOrderInput input)
		{
			if (id == 0) id = (await _orders.CreateAsync(Actor, input)).Order.Id; else await _orders.UpdateAsync(Actor, id, input); return Json(new { id });
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Transition(int id, WorkOrderTransition input) { await _orders.TransitionAsync(Actor, id, input); return Json(new { id }); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Assign(int id, WorkOrderAssignment input) { await _orders.AssignAsync(Actor, id, input); return Json(new { id }); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Accept(int id, int revision) { await _orders.AcceptAssignmentAsync(Actor, id, revision); return Json(new { id }); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Comment(int id, int revision, string note) { await _orders.CommentAsync(Actor, id, revision, note); return Json(new { id }); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Labor(int id, WorkOrderLaborInput input) { await _orders.AddLaborAsync(Actor, id, input); return Json(new { id }); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Part(int id, WorkOrderPartInput input) { await _orders.AddPartAsync(Actor, id, input); return Json(new { id }); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> VoidPart(int id, int partId, int revision, string reason) { await _orders.VoidPartAsync(Actor, id, partId, revision, reason); return Json(new { id }); }
		[HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(11 * 1024 * 1024)]
		public async Task<IActionResult> Upload(int id, int revision, IFormFile file)
		{
			if (file == null || file.Length > 10 * 1024 * 1024) throw new WorkOrderException(400, "InvalidFile");
			using var buffer = new MemoryStream(); await file.CopyToAsync(buffer); await _orders.AddFileAsync(Actor, id, revision, file.FileName, file.ContentType, buffer.ToArray()); return Json(new { id });
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> WithdrawFile(int id, int fileId, int revision, string reason) { await _orders.WithdrawFileAsync(Actor, id, fileId, revision, reason); return Json(new { id }); }
		[HttpGet]
		public async Task<IActionResult> Evidence(int id) { var file = await _orders.GetFileAsync(Actor, id); Response.Headers["X-Content-Type-Options"] = "nosniff"; return File(file.Data, file.ContentType, file.Content); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Export(int id) { var detail = await _orders.GetAsync(Actor, id); return File(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(detail, Formatting.Indented)), "application/json", "work-order-" + id + ".json"); }
		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Reopen(string destination, int id, WorkOrderFilter filter) => destination switch { "Detail" => Detail(id), "Edit" => Edit(id), "New" => New(), "Recurrence" => Recurrence(id), "EditRecurrence" => EditRecurrence(id), "NewRecurrence" => NewRecurrence(), "Recurrences" => Recurrences(filter?.Page ?? 0), "Index" => Index(filter ?? new WorkOrderFilter()), _ => Task.FromResult<IActionResult>(BadRequest()) };
	}
}
