using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Web.Middleware
{
	/// <summary>
	/// Keeps a session that must choose a new password on the change page. A password sign-in records the department that requires
	/// it (an administrator flagged the member, or the department's password expiration passed) as a claim on the new session; until
	/// the password is changed, which ends every session, no other page or the Web API bridge answers that session. The claim lives
	/// in the server-side ticket, so the browser can neither add nor drop it. A script call is answered 403 instead of redirected.
	/// </summary>
	public class RequirePasswordChangeMiddleware
	{
		/// <summary>The claim a password sign-in adds when a department requires a new password; its value is that department's id.</summary>
		public const string ClaimType = "pwd_change_required";

		public const string ChangePath = "/Account/ForcePasswordChange";

		private readonly RequestDelegate _next;

		// Paths that are always allowed through (the change page, signing out, static assets)
		private static readonly string[] AllowedPaths =
		{
			ChangePath,
			"/Account/LogOff",
			"/Account/LogOn",
			"/Account/SetLanugage",
			// A shared workstation's lock screen, Lock and End shift, so the member can always hand over (plan section 12.5.3).
			"/SharedSession/",
			"/favicon.ico",
			"/css/",
			"/js/",
			"/lib/",
			"/clib/",
			"/fonts/",
			"/images/",
			"/img/"
		};

		public RequirePasswordChangeMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			if (context.User?.Identity?.IsAuthenticated != true || !context.User.HasClaim(c => c.Type == ClaimType))
			{
				await _next(context);
				return;
			}

			var path = context.Request.Path.Value ?? string.Empty;
			if (AllowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
			{
				await _next(context);
				return;
			}

			var pathBase = context.Request.PathBase.Value ?? string.Empty;
			if (Require2FAEnrollmentMiddleware.IsScriptRequest(context.Request))
			{
				// A script would follow a redirect and put the whole change page where it expected its answer.
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				await context.Response.WriteAsJsonAsync(new
				{
					success = false,
					error = "password_change_required",
					redirect = $"{pathBase}{ChangePath}"
				});
				return;
			}

			context.Response.Redirect($"{pathBase}{ChangePath}");
		}
	}
}
