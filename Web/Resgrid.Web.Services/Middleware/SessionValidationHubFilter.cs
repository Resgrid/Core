using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Abstractions;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Middleware
{
	/// <summary>Revalidates user session state for every invocation on an open API SignalR connection.</summary>
	public class SessionValidationHubFilter : IHubFilter
	{
		private readonly IUserSessionService _userSessionService;

		public SessionValidationHubFilter(IUserSessionService userSessionService)
		{
			_userSessionService = userSessionService;
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

		public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next) =>
			next(context);

		public Task OnDisconnectedAsync(HubLifetimeContext context, Exception exception,
			Func<HubLifetimeContext, Exception, Task> next) => next(context, exception);

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
						Resgrid.Framework.Logging.LogException(ex, "API eventing session activity update failed.");
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "API eventing authentication state validation unavailable.");
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
