using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Server-side MFA evidence for the API caller's validated session (passkey plan section 7.6). The session and the
	/// generation come from session validation, never from token claims; without a tracked session there is no evidence.
	/// A Web session's API bridge carries the Web session's id, so evidence recorded on Web counts here too.
	/// </summary>
	public static class ApiStepUpEvidence
	{
		public static string SessionKey(HttpContext httpContext) =>
			MfaEvidence.TrackedSessionKey(HttpProtectedGrantContext.SessionOf(httpContext)?.SessionId);

		/// <summary>
		/// True when this session completed an actual second factor within <paramref name="window"/>, with a method the
		/// department accepts for <paramref name="scope"/> now (passkey plan section 10.1). With
		/// <paramref name="excludeFederated"/>, provider step-up does not count: it cannot authorize turning itself on or
		/// changing its own mapping (plan section 7.8).
		/// </summary>
		public static async Task<bool> HasRecentSecondFactorAsync(IMfaEvidenceService evidence, IMfaPolicyService policy, string userId,
			HttpContext httpContext, int? departmentId, MfaMethodScope scope, TimeSpan window, CancellationToken cancellationToken = default,
			bool excludeFederated = false)
		{
			var session = HttpProtectedGrantContext.SessionOf(httpContext);
			var sessionKey = MfaEvidence.TrackedSessionKey(session?.SessionId);
			if (evidence == null || policy == null || sessionKey == null || string.IsNullOrWhiteSpace(userId))
				return false;

			var latest = await evidence.GetLatestSecondFactorAsync(userId, sessionKey, session.AuthenticationGeneration, cancellationToken);
			return MfaEvidenceService.IsFresh(latest, window, DateTime.UtcNow) &&
				!(excludeFederated && latest.Method == (int)MfaEvidenceMethod.Federated) &&
				await policy.IsEvidenceAcceptedAsync(departmentId, scope, latest, cancellationToken);
		}
	}
}
