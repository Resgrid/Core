using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// MVC twin of the API department-operation-lock gate (ADP plan section 20.2): while a
	/// department's lock is active, authenticated mutating requests (POST/PUT/PATCH/DELETE) for
	/// that department are refused with 423 Locked and a plain explanation; GET/reads continue so
	/// every page stays viewable. Department identity comes from the signed authentication state
	/// (ClaimTypes.PrimaryGroupSid), never the form body. Opt out with
	/// <see cref="AllowDuringDepartmentLockAttribute"/> for auth, billing, and the lock-abort path.
	/// Fails OPEN on a lock-store fault — dispatch availability beats migration progress.
	/// </summary>
	public sealed class DepartmentLockActionFilter : IAsyncActionFilter
	{
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			var method = context.HttpContext.Request.Method;
			var isMutation = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
							 HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

			var departmentClaim = context.HttpContext.User?.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value;

			if (!isMutation || HasAllowAttribute(context) ||
				!int.TryParse(departmentClaim, out var departmentId) || departmentId <= 0)
			{
				await next();
				return;
			}

			var lockService = context.HttpContext.RequestServices.GetService<IDepartmentLockService>();

			var isLocked = false;
			string reason = null;
			if (lockService != null)
			{
				try
				{
					isLocked = await lockService.IsDepartmentLockedAsync(departmentId);
					if (isLocked)
						reason = (await lockService.GetActiveLockAsync(departmentId))?.Reason;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"DepartmentLockActionFilter (web) failed for department {departmentId}; failing open");
					isLocked = false;
				}
			}

			if (!isLocked)
			{
				await next();
				return;
			}

			context.Result = new ContentResult
			{
				StatusCode = StatusCodes.Status423Locked,
				ContentType = "text/plain",
				Content = reason ?? "A maintenance operation is in progress; data entry is paused. Viewing is unaffected — please try again after the maintenance window."
			};
		}

		private static bool HasAllowAttribute(ActionExecutingContext context)
		{
			// This app runs legacy MVC routing (EnableEndpointRouting = false), where
			// EndpointMetadata is not reliably populated — read the attributes off the action and
			// controller directly.
			if (context.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor descriptor)
			{
				return descriptor.MethodInfo.GetCustomAttributes(typeof(AllowDuringDepartmentLockAttribute), inherit: true).Any() ||
					   descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(AllowDuringDepartmentLockAttribute), inherit: true).Any();
			}

			return false;
		}
	}
}
