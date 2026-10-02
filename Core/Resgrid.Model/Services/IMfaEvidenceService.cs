using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Server-side MFA evidence per session (passkey plan sections 5.3 and 6.2). Written only where a factor was actually
	/// verified; read by guards that need a fresh first factor or a recent second factor. Recovery evidence is recorded
	/// separately and never satisfies either.
	/// </summary>
	public interface IMfaEvidenceService
	{
		/// <summary>Records one verified factor for the session. Retention comes from TwoFactorConfig.</summary>
		Task RecordAsync(string userId, string sessionKey, UserSessionClientApplication client, MfaEvidenceKind kind,
			MfaEvidenceMethod method, MfaEvidencePurpose purpose, DateTime verifiedOnUtc, long authenticationGeneration,
			int? departmentId = null, string factorReference = null, CancellationToken cancellationToken = default);

		/// <summary>The latest first-factor verification for this session under the user's current generation.</summary>
		Task<MfaEvidence> GetLatestFirstFactorAsync(string userId, string sessionKey, long currentGeneration,
			CancellationToken cancellationToken = default);

		/// <summary>True when a first factor was verified for this session within <paramref name="maxAge"/>.</summary>
		Task<bool> HasFreshFirstFactorAsync(string userId, string sessionKey, long currentGeneration, TimeSpan maxAge,
			DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The latest second-factor verification for this session under the user's current generation.</summary>
		Task<MfaEvidence> GetLatestSecondFactorAsync(string userId, string sessionKey, long currentGeneration,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The latest explicit step-up (not a sign-in) for this session under the user's current generation, for operations
		/// whose rule is an explicit verification (chat export, plan section 7.6 row 8).
		/// </summary>
		Task<MfaEvidence> GetLatestStepUpAsync(string userId, string sessionKey, long currentGeneration,
			CancellationToken cancellationToken = default);

		/// <summary>Retires every piece of evidence for the user (MFA turned off or replaced, factors recovered).</summary>
		Task RevokeForUserAsync(string userId, CancellationToken cancellationToken = default);

		/// <summary>Retires the evidence one factor instance produced, such as a removed passkey (plan section 6.1 item 8).</summary>
		Task RevokeForFactorAsync(string userId, string factorReference, CancellationToken cancellationToken = default);
	}
}
