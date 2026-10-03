using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Model.TwoFactor;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Attributes
{
	/// <summary>
	/// Action filter that enforces a recent step-up 2FA verification before accessing sensitive operations.
	/// <para>
	/// All enforcement decisions are delegated to <see cref="TwoFactorEnforcementEvaluator.Evaluate"/>;
	/// this filter is responsible only for the ASP.NET plumbing (resolving services, reading the session's
	/// server-side MFA evidence, and writing the HTTP result).
	/// </para>
	/// <list type="bullet">
	///   <item><see cref="TwoFactorEnforcementOutcome.NotRequired"/> — pass through.</item>
	///   <item><see cref="TwoFactorEnforcementOutcome.EnrollmentRequired"/> — redirect to enrollment.</item>
	///   <item><see cref="TwoFactorEnforcementOutcome.StepUpRequired"/> — redirect to Verify2FA.</item>
	/// </list>
	/// <para>
	/// A submission stopped for step-up is not lost: it is held (<see cref="StepUpFormReplay"/>), Verify2FA returns to a page that
	/// posts it back, and the global <see cref="Filters.HeldSubmissionReplayFilter"/> puts the held form in place before model
	/// binding. A script call is answered 403 with the Verify2FA address instead of a redirect it cannot follow.
	/// </para>
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
	public sealed class RequiresRecentTwoFactorAttribute : Attribute, IAsyncActionFilter
	{
		internal const string MfaVerifiedAtHttpContextItemKey = "mfa_verified_at";

		/// <summary>The response header a script call reads for where to send the user to verify.</summary>
		public const string StepUpRedirectHeader = "X-Resgrid-Step-Up";

		/// <summary>
		/// Requires MFA for the decorated operation even when the department-wide administrator
		/// 2FA setting is disabled.
		/// </summary>
		public bool RequireForOperation { get; set; }

		/// <summary>
		/// Optional operation-specific verification window. Values less than one use the configured
		/// default window.
		/// </summary>
		public int VerificationWindowMinutes { get; set; }

		/// <summary>
		/// Which department switches decide the acceptable methods (passkey plan section 7.6): sign-in (default), security
		/// changes, or ADP. Evidence of a method the active department no longer accepts does not count.
		/// </summary>
		public Resgrid.Model.Security.MfaMethodScope MethodScope { get; set; } = Resgrid.Model.Security.MfaMethodScope.Login;

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

			if (userManager == null ||
				(!RequireForOperation && (departmentsService == null || departmentSettingsService == null)))
			{
				if (RequireForOperation)
				{
					context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
					return;
				}

				await next();
				return;
			}

			var identityUser = await userManager.GetUserAsync(claimsPrincipal);
			if (identityUser == null)
			{
				if (RequireForOperation)
				{
					context.Result = new ChallengeResult();
					return;
				}

				await next();
				return;
			}

			// ── Gather plain-value inputs ────────────────────────────────────────────

			bool userHas2Fa = await userManager.GetTwoFactorEnabledAsync(identityUser);

			int departmentScope = 0;
			bool isAdminOrManagingUser = false;
			bool isGroupAdmin = false;

			try
			{
				var department = RequireForOperation || departmentsService == null
					? null
					: await departmentsService.GetDepartmentByUserIdAsync(identityUser.Id);
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

			// The proof is server-side evidence bound to this session and the account's current generation (passkey plan
			// section 7.6 row 7), so a password change or authenticator change ends it everywhere at once.
			var activeDepartmentId = StepUpFormReplay.ActiveDepartmentOf(claimsPrincipal);
			DateTime? lastStepUpVerifiedAtUtc;
			try
			{
				lastStepUpVerifiedAtUtc = await Helpers.StepUpEvidence.GetLatestSecondFactorUtcAsync(
					services.GetService<IMfaEvidenceService>(), identityUser, context.HttpContext, services.GetService<IMfaPolicyService>(),
					activeDepartmentId, MethodScope, context.HttpContext.RequestAborted);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Unknown evidence is no evidence: the user is asked to verify, never waved through.
				Framework.Logging.LogException(ex, "Step-up evidence lookup failed.");
				lastStepUpVerifiedAtUtc = null;
			}
			var verificationWindowMinutes = VerificationWindowMinutes > 0
				? VerificationWindowMinutes
				: TwoFactorConfig.StepUpVerificationWindowMinutes;

			// ── Delegate the decision ────────────────────────────────────────────────

			var enforcementContext = new TwoFactorEnforcementContext(
				UserHas2FaEnabled: userHas2Fa,
				DepartmentScope: departmentScope,
				IsAdminOrManagingUser: isAdminOrManagingUser,
				IsGroupAdmin: isGroupAdmin,
				LastStepUpVerifiedAtUtc: lastStepUpVerifiedAtUtc,
				StepUpWindowMinutes: verificationWindowMinutes,
				RequireStepUpForOperation: RequireForOperation);

			var decision = TwoFactorEnforcementEvaluator.Evaluate(enforcementContext, DateTime.UtcNow);

			// ── Act on the decision ──────────────────────────────────────────────────

			switch (decision.Outcome)
			{
				case TwoFactorEnforcementOutcome.NotRequired:
					if (RequireForOperation && lastStepUpVerifiedAtUtc.HasValue)
						context.HttpContext.Items[MfaVerifiedAtHttpContextItemKey] = lastStepUpVerifiedAtUtc.Value;

					await next();
					return;

				case TwoFactorEnforcementOutcome.EnrollmentRequired:
					context.Result = new RedirectResult("/User/TwoFactor/Enable2FA?enforced=1");
					return;

				case TwoFactorEnforcementOutcome.StepUpRequired:
				default:
					var request = context.HttpContext.Request;

					// A submission is held so verifying finishes it; only one that cannot be held must be made again.
					var (returnUrl, submissionLost) = await StepUpFormReplay.HoldForVerificationAsync(context.HttpContext,
						services.GetService<ICacheProvider>(), services.GetService<IDataProtectionProvider>(), identityUser.Id, ReturnUrlFor(request));

					var verifyRoute = new RouteValueDictionary
					{
						{ "area", "User" },
						{ "controller", "TwoFactor" },
						{ "action", "Verify2FA" },
						{ "returnUrl", returnUrl },
						// A display hint only: Verify2FA offers a passkey where this scope accepts one; this filter still decides.
						{ "scope", MethodScope.ToString() }
					};
					if (submissionLost)
						verifyRoute["resubmit"] = "1";

					if (StepUpFormReplay.IsScriptRequest(request))
					{
						// A script would follow a redirect and read the Verify2FA page as its answer (a "successful" save that never
						// happened). Tell it where the user must go; the page's ajaxError handler navigates there.
						var verifyUrl = $"{request.PathBase}/User/TwoFactor/Verify2FA?returnUrl={Uri.EscapeDataString(returnUrl)}" +
							$"&scope={Uri.EscapeDataString(MethodScope.ToString())}" + (submissionLost ? "&resubmit=1" : "");
						context.HttpContext.Response.Headers[StepUpRedirectHeader] = verifyUrl;
						context.Result = new JsonResult(new { success = false, error = "step_up_required", redirect = verifyUrl })
						{
							StatusCode = StatusCodes.Status403Forbidden
						};
						return;
					}

					context.Result = new RedirectToRouteResult(verifyRoute);
					return;
			}
		}

		/// <summary>
		/// The current user's most recent second-factor verification in this session (server-side evidence), or null.
		/// For JSON commands that must pass the proof to a service rule (Protected Workflows approvals) instead of
		/// redirecting through Verify2FA.
		/// </summary>
		public static Task<DateTime?> GetStepUpVerifiedAtUtcAsync(HttpContext httpContext, IdentityUser user,
			IMfaEvidenceService evidence, IMfaPolicyService policy, int? departmentId, Resgrid.Model.Security.MfaMethodScope scope) =>
			Helpers.StepUpEvidence.GetLatestSecondFactorUtcAsync(evidence, user, httpContext, policy, departmentId, scope,
				httpContext?.RequestAborted ?? default);

		/// <summary>
		/// Where Verify2FA sends the user back to. A GET returns to itself; a form post cannot be replayed, so it returns
		/// to the local page that submitted it, when there is one.
		/// </summary>
		public static string ReturnUrlFor(HttpRequest request)
		{
			var self = $"{request.PathBase}{request.Path}{request.QueryString}";
			if (HttpMethods.IsGet(request.Method))
				return self;

			var referer = request.Headers.Referer.ToString();
			if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) &&
				string.Equals(refererUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
				(request.Host.Port == null || refererUri.Port == request.Host.Port))
				return refererUri.PathAndQuery;

			return self;
		}
	}
}
