using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	public interface IUserSessionMfaEvidenceRepository
	{
		Task InsertAsync(MfaEvidence evidence, CancellationToken cancellationToken = default);

		/// <summary>
		/// The most recent unrevoked, unexpired evidence of <paramref name="kind"/> for the session, issued under
		/// <paramref name="authenticationGeneration"/>; null when there is none.
		/// </summary>
		Task<MfaEvidence> GetLatestAsync(string userId, string sessionKey, MfaEvidenceKind kind, long authenticationGeneration,
			DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>As <see cref="GetLatestAsync"/>, restricted to evidence recorded for <paramref name="purpose"/>.</summary>
		Task<MfaEvidence> GetLatestForPurposeAsync(string userId, string sessionKey, MfaEvidenceKind kind, MfaEvidencePurpose purpose,
			long authenticationGeneration, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<int> RevokeForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Revokes the user's evidence produced by one factor instance (for example a passkey that was removed).</summary>
		Task<int> RevokeForFactorAsync(string userId, string factorReference, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Revokes every user's evidence from one factor, such as a provider step-up mapping version that changed.</summary>
		Task<int> RevokeByFactorReferenceAsync(string factorReference, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);
	}
}
