using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Filters
{
	/// <summary>
	/// Global department-operation-lock mutation gate (ADP plan section 20.2). While a department's
	/// lock is active, every authenticated mutating request (POST/PUT/PATCH/DELETE) for that
	/// department is refused with 423 Locked and problem type "department_locked"; reads always
	/// continue. Department identity comes from the signed authentication state
	/// (ClaimTypes.PrimaryGroupSid), never from the request body, so a caller cannot dodge the gate
	/// by naming another department. Endpoints that must work during a lock (auth/session flows,
	/// status reads over POST, the lock-abort path) opt out with
	/// <see cref="AllowDuringDepartmentLockAttribute"/>.
	///
	/// Failure posture matches IDepartmentLockService: a lock-store outage fails OPEN — the lock
	/// protects a migration, but dispatch availability beats migration progress, and the migration
	/// worker separately refuses to proceed when it cannot verify its own lock.
	/// </summary>
	public sealed class DepartmentLockActionFilter : IAsyncActionFilter
	{
		public const string ProblemType = "department_locked";

		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			var method = context.HttpContext.Request.Method;
			var isMutation = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
							 HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

			// Department from the signed authentication state only. Anonymous endpoints (webhooks,
			// auth) have no department claim and pass through — their department-scoped work happens
			// in queue consumers, which enforce the lock at the consumer (section 20.2).
			var departmentClaim = context.HttpContext.User?.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value;

			if (!isMutation || HasAllowAttribute(context) ||
				!int.TryParse(departmentClaim, out var departmentId) || departmentId <= 0)
			{
				await next();
				return;
			}

			var lockService = context.HttpContext.RequestServices?.GetService(typeof(IDepartmentLockService)) as IDepartmentLockService;

			var isLocked = false;
			DepartmentOperationLock activeLock = null;
			if (lockService != null)
			{
				try
				{
					isLocked = await lockService.IsDepartmentLockedAsync(departmentId);
					if (isLocked)
						activeLock = await lockService.GetActiveLockAsync(departmentId);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"DepartmentLockActionFilter failed for department {departmentId}; failing open");
					isLocked = false;
				}
			}

			if (!isLocked)
			{
				await next();
				return;
			}

			context.Result = new ObjectResult(new ProblemDetails
			{
				Type = ProblemType,
				Title = "Department is temporarily locked",
				Detail = activeLock?.Reason ?? "A maintenance operation is in progress; data entry is paused. Reads remain available.",
				Status = StatusCodes.Status423Locked,
				Extensions =
				{
					["projectedEndUtc"] = activeLock?.ProjectedEndUtc,
					["lockType"] = activeLock?.LockType
				}
			})
			{
				StatusCode = StatusCodes.Status423Locked
			};
		}

		private static bool HasAllowAttribute(ActionExecutingContext context)
		{
			return context.ActionDescriptor.EndpointMetadata?.OfType<AllowDuringDepartmentLockAttribute>().Any() == true;
		}
	}
}
