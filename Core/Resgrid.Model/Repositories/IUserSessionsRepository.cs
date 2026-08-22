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
	}
}
