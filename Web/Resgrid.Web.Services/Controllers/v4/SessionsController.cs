using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>User authentication session inventory and revocation.</summary>
	[Route("api/v{VersionId:apiVersion}/sessions")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class SessionsController : V4AuthenticatedApiControllerbase
	{
		private readonly IUserSessionService _userSessionService;
		private readonly ISystemAuditsService _systemAuditsService;

		public SessionsController(IUserSessionService userSessionService, ISystemAuditsService systemAuditsService)
		{
			_userSessionService = userSessionService;
			_systemAuditsService = systemAuditsService;
		}

		[HttpGet]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<IReadOnlyList<UserSessionSummary>>> Get(CancellationToken cancellationToken)
		{
			var currentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			var sessions = await _userSessionService.GetActiveForUserAsync(UserId, cancellationToken);
			foreach (var session in sessions)
				session.IsCurrent = string.Equals(session.UserSessionId, currentSessionId, StringComparison.Ordinal);
			return Ok(sessions);
		}

		[HttpDelete("{sessionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> Revoke(string sessionId, CancellationToken cancellationToken)
		{
			var result = await _userSessionService.RevokeSessionAsync(UserId, UserId, sessionId,
				UserSessionRevocationReason.UserRevoked, cancellationToken);
			await AuditAsync(SystemAuditTypes.SessionRevoked, sessionId, result.RevokedSessionCount > 0, cancellationToken);
			return Ok(new { revoked = result.RevokedSessionCount });
		}

		[HttpPost("revoke-others")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<IActionResult> RevokeOthers(CancellationToken cancellationToken)
		{
			var currentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			if (string.IsNullOrWhiteSpace(currentSessionId))
				return Conflict(new { error = "legacy_session", message = "Refresh this session before revoking all others." });

			var result = await _userSessionService.RevokeOtherSessionsAsync(UserId, currentSessionId,
				UserSessionRevocationReason.OtherSessionsRevoked, cancellationToken);
			await AuditAsync(SystemAuditTypes.OtherSessionsRevoked, null, true, cancellationToken);
			return Ok(new { revoked = result.RevokedSessionCount });
		}

		[HttpPost("revoke-all")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> RevokeAll(CancellationToken cancellationToken)
		{
			var result = await _userSessionService.RevokeAllAsync(UserId, UserId,
				UserSessionRevocationReason.AccountCompromised, DateTime.UtcNow, cancellationToken);
			await AuditAsync(SystemAuditTypes.AllSessionsRevoked, null, true, cancellationToken);
			return Ok(new { revoked = result.RevokedSessionCount, reauthenticationRequired = true });
		}

		private Task AuditAsync(SystemAuditTypes type, string sessionId, bool successful, CancellationToken cancellationToken)
		{
			return _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)type,
				UserId = UserId,
				TargetUserId = UserId,
				SessionId = SessionSupportSuffix(sessionId),
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				CorrelationId = HttpContext.TraceIdentifier,
				Data = $"API session operation. Agent={BoundAuditValue(Request.Headers.UserAgent.ToString(), 256)}",
				LoggedOn = DateTime.UtcNow
			}, cancellationToken);
		}

		private static string BoundAuditValue(string value, int maximumLength)
		{
			if (string.IsNullOrWhiteSpace(value)) return "Unknown";
			var sanitized = value.Replace("\r", " ").Replace("\n", " ").Trim();
			return sanitized.Length <= maximumLength ? sanitized : sanitized.Substring(0, maximumLength);
		}

		private static string SessionSupportSuffix(string sessionId) =>
			string.IsNullOrWhiteSpace(sessionId) || sessionId.Length <= 8
				? sessionId
				: sessionId.Substring(sessionId.Length - 8);
	}
}
