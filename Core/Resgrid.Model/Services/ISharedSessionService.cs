using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Durable transitions of a shared vehicle tablet or workstation session (passkey plan sections 5.5 and 12.5.3). Lock and
	/// unlock are compare-and-set on the session itself; the factor that unlocks is verified by the caller, and this service
	/// only commits the unlock it proved.
	/// </summary>
	public interface ISharedSessionService
	{
		/// <summary>The session's shared state and deadlines, under the department's current policy.</summary>
		Task<SharedSessionStatus> GetStatusAsync(UserSession session, CancellationToken cancellationToken = default);

		/// <summary>
		/// Locks the session and advances its lock version, so every grant, challenge, approval and piece of evidence from
		/// before it is void. Locking a locked session succeeds with the current version.
		/// </summary>
		Task<SharedSessionTransition> LockAsync(UserSession session, SharedSessionRequestInfo request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Why the session cannot be unlocked by its operator now (normal sign-in is needed instead), or
		/// <see cref="SharedSessionOutcome.Succeeded"/>. Checked before any factor is verified.
		/// </summary>
		Task<SharedSessionOutcome> CanUnlockAsync(UserSession session, CancellationToken cancellationToken = default);

		/// <summary>
		/// Unlocks the session at <paramref name="expectedLockVersion"/> after the caller verified <paramref name="method"/>
		/// for its operator, and records that as <c>SharedUnlock</c> evidence. The first-factor time and the shift ceiling are
		/// unchanged.
		/// </summary>
		Task<SharedSessionTransition> UnlockAsync(UserSession session, long expectedLockVersion, MfaEvidenceMethod method, string factorReference,
			DateTime verifiedOnUtc, SharedSessionRequestInfo request, CancellationToken cancellationToken = default);

		/// <summary>Audits a failed unlock verification.</summary>
		Task RecordFailedUnlockAsync(UserSession session, string method, SharedSessionRequestInfo request, CancellationToken cancellationToken = default);

		/// <summary>Ends the session for End shift or Switch operator; the next operator signs in normally.</summary>
		Task<SharedSessionTransition> EndShiftAsync(UserSession session, bool switchOperator, SharedSessionRequestInfo request,
			CancellationToken cancellationToken = default);
	}
}
