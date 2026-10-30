using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Model.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Middleware
{
	/// <summary>
	/// Middleware that enforces 2FA enrollment: for admin users when the department has
	/// <c>Require2FAForAdmins</c> enabled, and for every member when the department's security policy has
	/// <c>RequireMfa</c> and its rollout gate is on (passkey plan section 7.6 rows 1 and 5). A user who must
	/// enroll is redirected to the 2FA setup page and cannot reach any other page, or the Web API bridge, until
	/// enrolled: the restricted setup of plan section 6.2. A script call is answered 403 instead of redirected.
	/// </summary>
	public class Require2FAEnrollmentMiddleware
	{
		private readonly RequestDelegate _next;

		// Paths that are always allowed through (enrollment, logout, static assets)
		private static readonly string[] AllowedPaths =
		{
			"/User/TwoFactor",
			// Starting authenticator setup can require confirming the password first.
			"/User/AccountSecurity/Reauthenticate",
			// Or, for a member who signs in through the department's identity provider, reauthenticating there: the round trip
			// starts and ends on these, and completes only what this session began (passkey plan section 6.2).
			"/Account/SsoSessionBegin",
			"/Account/SsoReturn",
			// A shared workstation's lock screen, Lock, End shift and unlock (the provider's too), so the member can always hand
			// over or resume (plan section 12.5.3).
			"/SharedSession/",
			"/Account/SsoUnlockBegin",
			"/Account/LogOff",
			"/Account/LogOn",
			"/favicon.ico",
			"/css/",
			"/js/",
			"/lib/",
			"/fonts/",
			"/images/",
			"/img/"
		};

		public Require2FAEnrollmentMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			// Only intercept authenticated requests
			if (context.User?.Identity?.IsAuthenticated != true)
			{
				await _next(context);
				return;
			}

			// Skip allowed paths (enrollment UI, logout, static files)
			var path = context.Request.Path.Value ?? string.Empty;
			if (AllowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
			{
				await _next(context);
				return;
			}

			// Resolve scoped services from request scope
			var userManager = context.RequestServices.GetService<UserManager<IdentityUser>>();
			var departmentsService = context.RequestServices.GetService<IDepartmentsService>();
			var departmentSettingsService = context.RequestServices.GetService<IDepartmentSettingsService>();
			var departmentGroupsService = context.RequestServices.GetService<IDepartmentGroupsService>();
			var mfaPolicyService = context.RequestServices.GetService<IMfaPolicyService>();

			if (userManager == null || departmentsService == null || departmentSettingsService == null)
			{
				await _next(context);
				return;
			}

			var user = await userManager.GetUserAsync(context.User);
			if (user == null)
			{
				await _next(context);
				return;
			}

			async Task<bool> mustEnroll()
			{
				// Enrolled users are never redirected, so check that first and skip the department lookups.
				if (await userManager.GetTwoFactorEnabledAsync(user))
					return false;

				var department = await departmentsService.GetDepartmentByUserIdAsync(user.Id);
				if (department == null)
					return false;

				// Department RequireMfa applies to every member, not only administrators.
				if (mfaPolicyService != null && await mfaPolicyService.IsRequireMfaEnforcedAsync(department.DepartmentId, context.RequestAborted))
					return true;

				var scope = await departmentSettingsService.GetRequire2FAForAdminsAsync(department.DepartmentId);
				if (scope == 0)
					return false;

				// Determine if user is in scope
				bool inScope = false;
				bool isDeptAdmin = department.IsUserAnAdmin(user.Id);
				bool isManagingUser = department.ManagingUserId == user.Id;

				if (scope >= 1 && (isDeptAdmin || isManagingUser))
					inScope = true;

				if (scope >= 2 && !inScope && departmentGroupsService != null)
				{
					var group = await departmentGroupsService.GetGroupForUserAsync(user.Id, department.DepartmentId);
					if (group != null && group.IsUserGroupAdmin(user.Id))
						inScope = true;
				}

				return inScope;
			}

			// Only the enrollment check fails open. _next must stay outside this try: swallowing a
			// downstream exception and falling through re-ran the whole request (duplicate side effects,
			// "Headers are read-only" once the first run had started the response, the real error lost).
			bool redirectToEnrollment;
			try
			{
				redirectToEnrollment = await mustEnroll();
			}
			catch
			{
				redirectToEnrollment = false;
			}

			if (redirectToEnrollment)
			{
				if (IsScriptRequest(context.Request))
				{
					// A script would follow the redirect and get the whole setup page as its answer: the layout's
					// $('#top-icons-area').load() put it, scripts and all, inside the setup page, whose scripts loaded it
					// again (the page went blank), and every load staged a new authenticator key, so the QR code on screen
					// stopped matching. No step-up header either: the setup page's ajaxError handler would reload it. The
					// handler reads mfa_enrollment_required instead and navigates only from a page this middleware would have
					// redirected (one left open when enforcement started), never from one it lets through: from Reauthenticate,
					// which Enable2FA sends a stale session to, going back to setup would loop.
					var pathBase = context.Request.PathBase.Value ?? string.Empty;
					context.Response.StatusCode = StatusCodes.Status403Forbidden;
					await context.Response.WriteAsJsonAsync(new
					{
						success = false,
						error = "mfa_enrollment_required",
						redirect = $"{pathBase}{EnrollmentPath}",
						allowedPaths = AllowedPaths.Select(p => pathBase + p)
					});
					return;
				}

				// Admin has not enrolled — redirect to setup
				context.Response.Redirect(EnrollmentPath);
				return;
			}

			await _next(context);
		}

		internal const string EnrollmentPath = "/User/TwoFactor/Enable2FA?enforced=1";

		/// <summary>
		/// A jQuery or fetch() call from a page rather than a page load. Only a positive signal counts, so a request that says
		/// nothing about itself still gets the redirect.
		/// </summary>
		internal static bool IsScriptRequest(HttpRequest request)
		{
			var mode = request.Headers["Sec-Fetch-Mode"].ToString();
			if (!string.IsNullOrWhiteSpace(mode))
				return !string.Equals(mode, "navigate", StringComparison.OrdinalIgnoreCase);

			return Resgrid.Web.Helpers.StepUpFormReplay.IsScriptRequest(request);
		}
	}
}



