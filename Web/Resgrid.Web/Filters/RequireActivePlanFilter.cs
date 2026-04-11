using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Config;
using Resgrid.Model.Services;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Global action filter that enforces paid plan selection for new accounts when
	/// <see cref="SystemBehaviorConfig.RequirePlanSelectionDuringSignup"/> is enabled.
	/// Authenticated users with no active paid plan are redirected to SelectRegistrationPlan.
	/// </summary>
	public class RequireActivePlanFilter : IAsyncActionFilter
	{
		// Actions that are always reachable regardless of plan status
		private static readonly string[] ExemptControllers = new[]
		{
			"account",
			"subscription"
		};

		private static readonly string[] ExemptActions = new[]
		{
			"topiconsarea",
			"getsearchresults"
		};

		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			// Feature is off — skip entirely
			if (!SystemBehaviorConfig.RequirePlanSelectionDuringSignup)
			{
				await next();
				return;
			}

			// Only enforce for authenticated users in the User area
			var user = context.HttpContext.User;
			if (user?.Identity == null || !user.Identity.IsAuthenticated)
			{
				await next();
				return;
			}

			var routeData = context.RouteData;
			var area = routeData.Values["area"]?.ToString();
			if (!string.Equals(area, "User", StringComparison.OrdinalIgnoreCase))
			{
				await next();
				return;
			}

			// Skip exempt controllers (e.g. Account)
			var controller = routeData.Values["controller"]?.ToString() ?? string.Empty;
			foreach (var exempt in ExemptControllers)
			{
				if (string.Equals(controller, exempt, StringComparison.OrdinalIgnoreCase))
				{
					await next();
					return;
				}
			}

			// Skip exempt actions (payment flow, plan selection itself)
			var action = routeData.Values["action"]?.ToString() ?? string.Empty;
			foreach (var exempt in ExemptActions)
			{
				if (string.Equals(action, exempt, StringComparison.OrdinalIgnoreCase))
				{
					await next();
					return;
				}
			}

			// Resolve services and check plan status
			var services = context.HttpContext.RequestServices;
			var subscriptionsService = services.GetService<ISubscriptionsService>();
			if (subscriptionsService == null)
			{
				await next();
				return;
			}

			var deptClaim = context.HttpContext.User.FindFirst(ClaimTypes.PrimaryGroupSid);
			if (deptClaim == null || !int.TryParse(deptClaim.Value, out int departmentId) || departmentId <= 0)
			{
				await next();
				return;
			}

			try
			{
				var payment = await subscriptionsService.GetCurrentPaymentForDepartmentAsync(departmentId);
				if (payment == null || payment.IsFreePlan())
				{
					context.Result = new RedirectToRouteResult(new Microsoft.AspNetCore.Routing.RouteValueDictionary
					{
						{ "area", "User" },
						{ "controller", "Subscription" },
						{ "action", "SelectRegistrationPlan" }
					});
					return;
				}
			}
			catch
			{
				// Fail open on billing API errors so legitimate paid users are not locked out
			}

			await next();
		}
	}
}
