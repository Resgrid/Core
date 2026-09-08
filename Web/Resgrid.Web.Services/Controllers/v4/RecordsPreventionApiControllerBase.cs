using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Shared gate for the RMS-5 prevention/investigation and RMS-4 quality/health surfaces: the Records flag first, member
	/// principals only (a system principal's department-scoped Record_View grant covers Records, never prevention data or
	/// investigation cases), and one exception-to-status mapping so every module answers the same way.
	/// </summary>
	public abstract class RecordsPreventionApiControllerBase : V4AuthenticatedApiControllerbase, IActionFilter
	{
		protected readonly IRecordsCutoverService Cutover;

		protected RecordsPreventionApiControllerBase(IRecordsCutoverService cutover)
		{
			Cutover = cutover;
		}

		public void OnActionExecuting(ActionExecutingContext context)
		{
			if (RecordsSystemPrincipal.IsSystemPrincipal(User))
				context.Result = Problem(statusCode: StatusCodes.Status403Forbidden, title: "Prevention and investigation endpoints accept member principals only.", type: "record_prevention_member_only");
		}

		public void OnActionExecuted(ActionExecutedContext context)
		{
		}

		protected async Task<bool> FlagOnAsync() => (await Cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled;

		protected string ClientIp => HttpContext?.Connection?.RemoteIpAddress?.ToString();

		protected T Done<T>(T result) where T : StandardApiResponseV4Base
		{
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		protected ActionResult Fail(Exception ex)
		{
			switch (ex)
			{
				case UnauthorizedAccessException _: return Forbid();
				case RecordsModuleDisabledException _: return NotFound();
				case RecordProtectedContentException protectedContent: return Problem(statusCode: StatusCodes.Status403Forbidden, title: protectedContent.Message, type: "record_protected_content", detail: protectedContent.Reason);
				case RecordAttachmentRejectedException rejected: return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: rejected.Message, type: "record_attachment_rejected");
				case ArgumentException argument: return Problem(statusCode: StatusCodes.Status400BadRequest, title: argument.Message, type: "record_prevention_validation");
				case InvalidOperationException invalid: return Problem(statusCode: StatusCodes.Status409Conflict, title: invalid.Message, type: "record_prevention_state");
				default: throw ex;
			}
		}
	}
}
