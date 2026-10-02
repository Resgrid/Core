using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	public interface IUserSessionService
	{
		Task<UserSession> CreateSessionAsync(SessionIssueContext context, CancellationToken cancellationToken = default);
		Task<SessionValidationResult> ValidateAsync(SessionPrincipalContext context, CancellationToken cancellationToken = default);
		Task<UserSession> AdoptLegacyAsync(LegacySessionContext context, CancellationToken cancellationToken = default);
		Task TouchAsync(string sessionId, RequestActivity activity, CancellationToken cancellationToken = default);
		/// <summary>
		/// Whether the session's recorded activity is stale enough to be worth writing. Callers that already
		/// hold the session should gate <see cref="TouchAsync"/> on this: the write itself is guarded by the
		/// same interval server-side, so calling through on a fresh session costs a location lookup and a
		/// database round trip to update no rows.
		/// </summary>
		bool ShouldRecordActivity(UserSession session, DateTime occurredOn);
		Task<bool> MoveSessionToDepartmentAsync(string userId, string sessionId, int departmentId,
			CancellationToken cancellationToken = default);
		Task<IReadOnlyList<UserSessionSummary>> GetActiveForUserAsync(string userId, CancellationToken cancellationToken = default);
		Task<RevocationResult> RevokeSessionAsync(string actorUserId, string targetUserId, string sessionId, UserSessionRevocationReason reason, CancellationToken cancellationToken = default);
		Task<RevocationResult> RevokeOtherSessionsAsync(string userId, string currentSessionId, UserSessionRevocationReason reason, CancellationToken cancellationToken = default);
		Task<RevocationResult> RevokeAllAsync(string actorUserId, string targetUserId, UserSessionRevocationReason reason, DateTime validAfterUtc, CancellationToken cancellationToken = default);
		Task<RevocationResult> RevokeAllAfterCredentialChangeAsync(string actorUserId, string targetUserId, UserSessionRevocationReason reason, DateTime validAfterUtc, CancellationToken cancellationToken = default);
		/// <summary>
		/// Moves an unlocked shared session's idle deadline to now, because the operator did something (the client says so
		/// with <c>X-Resgrid-Operator-Activity</c>; polling and sockets never do). Written at most once per
		/// <c>PasskeyConfig.SharedActivityWriteIntervalSeconds</c>, and never for a session whose deadline already passed.
		/// </summary>
		Task RecordOperatorActivityAsync(UserSession session, CancellationToken cancellationToken = default);

		/// <summary>
		/// Which of the given sessions can no longer be used: missing, ended, expired, locked, or a shared session past its
		/// recorded idle deadline. For closing open SignalR connections in bulk (slice 16); a request still gets the full
		/// validation, which also applies a stricter current department policy.
		/// </summary>
		Task<IReadOnlySet<string>> GetUnusableSessionIdsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default);

		/// <summary>Asks every SignalR host to close this session's open connections now; best effort.</summary>
		Task CloseConnectionsAsync(string sessionId);

		Task<RevocationResult> RevokeDepartmentSessionsAsync(string targetUserId, int departmentId, UserSessionRevocationReason reason, CancellationToken cancellationToken = default);
	}
}
