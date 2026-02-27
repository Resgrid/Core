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
	/// Middleware that enforces 2FA enrollment for admin users when the department has
	/// <c>Require2FAForAdmins</c> enabled. If an in-scope admin has not yet enrolled,
	/// they are redirected to the 2FA setup page and cannot access any other page until enrolled.
	/// </summary>
	public class Require2FAEnrollmentMiddleware
	{
		private readonly RequestDelegate _next;

		// Paths that are always allowed through (enrollment, logout, static assets)
		private static readonly string[] AllowedPaths =
		{
			"/User/TwoFactor",
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

			try
			{
				var department = await departmentsService.GetDepartmentByUserIdAsync(user.UserName);
				if (department == null)
				{
					await _next(context);
					return;
				}

				var scope = await departmentSettingsService.GetRequire2FAForAdminsAsync(department.DepartmentId);
				if (scope == 0)
				{
					await _next(context);
					return;
				}

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

				if (inScope && !await userManager.GetTwoFactorEnabledAsync(user))
				{
					// Admin has not enrolled — redirect to setup
					context.Response.Redirect("/User/TwoFactor/Enable2FA?enforced=1");
					return;
				}
			}
			catch
			{
				// Swallow any error to avoid breaking the pipeline — fail open
			}

			await _next(context);
		}
	}
}



