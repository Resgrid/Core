using System;
using System.IO;
using System.Linq;
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
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Checklists;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	public partial class ChecklistsController : SecureBaseController
	{
		private readonly IChecklistTemplateService _templates;
		private readonly IChecklistsService _checklists;
		private readonly IReadinessAccessService _access;
		private readonly IProtectedGrantContext _grant;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> _strings;
		private readonly Lazy<IWorkShiftsService> _workshifts;
		private readonly Lazy<IDepartmentsService> _departments;
		public ChecklistsController(IChecklistTemplateService templates, IChecklistsService checklists, IReadinessAccessService access, IProtectedGrantContext grant, IDepartmentDataProtectionService protection, IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> strings, Lazy<IWorkShiftsService> workshifts = null, Lazy<IDepartmentsService> departments = null)
		{ _templates = templates; _checklists = checklists; _access = access; _grant = grant; _protection = protection; _strings = strings; _workshifts = workshifts; _departments = departments; }
		private ChecklistActor Actor => new ChecklistActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = _grant.GrantToken };
		private Task<bool> _checklistsEnabled;
		private Task<bool> ChecklistsEnabledAsync() => _checklistsEnabled ??= _access.CanUseChecklistsAsync(DepartmentId);
		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			ViewBag.ProtectionEnforced = await _protection.IsProtectionEnforcedAsync(DepartmentId);
			ViewBag.ProtectedGrant = _grant.GrantToken;
			ViewBag.GrantExpiresOn = HttpProtectedGrantContext.ReadExpiry(Request);
			ViewBag.ChecklistUserId = UserId;
			ViewBag.ChecklistsEnabled = await ChecklistsEnabledAsync();
			var executed = await next();
			if (executed.Exception is ChecklistException ex)
			{
				executed.ExceptionHandled = true;
				if (HttpMethods.IsGet(Request.Method) && ex.StatusCode == 403 && ex.Message.StartsWith("Unlock protected", StringComparison.Ordinal))
					executed.Result = View("Locked", new ChecklistLockedView { Page = context.RouteData.Values["action"]?.ToString() == "EditSchedule" && Request.Query.ContainsKey("definitionId") ? "NewSchedule" : context.RouteData.Values["action"]?.ToString(), Id = Request.Query["id"].ToString() is { Length: > 0 } queryId ? queryId : Request.Query["definitionId"].ToString() is { Length: > 0 } definitionId ? definitionId : context.RouteData.Values["id"]?.ToString() });
				else { var message = _strings[ex.Message]; executed.Result = StatusCode(ex.StatusCode, new { message = message.ResourceNotFound ? _strings["The request could not be completed."].Value : message.Value }); }
			}
		}
		[HttpGet]
		public async Task<IActionResult> Index(int page = 0)
		{
			var rows = await _checklists.ListAsync(Actor, page, includeNext: true);
			return View("Index", new ChecklistIndexView { Definitions = rows.Take(50).ToList(), HasMore = rows.Count > 50,
				CanManage = await _checklists.CanManageAsync(Actor) && await ChecklistsEnabledAsync(), Page = page });
		}
		[HttpGet]
		public async Task<IActionResult> Templates(string query = null)
		{
			if (query?.Length > 256) return BadRequest();
			var templates = await _templates.SearchAsync(DepartmentId, query);
			if (templates == null) return NotFound();
			return View(new ChecklistTemplatesView { Templates = templates, Query = query });
		}
		[HttpGet]
		public async Task<IActionResult> Template(string id)
		{
			if (string.IsNullOrWhiteSpace(id) || id.Length > 80) return NotFound();
			var template = await _templates.GetByIdAsync(DepartmentId, id);
			if (template == null) return NotFound();
			return View(template);
		}
		[HttpGet, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> New(string templateId = null)
		{
			if (!await ChecklistsEnabledAsync() || !await _checklists.CanManageAsync(Actor)) return NotFound();
			var assetsAvailable = await _checklists.AssetTargetsAvailableAsync(Actor);
			var form = new ChecklistForm { Name = "", Sections = { new ChecklistSection { Name = _strings["Checks"].Value, Items = { new ChecklistItem { Name = "" } } } } };
			if (templateId != null)
			{
				var template = await _templates.GetByIdAsync(DepartmentId, templateId);
				if (template == null) return NotFound();
				form = ChecklistForm.FromTemplate(template, assetsAvailable);
			}
			return View("Edit", new ChecklistEditView { Form = form, AssetsAvailable = assetsAvailable });
		}
		[HttpGet, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> Edit(string id)
		{
			if (!await _checklists.CanManageAsync(Actor)) return Forbid();
			if (!await ChecklistsEnabledAsync()) return NotFound();
			var row = await _checklists.GetDefinitionAsync(Actor, id);
			return View("Edit", new ChecklistEditView { AssetsAvailable = await _checklists.AssetTargetsAvailableAsync(Actor), Id = id, Revision = row.Definition.Revision, Form = row.Form });
		}
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> SaveDefinition(string id, int revision, string formJson)
		{
			var form = Parse<ChecklistForm>(formJson);
			var saved = await _checklists.SaveDefinitionAsync(Actor, id, revision, form);
			return Json(new { url = Url.Action("Detail", new { id = saved }) });
		}
		private static T Parse<T>(string json) where T : class
		{
			if (string.IsNullOrWhiteSpace(json) || json.Length > 1000000) throw new ChecklistException(400, "Form content is missing or too large.");
			try { return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings { MaxDepth = 20, TypeNameHandling = TypeNameHandling.None }) ?? throw new ChecklistException(400, "The form content is invalid."); }
			catch (JsonException) { throw new ChecklistException(400, "The form content is invalid."); }
		}
		[HttpGet]
		public async Task<IActionResult> Detail(string id, int page = 0)
		{
			var row = await _checklists.GetDefinitionAsync(Actor, id);
			var canStart = await ChecklistsEnabledAsync() && !row.Definition.Retired && row.Definition.CurrentVersionId != null;
			var history = await _checklists.HistoryAsync(Actor, id, page, includeNext: true);
			return View("Detail", new ChecklistDetailView { Definition = row, History = history.Take(50).ToList(), HasMore = history.Count > 50, CanManage = await _checklists.CanManageAsync(Actor) && await ChecklistsEnabledAsync(), CanStart = canStart,
				Targets = canStart ? await _checklists.TargetsAsync(Actor, row.PublishedForm.TargetType) : new System.Collections.Generic.List<ChecklistTarget>(), Page = page });
		}
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> Publish(string id, int revision) { await _checklists.PublishAsync(Actor, id, revision); return RedirectToAction("Detail", new { id }); }
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> Retire(string id, int revision, bool delete = false) { await _checklists.RetireAsync(Actor, id, revision, delete); return RedirectToAction(delete ? "Index" : "Detail", new { id }); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Start(string id, string targetId, string completionId)
		{ var run = await _checklists.StartAsync(Actor, id, targetId, completionId); return RedirectToAction("Run", new { id = run }); }
		[HttpGet]
		public async Task<IActionResult> Run(string id)
		{
			var run = await _checklists.GetRunAsync(Actor, id);
			return View(run.Completion.State == (int)ChecklistRunState.InProgress && run.Completion.CreatedBy == UserId && await ChecklistsEnabledAsync() ? "Run" : "CompletionDetail", run);
		}
		[HttpGet]
		public Task<IActionResult> CompletionDetail(string id) => Run(id);
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveRun(string id, string inputJson, bool submit)
		{ var revision = await _checklists.SaveRunAsync(Actor, id, Parse<ChecklistRunInput>(inputJson), submit); return Json(new { revision, url = submit ? Url.Action("CompletionDetail", new { id }) : null }); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Witness(string id, string submissionHash, string attestation)
		{ await _checklists.WitnessAsync(Actor, id, submissionHash, attestation); return RedirectToAction("CompletionDetail", new { id }); }
		[HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(11 * 1024 * 1024)]
		public async Task<IActionResult> Upload(string id, string itemId, IFormFile file)
		{
			if (file == null || file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "Choose an evidence image up to 10 MB." });
			using var stream = new MemoryStream(); await file.CopyToAsync(stream);
			await _checklists.AddFileAsync(Actor, id, itemId, file.FileName, file.ContentType, stream.ToArray());
			var run = await _checklists.GetRunAsync(Actor, id); return Json(new { revision = run.Completion.Revision, files = run.Files });
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveFile(string id, string completionId)
		{ await _checklists.DeleteFileAsync(Actor, id); var run = await _checklists.GetRunAsync(Actor, completionId); return Json(new { revision = run.Completion.Revision, files = run.Files }); }
		[HttpGet]
		public async Task<IActionResult> Evidence(string id)
		{ var file = await _checklists.GetFileAsync(Actor, id); Response.Headers["X-Content-Type-Options"] = "nosniff"; return File(file.Data, file.ContentType, file.Content); }
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Export(string id)
		{ var run = await _checklists.GetRunAsync(Actor, id); return File(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(run, Formatting.Indented)), "application/json", "checklist-" + run.Completion.Id + ".json"); }
		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Reopen(string page, string id) => page switch
		{
			"Index" => Index(), "Detail" => Detail(id), "Edit" => Edit(id), "Run" => Run(id), "CompletionDetail" => CompletionDetail(id),
			"Schedules" => Schedules(id), "EditSchedule" => EditSchedule(id), "NewSchedule" => EditSchedule(definitionId: id), "Due" => Due(),
			"Occurrence" => Occurrence(id), "Reminders" => Reminders(),
			_ => Task.FromResult<IActionResult>(BadRequest())
		};
	}
}
