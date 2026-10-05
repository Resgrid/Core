using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;
using Resgrid.Model.Security;

namespace Resgrid.Web.Services.Filters
{
	/// <summary>
	/// Opens a <see cref="StatusWriteActor"/> scope for every signed-in API request, so a unit state or personnel status the
	/// request saves records who submitted it and from which app (M0260). The app comes from the session's
	/// <c>client_app</c> claim, which the app asserted at sign-in; a token without it reads as a plain API client.
	/// Anonymous endpoints (SMS, voice) open their own scopes.
	/// </summary>
	public sealed class StatusWriteActorFilter : IAsyncActionFilter
	{
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			var user = context.HttpContext.User;
			var userId = user?.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (string.IsNullOrWhiteSpace(userId))
			{
				await next();
				return;
			}

			UserSessionClientApplication? application = null;
			if (int.TryParse(user.FindFirst(SessionClaimTypes.ClientApp)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
				&& System.Enum.IsDefined(typeof(UserSessionClientApplication), value))
				application = (UserSessionClientApplication)value;

			using (StatusWriteActor.Begin(userId, StatusSetOriginsExtensions.FromClientApplication(application)))
				await next();
		}
	}
}
