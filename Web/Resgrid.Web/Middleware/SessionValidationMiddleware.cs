using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Middleware
{
	/// <summary>
	/// Validates the server-side Web cookie against account and session state. Existing
	/// pre-feature cookies are adopted lazily without interrupting the signed-in user.
	/// </summary>
	public class SessionValidationMiddleware
	{
		private readonly RequestDelegate _next;

		public SessionValidationMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context, IUserSessionService userSessionService)
		{
			if (context.User?.Identity?.IsAuthenticated != true)
			{
				await _next(context);
				return;
			}

			var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
				context.User.FindFirstValue(ClaimTypes.PrimarySid);
			if (string.IsNullOrWhiteSpace(userId))
			{
				await _next(context);
				return;
			}

			var authentication = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			long? generation = null;
			if (long.TryParse(context.User.FindFirstValue(SessionClaimTypes.AuthenticationGeneration),
				NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGeneration))
				generation = parsedGeneration;

			int? departmentId = null;
			if (int.TryParse(context.User.FindFirstValue(ClaimTypes.PrimaryGroupSid), out var parsedDepartmentId))
				departmentId = parsedDepartmentId;

			SessionValidationResult validation;
			try
			{
				validation = await userSessionService.ValidateAsync(new SessionPrincipalContext
				{
					UserId = userId,
					SessionId = context.User.FindFirstValue(SessionClaimTypes.SessionId),
					AuthenticationGeneration = generation,
					DepartmentId = departmentId,
					CredentialIssuedOn = authentication.Properties?.IssuedUtc?.UtcDateTime
				}, context.RequestAborted);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Web authentication state validation unavailable.");
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				return;
			}

			if (!validation.IsValid)
			{
				// A locked shared session keeps its cookie: the same operator unlocks it (plan section 12.5.3). It reaches only its
				// lock screen, status, unlock and end-shift routes, which read the locked row from here; it gets no grant context and
				// records no activity. A page load goes to the lock screen; anything else is refused and told why.
				if (validation.IsLocked && validation.Session != null)
				{
					if (WebSharedSession.AcceptsLockedSession(context.Request))
					{
						context.Items[WebSharedSession.SessionItemKey] = validation.Session;
						await _next(context);
						return;
					}

					if (WebSharedSession.IsNavigation(context.Request))
					{
						context.Response.Redirect(WebSharedSession.LockedUrl(context.Request.PathBase + context.Request.Path + context.Request.QueryString));
						return;
					}

					context.Response.StatusCode = StatusCodes.Status401Unauthorized;
					await context.Response.WriteAsJsonAsync(new { error = validation.FailureCode, lock_version = validation.Session.LockVersion },
						context.RequestAborted);
					return;
				}

				await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
				if (HttpMethods.IsGet(context.Request.Method) && !context.Request.Path.StartsWithSegments("/Account"))
				{
					var returnUrl = Uri.EscapeDataString(context.Request.PathBase + context.Request.Path + context.Request.QueryString);
					// A shared session past its shift ceiling says so on the sign-in page; the next operator signs in normally.
					var reason = validation.FailureCode == SharedSessionRules.ExpiredFailureCode ? "&reason=shift_ended" : string.Empty;
					context.Response.Redirect($"/Account/LogOn?returnUrl={returnUrl}{reason}");
				}
				else
				{
					context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				}
				return;
			}

			var session = validation.Session;
			if (session == null && validation.CanAdoptLegacy && SessionSecurityConfig.TrackingEnabled)
			{
				try
				{
					session = await userSessionService.AdoptLegacyAsync(new LegacySessionContext
					{
						UserId = userId,
						DepartmentId = departmentId,
						AuthenticationGeneration = generation ?? 0,
						ClientApplication = Resgrid.Model.UserSessionClientApplication.Web,
						ExpiresOn = authentication.Properties?.ExpiresUtc?.UtcDateTime ?? DateTime.UtcNow.AddHours(8),
						IpAddress = IpAddressHelper.GetRequestIP(context.Request, true),
						UserAgent = context.Request.Headers.UserAgent
					}, context.RequestAborted);

					if (context.User.Identity is ClaimsIdentity identity)
					{
						identity.AddClaim(new Claim(SessionClaimTypes.SessionId, session.UserSessionId));
						if (!identity.HasClaim(claim => claim.Type == SessionClaimTypes.AuthenticationGeneration))
							identity.AddClaim(new Claim(SessionClaimTypes.AuthenticationGeneration,
								session.AuthenticationGeneration.ToString(CultureInfo.InvariantCulture)));
					}

					await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
						context.User, authentication.Properties);
				}
				catch (Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex, "Legacy Web session adoption unavailable.");
					context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
					return;
				}
			}

			// The session this request was just validated against (or adopted into) is what a version 2
			// Protected Data Grant must match (passkey plan section 8.3); nothing downstream re-derives it
			// from claims.
			var grantSession = ProtectedGrantSessionContext.From(session, authentication.Properties?.IssuedUtc?.UtcDateTime);
			if (grantSession != null)
				context.Items[ProtectedGrantSessionContext.HttpItemKey] = grantSession;
			if (session != null)
				context.Items[WebSharedSession.SessionItemKey] = session;

			// Operator activity moves a shared session's idle deadline only when the operator caused the request (plan section
			// 10.5): a user-activated navigation, or input the page reports. Background polling and sockets never do.
			if (session?.SharedMode == true && WebSharedSession.IsOperatorActivity(context.Request))
			{
				try
				{
					await userSessionService.RecordOperatorActivityAsync(session, context.RequestAborted);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Resgrid.Framework.Logging.LogException(ex, "Shared session operator activity update failed.");
				}
			}

			// Skip the write when the recorded activity is still inside the write interval: without this
			// every authenticated request pays a location lookup and a database round trip to update no
			// rows. It also collapses the duplicate touch a SignalR connection would otherwise make
			// through both this path and the hub filter.
			var occurredOn = DateTime.UtcNow;
			if (session != null && userSessionService.ShouldRecordActivity(session, occurredOn))
			{
				try
				{
					await userSessionService.TouchAsync(session.UserSessionId, new RequestActivity
					{
						OccurredOn = occurredOn,
						IpAddress = IpAddressHelper.GetRequestIP(context.Request, true),
						UserAgent = context.Request.Headers.UserAgent
					}, context.RequestAborted);
				}
				catch (Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex, "Web session activity update failed.");
				}
			}

			await _next(context);
		}
	}
}
