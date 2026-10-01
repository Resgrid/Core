using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Abstractions;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Web.Eventing.Middleware
{
	/// <summary>
	/// Revalidates authenticated user state for every invocation on an already-open
	/// SignalR connection. This prevents a revoked session from continuing to publish
	/// chat, location, or subscription commands until its access token naturally expires.
	/// </summary>
	public class SessionValidationHubFilter : IHubFilter
	{
		private readonly IUserSessionService _userSessionService;
		private readonly Resgrid.Services.SessionConnectionRegistry _connections;

		public SessionValidationHubFilter(IUserSessionService userSessionService, Resgrid.Services.SessionConnectionRegistry connections)
		{
			_userSessionService = userSessionService;
			_connections = connections;
		}

		public async ValueTask<object> InvokeMethodAsync(HubInvocationContext invocationContext,
			Func<HubInvocationContext, ValueTask<object>> next)
		{
			if (!await IsValidAsync(invocationContext.Context))
			{
				invocationContext.Context.Abort();
				throw new HubException("This authentication session is no longer valid.");
			}

			return await next(invocationContext);
		}

		/// <summary>
		/// A connection with a user session joins that session's group, for events meant for it alone, and is tracked so the
		/// sweep can close it once the session ends or locks (slice 16).
		/// </summary>
		public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
		{
			var sessionId = SessionIdOf(context.Context.User);
			if (sessionId != null)
			{
				_connections.Register(context.Context.ConnectionId, sessionId, context.Context.Abort);
				await context.Hub.Groups.AddToGroupAsync(context.Context.ConnectionId, SessionEvents.GroupFor(sessionId));
			}

			await next(context);
		}

		public Task OnDisconnectedAsync(HubLifetimeContext context, Exception exception,
			Func<HubLifetimeContext, Exception, Task> next)
		{
			_connections.Unregister(context.Context.ConnectionId);
			return next(context, exception);
		}

		/// <summary>The tracked user session a connection belongs to; null for workloads and pre-session tokens.</summary>
		private static string SessionIdOf(ClaimsPrincipal principal)
		{
			if (principal?.Identity?.IsAuthenticated != true)
				return null;

			var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue(ClaimTypes.PrimarySid) ??
				principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
			if (string.IsNullOrWhiteSpace(userId) || userId.StartsWith("dept_", StringComparison.Ordinal) || userId.StartsWith("system_", StringComparison.Ordinal))
				return null;

			var sessionId = principal.FindFirstValue(SessionClaimTypes.SessionId);
			return string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
		}

		private async Task<bool> IsValidAsync(HubCallerContext context)
		{
			var principal = context.User;
			if (principal?.Identity?.IsAuthenticated != true)
				return true;

			var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
				principal.FindFirstValue(ClaimTypes.PrimarySid) ??
				principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
			if (string.IsNullOrWhiteSpace(userId) || userId.StartsWith("dept_", StringComparison.Ordinal) ||
				userId.StartsWith("system_", StringComparison.Ordinal))
				return true;

			long? generation = long.TryParse(
				principal.FindFirstValue(SessionClaimTypes.AuthenticationGeneration), NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var parsedGeneration) ? parsedGeneration : null;
			int? departmentId = int.TryParse(principal.FindFirstValue(ClaimTypes.PrimaryGroupSid),
				out var parsedDepartmentId) ? parsedDepartmentId : null;

			try
			{
				var validation = await _userSessionService.ValidateAsync(new SessionPrincipalContext
				{
					UserId = userId,
					SessionId = principal.FindFirstValue(SessionClaimTypes.SessionId),
					AuthenticationGeneration = generation,
					DepartmentId = departmentId,
					CredentialIssuedOn = GetIssuedOn(principal)
				}, context.ConnectionAborted);
				if (!validation.IsValid)
					return false;

				// Skip the write when the recorded activity is still inside the write interval: without this
				// every hub invocation pays a location lookup and a database round trip to update no rows.
				// It also collapses the duplicate touch a connection makes through the middleware as well.
				var occurredOn = DateTime.UtcNow;
				if (validation.Session != null && _userSessionService.ShouldRecordActivity(validation.Session, occurredOn))
				{
					var httpContext = context.GetHttpContext();
					try
					{
						await _userSessionService.TouchAsync(validation.Session.UserSessionId, new RequestActivity
						{
							OccurredOn = occurredOn,
							IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
							UserAgent = httpContext?.Request.Headers.UserAgent
						}, context.ConnectionAborted);
					}
					catch (Exception ex)
					{
						Resgrid.Framework.Logging.LogException(ex, "Eventing session activity update failed.");
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Eventing hub authentication state validation unavailable.");
				return false;
			}
		}

		private static DateTime? GetIssuedOn(ClaimsPrincipal principal)
		{
			var value = principal.FindFirstValue(OpenIddictConstants.Claims.IssuedAt);
			if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
			{
				try { return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime; }
				catch (ArgumentOutOfRangeException) { return null; }
			}

			return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
				out var parsed) ? parsed.ToUniversalTime() : null;
		}
	}
}
