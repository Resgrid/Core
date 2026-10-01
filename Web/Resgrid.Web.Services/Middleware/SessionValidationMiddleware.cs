using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Middleware
{
	/// <summary>
	/// Enforces account-wide credential cutoffs and per-session revocation on every
	/// authenticated user API request. Pre-feature tokens without a session claim remain
	/// valid until their natural expiry unless the account has subsequently been revoked.
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
			var principal = context.User;
			if (principal?.Identity?.IsAuthenticated != true)
			{
				await _next(context);
				return;
			}

			if (string.Equals(principal.FindFirstValue(SessionClaimTypes.WebEventingOnly), "true",
				StringComparison.Ordinal))
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}

			var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
				principal.FindFirstValue(ClaimTypes.PrimarySid) ??
				principal.FindFirstValue(OpenIddictConstants.Claims.Subject);

			// Client-credential/system principals are not user sessions.
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
				Resgrid.Framework.Logging.LogException(ex, "API authentication state validation unavailable.");
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				return;
			}

			if (!validation.IsValid)
			{
				// A locked shared session reaches only its own status, lock, unlock and end-shift endpoints (plan section
				// 12.5.3); they read the locked row from here. It gets no grant context and records no activity.
				if (validation.IsLocked && validation.Session != null && SharedSessionEndpoints.AcceptsLockedSession(context.Request))
				{
					context.Items[SharedSessionEndpoints.SessionItemKey] = validation.Session;
					await _next(context);
					return;
				}

				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				if (validation.IsLocked || validation.FailureCode == SharedSessionRules.ExpiredFailureCode)
				{
					// The token is still what unlocks the session, so a shared-mode client is told why rather than only
					// that the token is refused. A client that does not know shared mode signs in again, which is also safe.
					context.Response.Headers.WWWAuthenticate = $"Bearer error=\"invalid_token\", error_description=\"{validation.FailureCode}\"";
					await context.Response.WriteAsJsonAsync(new SharedSessionRefusal
					{
						Error = validation.FailureCode,
						LockVersion = validation.IsLocked ? validation.Session?.LockVersion : null
					}, context.RequestAborted);
					return;
				}

				context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
				return;
			}

			if (validation.Session != null)
				context.Items[SharedSessionEndpoints.SessionItemKey] = validation.Session;

			// The session this request was just validated against is what a version 2 Protected Data Grant
			// must match (passkey plan section 8.3); nothing downstream re-derives it from token claims.
			var grantSession = ProtectedGrantSessionContext.From(validation.Session, GetIssuedOn(principal));
			if (grantSession != null)
				context.Items[ProtectedGrantSessionContext.HttpItemKey] = grantSession;

			// Operator activity moves a shared session's idle deadline only when the client marks the request as the
			// operator's (plan section 10.5); background polling and sockets never do.
			if (validation.Session?.SharedMode == true && SharedSessionEndpoints.IsOperatorActivity(context.Request))
			{
				try
				{
					await userSessionService.RecordOperatorActivityAsync(validation.Session, context.RequestAborted);
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
			if (validation.Session != null && userSessionService.ShouldRecordActivity(validation.Session, occurredOn))
			{
				try
				{
					await userSessionService.TouchAsync(validation.Session.UserSessionId, new RequestActivity
					{
						OccurredOn = occurredOn,
						IpAddress = IpAddressHelper.GetRequestIP(context.Request, true),
						UserAgent = context.Request.Headers.UserAgent
					}, context.RequestAborted);
				}
				catch (Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex, "API session activity update failed.");
				}
			}

			await _next(context);
		}

		private sealed class SharedSessionRefusal
		{
			[System.Text.Json.Serialization.JsonPropertyName("error")]
			public string Error { get; init; }

			[System.Text.Json.Serialization.JsonPropertyName("lock_version")]
			[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
			public long? LockVersion { get; init; }
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
