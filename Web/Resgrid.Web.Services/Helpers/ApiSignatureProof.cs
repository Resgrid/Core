using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// What an API caller can prove for a signature (S0): the validated session's latest second-factor evidence, read from the
	/// server-side evidence store (never from the request body or token claims). The signing service decides the level.
	/// </summary>
	public static class ApiSignatureProof
	{
		public static async Task<SignatureProof> BuildAsync(HttpContext httpContext, string userId, int departmentId, IMfaEvidenceService evidence,
			IMfaPolicyService policy, CancellationToken cancellationToken = default)
		{
			var proof = SignatureProof.SessionOnly(userId, httpContext?.Request == null ? null : IpAddressHelper.GetRequestIP(httpContext.Request, true),
				httpContext?.Request?.Headers["User-Agent"].ToString());
			if (evidence == null || httpContext == null || string.IsNullOrWhiteSpace(userId))
				return proof;

			try
			{
				var session = HttpProtectedGrantContext.SessionOf(httpContext);
				var sessionKey = MfaEvidence.TrackedSessionKey(session?.SessionId);
				if (sessionKey == null)
					return proof;

				var latest = await evidence.GetLatestSecondFactorAsync(userId, sessionKey, session.AuthenticationGeneration, cancellationToken);
				if (latest != null && (policy == null || await policy.IsEvidenceAcceptedAsync(departmentId, MfaMethodScope.Login, latest, cancellationToken)))
				{
					proof.StepUpVerifiedOnUtc = latest.VerifiedOnUtc;
					proof.StepUpMethod = latest.Method;
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Unknown evidence is no evidence: an L2 signature stays refused, never waved through.
				Logging.LogException(ex, "Step-up evidence for a signature could not be read.");
			}

			return proof;
		}
	}
}
