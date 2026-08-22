using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Resgrid.Config;
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
				await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
				if (HttpMethods.IsGet(context.Request.Method) && !context.Request.Path.StartsWithSegments("/Account"))
				{
					var returnUrl = Uri.EscapeDataString(context.Request.PathBase + context.Request.Path + context.Request.QueryString);
					context.Response.Redirect($"/Account/LogOn?returnUrl={returnUrl}");
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
