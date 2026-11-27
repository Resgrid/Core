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
	/// What an API caller can prove for a signature (S0): the validated session's latest second-factor evidence, and its latest password
	/// or SSO reauthentication, read from the server-side evidence store (never from the request body or token claims). Evidence that
	/// names another department is not offered. The signing service decides the level under the department's step-up rule.
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
				if (latest != null && InDepartment(latest, departmentId) &&
					(policy == null || await policy.IsEvidenceAcceptedAsync(departmentId, MfaMethodScope.Login, latest, cancellationToken)))
				{
					proof.StepUpVerifiedOnUtc = latest.VerifiedOnUtc;
					proof.StepUpMethod = latest.Method;
				}

				// The password or SSO entered again: offered as such, never as a second factor; the department's rule decides whether it counts.
				var reauthentication = await evidence.GetLatestReauthenticationAsync(userId, sessionKey, session.AuthenticationGeneration, cancellationToken);
				if (reauthentication != null && InDepartment(reauthentication, departmentId))
				{
					proof.ReauthenticatedOnUtc = reauthentication.VerifiedOnUtc;
					proof.ReauthenticationMethod = reauthentication.Method;
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Unknown evidence is no evidence: an L2 signature stays refused, never waved through.
				Logging.LogException(ex, "Step-up evidence for a signature could not be read.");
			}

			return proof;
		}

		/// <summary>Evidence recorded for one department (a step-up made there) never serves another; sign-in evidence names none.</summary>
		private static bool InDepartment(MfaEvidence evidence, int departmentId) => evidence.DepartmentId == null || evidence.DepartmentId == departmentId;
	}
}
