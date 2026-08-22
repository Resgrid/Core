using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Web.Eventing.Middleware
{
	public class SessionValidationMiddleware
	{
		private readonly RequestDelegate _next;

		public SessionValidationMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context, IUserSessionService userSessionService)
		{
			var principal = context.User;
			if (principal?.Identity?.IsAuthenticated != true)
			{
				await _next(context);
				return;
			}

			var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
				principal.FindFirstValue(ClaimTypes.PrimarySid) ??
				principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
			if (string.IsNullOrWhiteSpace(userId) || userId.StartsWith("dept_", StringComparison.Ordinal) ||
				userId.StartsWith("system_", StringComparison.Ordinal))
			{
				await _next(context);
				return;
			}

			long? generation = null;
			if (long.TryParse(principal.FindFirstValue(SessionClaimTypes.AuthenticationGeneration),
				NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGeneration))
				generation = parsedGeneration;

			int? departmentId = null;
			if (int.TryParse(principal.FindFirstValue(ClaimTypes.PrimaryGroupSid), out var parsedDepartmentId))
				departmentId = parsedDepartmentId;

			SessionValidationResult validation;
			try
			{
				validation = await userSessionService.ValidateAsync(new SessionPrincipalContext
				{
					UserId = userId,
					SessionId = principal.FindFirstValue(SessionClaimTypes.SessionId),
					AuthenticationGeneration = generation,
					DepartmentId = departmentId,
					CredentialIssuedOn = GetIssuedOn(principal)
				}, context.RequestAborted);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Eventing authentication state validation unavailable.");
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				return;
			}

			if (!validation.IsValid)
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}

			// Skip the write when the recorded activity is still inside the write interval: without this
			// every authenticated request pays a location lookup and a database round trip to update no
			// rows. It also collapses the duplicate touch a SignalR connection would otherwise make
			// through both this path and the hub filter.
			var occurredOn = DateTime.UtcNow;
			if (validation.Session != null && userSessionService.ShouldRecordActivity(validation.Session, occurredOn))
			{
				try
				{
					await userSessionService.TouchAsync(validation.Session.UserSessionId, new RequestActivity
					{
						OccurredOn = occurredOn,
						IpAddress = context.Connection.RemoteIpAddress?.ToString(),
						UserAgent = context.Request.Headers.UserAgent
					}, context.RequestAborted);
				}
				catch (Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex, "Eventing session activity update failed.");
				}
			}

			await _next(context);
		}

		private static DateTime? GetIssuedOn(ClaimsPrincipal principal)
		{
			var value = principal.FindFirstValue(OpenIddictConstants.Claims.IssuedAt);
			if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
			{
				try { return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime; }
				catch (ArgumentOutOfRangeException) { return null; }
			}

			return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
				? parsed.ToUniversalTime()
				: null;
		}
	}
}
