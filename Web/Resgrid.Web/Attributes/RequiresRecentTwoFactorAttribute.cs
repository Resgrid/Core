﻿using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Resgrid.Config;

namespace Resgrid.Web.Attributes
{
	/// <summary>
	/// Action filter that enforces a recent step-up 2FA verification before accessing sensitive operations.
	/// If the user has 2FA enabled and the last step-up verification is absent or older than
	/// <see cref="TwoFactorConfig.StepUpVerificationWindowMinutes"/>, redirects to the Verify2FA page.
	/// Users without 2FA enabled pass through (enrollment enforcement is handled by separate middleware).
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
	public sealed class RequiresRecentTwoFactorAttribute : ActionFilterAttribute
	{
		/// <summary>
		/// Session key whose value is a pipe-delimited string of the form "{userId}|{verifiedAtUtcRoundtrip}".
		/// Binding the step-up proof to the user id prevents one user from inheriting another user's proof
		/// within a shared session store.
		/// </summary>
		internal const string StepUpSessionKey = "Resgrid2FAVerifiedAt";

		public override void OnActionExecuting(ActionExecutingContext context)
		{
			var user = context.HttpContext.User;

			// Not authenticated — let the framework handle it
			if (user?.Identity == null || !user.Identity.IsAuthenticated)
			{
				base.OnActionExecuting(context);
				return;
			}

			// Resolve the current user's unique id from the PrimarySid claim (set by ClaimsLogic).
			var currentUserId = user.FindFirstValue(ClaimTypes.PrimarySid);

			// Check if user has 2FA enabled via the claim set by Identity
			// TwoFactorEnabled is persisted on the IdentityUser; we check via UserManager
			// but that requires async — use session flag set during login/step-up instead.
			// If session key is absent the user must re-verify.
			var session = context.HttpContext.Session;
			var sessionValue = session.GetString(StepUpSessionKey);

			if (!string.IsNullOrEmpty(sessionValue))
			{
				// Expected format: "{userId}|{verifiedAtUtcRoundtrip}"
				var separatorIndex = sessionValue.IndexOf('|');
				if (separatorIndex > 0)
				{
					var storedUserId = sessionValue.Substring(0, separatorIndex);
					var verifiedAtRaw = sessionValue.Substring(separatorIndex + 1);

					// Only accept the step-up proof when it belongs to the currently authenticated user.
					if (string.Equals(storedUserId, currentUserId, StringComparison.Ordinal) &&
						DateTime.TryParse(verifiedAtRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var verifiedAt))
					{
						var windowExpiry = verifiedAt.AddMinutes(TwoFactorConfig.StepUpVerificationWindowMinutes);
						if (DateTime.UtcNow <= windowExpiry)
						{
							// Still within the valid step-up window for this user
							base.OnActionExecuting(context);
							return;
						}
					}
				}
			}

			// Build the return URL so we can redirect back after verification
			var request = context.HttpContext.Request;
			var returnUrl = $"{request.Path}{request.QueryString}";

			context.Result = new RedirectToRouteResult(new RouteValueDictionary
			{
				{ "area", "User" },
				{ "controller", "TwoFactor" },
				{ "action", "Verify2FA" },
				{ "returnUrl", returnUrl }
			});
		}
	}
}

