using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;

namespace Resgrid.Web.Services.Filters
{
	/// <summary>
	/// A Records module that is not enabled for the department is a message for the caller, not a server fault. The prevention
	/// controllers map it themselves (RecordsPreventionApiControllerBase.Fail); any other endpoint that lets
	/// <see cref="RecordsModuleDisabledException"/> escape answers 404 with the message instead of a 500.
	/// </summary>
	public sealed class RecordsModuleDisabledExceptionFilter : IExceptionFilter
	{
		public void OnException(ExceptionContext context)
		{
			if (!(context.Exception is RecordsModuleDisabledException exception))
				return;

			context.Result = new ObjectResult(new ProblemDetails
			{
				Status = StatusCodes.Status404NotFound,
				Title = exception.Message,
				Type = "records_module_disabled"
			})
			{ StatusCode = StatusCodes.Status404NotFound };
			context.ExceptionHandled = true;
		}
	}
}
