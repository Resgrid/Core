using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Checklists;

namespace Resgrid.Web.Services.Controllers.v4
{
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[RequestSizeLimit(1024 * 1024)]
	public abstract class ChecklistApiControllerBase : V4AuthenticatedApiControllerbase, IAsyncActionFilter
	{
		protected readonly IChecklistsService Checklists;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> _strings;
		protected ChecklistApiControllerBase(IChecklistsService checklists, IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> strings) { Checklists = checklists; _strings = strings; }
		protected ChecklistActor Actor => new ChecklistActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = Request.Headers[DataProtectionController.GrantHeader].ToString() };
		protected OkObjectResult Reply<T>(T data, int count = 1, bool hasMore = false)
		{
			var result = new ChecklistApiResult<T> { Data = data, Status = ResponseHelper.Success, PageSize = count, HasMore = hasMore };
			ResponseHelper.PopulateV4ResponseData(result); return Ok(result);
		}
		protected static T Required<T>(T input) where T : class => input ?? throw new ChecklistException(400, "The form content is invalid.");
		[NonAction]
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			// Framework binding errors can quote the supplied value (including free text).
			// Keep malformed JSON/model errors under the same value-free localized contract.
			if (!context.ModelState.IsValid)
			{
				context.Result = Failure(new ChecklistException(400, "The form content is invalid."));
				return;
			}
			var executed = await next();
			if (executed.Exception is ChecklistException ex)
			{
				executed.ExceptionHandled = true;
				executed.Result = Failure(ex);
			}
		}
		private ObjectResult Failure(ChecklistException ex)
		{
			var protectedData = ex.StatusCode == 403 && (ex.Message.Contains("protected", StringComparison.OrdinalIgnoreCase) || ex.Message.StartsWith("Unlock", StringComparison.Ordinal));
			var code = protectedData ? "protected_data_required" : ex.StatusCode == 409 ? "checklist_conflict" : ex.StatusCode == 403 ? "checklist_forbidden" : ex.StatusCode == 404 ? "checklist_unavailable" : "checklist_validation";
			// The protected-data message is a routing sentinel the MVC controllers match on, not a resource key.
			// Look the localized text up by key so the caller gets a real message instead of the generic fallback.
			var message = _strings[protectedData ? "ProtectedDataRequired" : ex.Message];
			// Do not return validation excerpts containing item names, entered values or envelopes.
			var problem = new ProblemDetails { Status = ex.StatusCode, Type = code, Title = message.ResourceNotFound ? _strings["The request could not be completed."].Value : message.Value };
			problem.Extensions["IsRedacted"] = protectedData;
			return new ObjectResult(problem) { StatusCode = ex.StatusCode };
		}
	}
}
