using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.WorkOrders;

namespace Resgrid.Web.Services.Controllers.v4
{
	[Route("api/v{VersionId:apiVersion}/[controller]"), ApiVersion("4.0"), ApiExplorerSettings(GroupName = "v4"), Authorize]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(1024 * 1024)]
	public sealed class WorkOrdersController : V4AuthenticatedApiControllerbase, IAsyncActionFilter
	{
		private readonly IWorkOrdersService _orders;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> _strings;
		public WorkOrdersController(IWorkOrdersService orders, IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> strings) { _orders = orders; _strings = strings; }
		private ChecklistActor Actor => new ChecklistActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = Request.Headers[DataProtectionController.GrantHeader].ToString() };
		private OkObjectResult Reply<T>(T value, int count = 1, bool more = false) { var response = new WorkOrderApiResult<T> { Data = value, Status = ResponseHelper.Success, PageSize = count, HasMore = more }; ResponseHelper.PopulateV4ResponseData(response); return Ok(response); }
		private static T Required<T>(T input) where T : class => input ?? throw new WorkOrderException(400, "InvalidInput");
		[NonAction]
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!context.ModelState.IsValid) { context.Result = Failure(new WorkOrderException(400, "InvalidInput")); return; }
			var executed = await next(); if (executed.Exception is WorkOrderException ex) { executed.ExceptionHandled = true; executed.Result = Failure(ex); }
		}
		private ObjectResult Failure(WorkOrderException ex)
		{
			var problem = new ProblemDetails { Status = ex.StatusCode, Type = ex.Code == "ProtectedDataRequired" ? "protected_data_required" : "work_order_" + ex.Code, Title = _strings[ex.Code] };
			problem.Extensions["IsRedacted"] = ex.Code == "ProtectedDataRequired"; return new ObjectResult(problem) { StatusCode = ex.StatusCode };
		}
		[HttpGet("GetWorkOrders")]
		public async Task<IActionResult> GetWorkOrders([FromQuery] WorkOrderFilter filter) { var page = await _orders.ListAsync(Actor, filter); return Reply(page, page.Items.Count, page.HasMore); }
		[HttpGet("GetWorkOrder")]
		public async Task<IActionResult> GetWorkOrder(int id) => Reply(await _orders.GetAsync(Actor, id));
		[HttpGet("GetWorkOrderChoices")]
		public async Task<IActionResult> GetWorkOrderChoices() => Reply(await _orders.ChoicesAsync(Actor));
		[HttpGet("GetWorkOrderActivity")]
		public async Task<IActionResult> GetWorkOrderActivity(int id) { var detail = await _orders.GetAsync(Actor, id); return Reply(detail.Activities, detail.Activities.Count); }
		[HttpGet("GetWorkOrderHistoryForAsset")]
		public Task<IActionResult> GetWorkOrderHistoryForAsset(string assetId, int page = 0) { if (string.IsNullOrWhiteSpace(assetId)) throw new WorkOrderException(400, "InvalidInput"); return GetWorkOrders(new WorkOrderFilter { AssetId = assetId, Page = page }); }
		[HttpPost("NewWorkOrder")]
		public async Task<IActionResult> NewWorkOrder([FromBody] WorkOrderInput input) => Reply(await _orders.CreateAsync(Actor, Required(input)));
		[HttpPost("UpdateWorkOrder")]
		public async Task<IActionResult> UpdateWorkOrder([FromBody] WorkOrderUpdateInput input) { Required(input); await _orders.UpdateAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
		[HttpPost("SetWorkOrderStatus")]
		public async Task<IActionResult> SetWorkOrderStatus([FromBody] WorkOrderStatusInput input) { Required(input); await _orders.TransitionAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
		[HttpPost("AssignWorkOrder")]
		public async Task<IActionResult> AssignWorkOrder([FromBody] WorkOrderAssignInput input) { Required(input); await _orders.AssignAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
		[HttpPost("AcceptWorkOrderAssignment")]
		public async Task<IActionResult> AcceptWorkOrderAssignment([FromBody] WorkOrderCommandInput input) { Required(input); await _orders.AcceptAssignmentAsync(Actor, input.Id, input.Revision); return Reply(await _orders.GetAsync(Actor, input.Id)); }
		[HttpPost("AddWorkOrderComment")]
		public async Task<IActionResult> AddWorkOrderComment([FromBody] WorkOrderCommandInput input) { Required(input); await _orders.CommentAsync(Actor, input.Id, input.Revision, input.Note); return Reply(await _orders.GetAsync(Actor, input.Id)); }
		[HttpPost("AddWorkOrderLabor")]
		public async Task<IActionResult> AddWorkOrderLabor([FromBody] WorkOrderApiLaborInput input) { Required(input); await _orders.AddLaborAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
		[HttpPost("AddWorkOrderPart")]
		public async Task<IActionResult> AddWorkOrderPart([FromBody] WorkOrderApiPartInput input) { Required(input); await _orders.AddPartAsync(Actor, input.Id, Required(input.Input)); return Reply(await _orders.GetAsync(Actor, input.Id)); }
		[HttpPost("RemoveWorkOrderPart")]
		public async Task<IActionResult> RemoveWorkOrderPart([FromBody] WorkOrderCommandInput input) { Required(input); await _orders.VoidPartAsync(Actor, input.Id, input.ChildId, input.Revision, input.Note); return Reply(await _orders.GetAsync(Actor, input.Id)); }
		[HttpPost("UploadWorkOrderFile"), RequestSizeLimit(11 * 1024 * 1024)]
		public async Task<IActionResult> UploadWorkOrderFile([FromForm] int id, [FromForm] int revision, IFormFile file)
		{
			if (file == null || file.Length > 10 * 1024 * 1024) throw new WorkOrderException(400, "InvalidFile");
			using var buffer = new MemoryStream(); await file.CopyToAsync(buffer); await _orders.AddFileAsync(Actor, id, revision, file.FileName, file.ContentType, buffer.ToArray()); return Reply(await _orders.GetAsync(Actor, id));
		}
		[HttpGet("GetWorkOrderFile")]
		public async Task<IActionResult> GetWorkOrderFile(int id) { var file = await _orders.GetFileAsync(Actor, id); Response.Headers["X-Content-Type-Options"] = "nosniff"; return File(file.Data, file.ContentType, file.Content); }
		[HttpPost("RemoveWorkOrderFile")]
		public async Task<IActionResult> RemoveWorkOrderFile([FromBody] WorkOrderCommandInput input) { Required(input); await _orders.WithdrawFileAsync(Actor, input.Id, input.ChildId, input.Revision, input.Note); return Reply(await _orders.GetAsync(Actor, input.Id)); }
	}
}
