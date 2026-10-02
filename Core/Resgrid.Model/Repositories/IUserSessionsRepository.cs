using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IUserSessionsRepository : IRepository<UserSession>
	{
		Task<IReadOnlyList<UserSession>> GetActiveByUserAsync(string userId, DateTime utcNow);
		Task<UserSession> GetByAuthorizationIdAsync(string authorizationId);

		/// <summary>
		/// Counts the department's policy-managed active sessions for the user and inserts the new session in a
		/// single serialized database operation. Returns false (and inserts nothing) when the limit is already met,
		/// so two concurrent logins cannot both pass the check and exceed MaxConcurrentSessions.
		/// </summary>
		Task<bool> TryInsertWithinDepartmentSessionLimitAsync(UserSession session, int departmentId,
			DateTime policyGateOn, int maxConcurrentSessions, DateTime utcNow, CancellationToken cancellationToken);
		Task<int> TouchAsync(string sessionId, DateTime occurredOn, DateTime writeBefore, string ipAddress,
			string country, string region, string city, string userAgent, CancellationToken cancellationToken);
		Task<int> UpdateDepartmentAsync(string targetUserId, string sessionId, int departmentId,
			CancellationToken cancellationToken);
		Task<int> RevokeAsync(string targetUserId, string sessionId, string actorUserId, int reason, DateTime revokedOn, CancellationToken cancellationToken);
		Task<int> RevokeOthersAsync(string userId, string currentSessionId, int reason, DateTime revokedOn, CancellationToken cancellationToken);
		Task<int> RevokeAllAsync(string targetUserId, string actorUserId, int reason, DateTime revokedOn, CancellationToken cancellationToken);
		Task<int> RevokeDepartmentAsync(string targetUserId, int departmentId, int reason, DateTime revokedOn, CancellationToken cancellationToken);
		Task<int> PurgeInactiveBeforeAsync(DateTime historyBeforeUtc, CancellationToken cancellationToken);

		/// <summary>
		/// Locks an active, unlocked shared session still at <paramref name="expectedLockVersion"/> and advances its lock
		/// version, in one guarded statement (passkey plan section 12.5.3). Returns 1 when this call locked it, 0 when it
		/// was already locked, changed, ended, or is not shared.
		/// </summary>
		Task<int> TryLockAsync(string sessionId, long expectedLockVersion, int reason, DateTime lockedOnUtc, CancellationToken cancellationToken);

		/// <summary>
		/// Unlocks the user's active, unexpired, locked shared session only at <paramref name="expectedLockVersion"/>, and
		/// restarts its idle deadline. The lock version is not advanced, so one lock can be unlocked at most once. Returns 1
		/// when this call unlocked it.
		/// </summary>
		Task<int> TryUnlockAsync(string userId, string sessionId, long expectedLockVersion, DateTime unlockedOnUtc, CancellationToken cancellationToken);

		/// <summary>
		/// Records operator activity on an active, unlocked shared session, at most once per write interval, and never for a
		/// session whose idle deadline already passed (<paramref name="idleCutoff"/>): activity cannot revive a lapsed session.
		/// </summary>
		Task<int> RecordOperatorActivityAsync(string sessionId, DateTime occurredOnUtc, DateTime writeBefore, DateTime idleCutoff,
			CancellationToken cancellationToken);

		/// <summary>
		/// The state columns of the given sessions, in batches (id, state, expiry, creation, and the shared lock and idle
		/// fields), for the SignalR connection sweep. Missing sessions are simply absent.
		/// </summary>
		Task<IReadOnlyList<UserSession>> GetStatesAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken);

		/// <summary>
		/// Stops approval requests on the user's active Responder installation <paramref name="sessionId"/>, or on all of them
		/// when it is null (plan section 6.5). Returns how many installations stopped.
		/// </summary>
		Task<int> DisableApprovalsAsync(string userId, string sessionId, DateTime disabledOnUtc, CancellationToken cancellationToken);
	}
}
