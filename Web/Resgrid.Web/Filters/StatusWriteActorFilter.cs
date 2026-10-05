using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Opens a <see cref="StatusWriteActor"/> scope for every signed-in website request, so a unit state or personnel status
	/// set from the website (a dispatcher's console, the personnel or units list, the dashboard) records who set it (M0260).
	/// </summary>
	public sealed class StatusWriteActorFilter : IAsyncActionFilter
	{
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			var userId = context.HttpContext.User?.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (string.IsNullOrWhiteSpace(userId))
			{
				await next();
				return;
			}

			using (StatusWriteActor.Begin(userId, StatusSetOrigins.Web))
				await next();
		}
	}
}
