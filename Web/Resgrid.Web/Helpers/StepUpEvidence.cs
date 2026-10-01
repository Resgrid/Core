using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// When this Web session last completed an actual second factor (passkey plan section 7.6 row 7): a sign-in with
	/// TOTP or a step-up, read from server-side evidence for the current session and authentication generation. It
	/// replaces the old session-stamp proof. A recovery-code sign-in is recovery evidence and never counts here.
	/// </summary>
	public static class StepUpEvidence
	{
		private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

		/// <param name="policy">When given, the evidence also has to be of a method the department accepts for
		/// <paramref name="scope"/> now (passkey plan section 10.1); a method it has since switched off does not count.</param>
		/// <param name="excludeFederated">Provider step-up does not count: for turning provider step-up on (plan section 7.8).</param>
		public static async Task<DateTime?> GetLatestSecondFactorUtcAsync(IMfaEvidenceService evidence, IdentityUser user,
			HttpContext httpContext, IMfaPolicyService policy, int? departmentId, MfaMethodScope scope,
			CancellationToken cancellationToken = default, bool excludeFederated = false)
		{
			if (evidence == null || user == null || httpContext == null)
				return null;

			var sessionKey = MfaEvidenceSession.KeyFor(httpContext.User, httpContext);
			if (sessionKey == null)
				return null;

			var latest = await evidence.GetLatestSecondFactorAsync(user.Id, sessionKey, user.AuthenticationGeneration, cancellationToken);
			if (excludeFederated && latest?.Method == (int)MfaEvidenceMethod.Federated)
				return null;
			if (latest != null && policy != null && !await policy.IsEvidenceAcceptedAsync(departmentId, scope, latest, cancellationToken))
				return null;

			return Normalize(latest?.VerifiedOnUtc, DateTime.UtcNow);
		}

		/// <summary>
		/// Evidence written by another node may be a few seconds ahead of this clock: accept that as "now", and refuse
		/// anything further in the future.
		/// </summary>
		public static DateTime? Normalize(DateTime? verifiedOnUtc, DateTime utcNow)
		{
			if (verifiedOnUtc == null)
				return null;

			var verified = DateTime.SpecifyKind(verifiedOnUtc.Value, DateTimeKind.Utc);
			if (verified > utcNow.Add(ClockSkew))
				return null;
			return verified > utcNow ? utcNow : verified;
		}
	}
}
