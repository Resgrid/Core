using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Config;
using Resgrid.Model.Services;
using Resgrid.Model.TwoFactor;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Attributes
{
	/// <summary>
	/// Action filter that enforces a recent step-up 2FA verification before accessing sensitive operations.
	/// <para>
	/// All enforcement decisions are delegated to <see cref="TwoFactorEnforcementEvaluator.Evaluate"/>;
	/// this filter is responsible only for the ASP.NET plumbing (resolving services, reading the session,
	/// and writing the HTTP result).
	/// </para>
	/// <list type="bullet">
	///   <item><see cref="TwoFactorEnforcementOutcome.NotRequired"/> — pass through.</item>
	///   <item><see cref="TwoFactorEnforcementOutcome.EnrollmentRequired"/> — redirect to enrollment.</item>
	///   <item><see cref="TwoFactorEnforcementOutcome.StepUpRequired"/> — redirect to Verify2FA.</item>
	/// </list>
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
	public sealed class RequiresRecentTwoFactorAttribute : Attribute, IAsyncActionFilter
	{
		/// <summary>
		/// Session key whose value is a pipe-delimited string of the form "{userId}|{verifiedAtUtcRoundtrip}".
		/// Binding the step-up proof to the user id prevents one user from inheriting another user's proof
		/// within a shared session store.
		/// </summary>
		internal const string StepUpSessionKey = "Resgrid2FAVerifiedAt";

		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			var claimsPrincipal = context.HttpContext.User;

			if (claimsPrincipal?.Identity == null || !claimsPrincipal.Identity.IsAuthenticated)
			{
				await next();
				return;
			}

			var services = context.HttpContext.RequestServices;
			var userManager = services.GetService<UserManager<IdentityUser>>();
			var departmentsService = services.GetService<IDepartmentsService>();
			var departmentSettingsService = services.GetService<IDepartmentSettingsService>();
			var departmentGroupsService = services.GetService<IDepartmentGroupsService>();

			if (userManager == null || departmentsService == null || departmentSettingsService == null)
			{
				await next();
				return;
			}

			var identityUser = await userManager.GetUserAsync(claimsPrincipal);
			if (identityUser == null)
			{
				await next();
				return;
			}

			// ── Gather plain-value inputs ────────────────────────────────────────────

			var currentUserId = claimsPrincipal.FindFirstValue(ClaimTypes.PrimarySid);
			bool userHas2Fa = await userManager.GetTwoFactorEnabledAsync(identityUser);

			int departmentScope = 0;
			bool isAdminOrManagingUser = false;
			bool isGroupAdmin = false;

			try
			{
				var department = await departmentsService.GetDepartmentByUserIdAsync(identityUser.Id);
				if (department != null)
				{
					departmentScope = await departmentSettingsService.GetRequire2FAForAdminsAsync(department.DepartmentId);
					isAdminOrManagingUser = department.IsUserAnAdmin(identityUser.Id)
					                        || department.ManagingUserId == identityUser.Id;

					if (!isAdminOrManagingUser && departmentScope == 2 && departmentGroupsService != null)
					{
						var group = await departmentGroupsService.GetGroupForUserAsync(identityUser.Id, department.DepartmentId);
						isGroupAdmin = group != null && group.IsUserGroupAdmin(identityUser.Id);
					}
				}
			}
			catch
			{
				// Fail open on department lookup errors
			}

			DateTime? lastStepUpVerifiedAtUtc = ParseStepUpSession(context.HttpContext.Session, currentUserId);

			// ── Delegate the decision ────────────────────────────────────────────────

			var enforcementContext = new TwoFactorEnforcementContext(
				UserHas2FaEnabled: userHas2Fa,
				DepartmentScope: departmentScope,
				IsAdminOrManagingUser: isAdminOrManagingUser,
				IsGroupAdmin: isGroupAdmin,
				LastStepUpVerifiedAtUtc: lastStepUpVerifiedAtUtc,
				StepUpWindowMinutes: TwoFactorConfig.StepUpVerificationWindowMinutes);

			var decision = TwoFactorEnforcementEvaluator.Evaluate(enforcementContext, DateTime.UtcNow);

			// ── Act on the decision ──────────────────────────────────────────────────

			switch (decision.Outcome)
			{
				case TwoFactorEnforcementOutcome.NotRequired:
					await next();
					return;

				case TwoFactorEnforcementOutcome.EnrollmentRequired:
					context.Result = new RedirectResult("/User/TwoFactor/Enable2FA?enforced=1");
					return;

				case TwoFactorEnforcementOutcome.StepUpRequired:
				default:
					var request = context.HttpContext.Request;
					var returnUrl = $"{request.Path}{request.QueryString}";
					context.Result = new RedirectToRouteResult(new RouteValueDictionary
					{
						{ "area", "User" },
						{ "controller", "TwoFactor" },
						{ "action", "Verify2FA" },
						{ "returnUrl", returnUrl }
					});
					return;
			}
		}

		// ── private helpers ──────────────────────────────────────────────────────────

		/// <summary>
		/// Reads and validates the step-up session entry for the given user.
		/// Returns the UTC timestamp of the last successful verification, or <see langword="null"/>
		/// if no valid proof exists.
		/// </summary>
		private static DateTime? ParseStepUpSession(ISession session, string currentUserId)
		{
			var sessionValue = session.GetString(StepUpSessionKey);
			if (string.IsNullOrEmpty(sessionValue))
				return null;

			var separatorIndex = sessionValue.IndexOf('|');
			if (separatorIndex <= 0)
				return null;

			var storedUserId = sessionValue.Substring(0, separatorIndex);
			var verifiedAtRaw = sessionValue.Substring(separatorIndex + 1);

			if (!string.Equals(storedUserId, currentUserId, StringComparison.Ordinal))
				return null;

			if (!DateTime.TryParse(verifiedAtRaw, null, DateTimeStyles.RoundtripKind, out var verifiedAt))
				return null;

			return verifiedAt;
		}
	}
}
