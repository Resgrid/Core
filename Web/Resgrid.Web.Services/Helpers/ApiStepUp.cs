using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// The one v4 <c>step_up_required</c> refusal (owner decision 2026-11-26): HTTP 403, an RFC 7807 body whose <c>type</c> is
	/// <c>step_up_required</c>, extended with the department area, whether it is required, the allowed kinds of proof, the kinds this member
	/// can use, their usable second factors and the window; plus the <c>X-Resgrid-Step-Up</c> header naming the area. Every value comes from
	/// the server; nothing in it is a secret. The app completes the step-up with <c>StepUp/Verify</c> and retries the same request.
	/// </summary>
	public static class ApiStepUp
	{
		public const string ProblemType = "step_up_required";

		/// <summary>The response header naming the area a refusal is for.</summary>
		public const string AreaHeader = "X-Resgrid-Step-Up";

		/// <summary>The client application of the validated session (the API's own when it has none).</summary>
		public static UserSessionClientApplication ClientOf(HttpContext httpContext) =>
			(UserSessionClientApplication)(HttpProtectedGrantContext.SessionOf(httpContext)?.ClientApplication ?? (int)UserSessionClientApplication.Api);

		public static async Task<ObjectResult> RefuseAsync(HttpContext httpContext, IStepUpCapabilityService capabilities, int departmentId, string userId,
			StepUpArea area, CancellationToken cancellationToken = default)
		{
			StepUpRequirement requirement;
			try
			{
				requirement = await capabilities.DescribeAsync(departmentId, userId, ClientOf(httpContext), area, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The refusal stands without its advisory detail.
				Logging.LogException(ex, "The step-up requirement could not be described.");
				requirement = StepUpRequirement.For(StepUpRule.Default(area, Config.TwoFactorConfig.StepUpVerificationWindowMinutes), null);
			}

			return Refusal(httpContext, requirement);
		}

		/// <summary>The refusal for a described requirement (pure apart from the header, for tests).</summary>
		public static ObjectResult Refusal(HttpContext httpContext, StepUpRequirement requirement)
		{
			var areaName = StepUpAreas.Name(requirement.Area);
			if (httpContext?.Response != null)
			{
				httpContext.Response.Headers[AreaHeader] = areaName;
				httpContext.Response.Headers["X-Resgrid-Reason"] = ProblemType;
				httpContext.Response.Headers["Cache-Control"] = "no-store";
			}

			var problem = new ProblemDetails
			{
				Type = ProblemType,
				Title = requirement.CanComplete
					? "Verify it is you to continue."
					: "Verify it is you to continue. Set up two-factor authentication first: no verification this organization allows is available to you.",
				Status = StatusCodes.Status403Forbidden
			};
			problem.Extensions["area"] = areaName;
			problem.Extensions["required"] = requirement.Required;
			problem.Extensions["allowedMethods"] = requirement.AllowedMethods;
			problem.Extensions["usableMethods"] = requirement.UsableMethods;
			problem.Extensions["mfaMethods"] = requirement.MfaMethods;
			problem.Extensions["windowMinutes"] = requirement.WindowMinutes;
			problem.Extensions["canComplete"] = requirement.CanComplete;

			return new ObjectResult(problem)
			{
				StatusCode = StatusCodes.Status403Forbidden,
				ContentTypes = { "application/problem+json" }
			};
		}
	}
}
