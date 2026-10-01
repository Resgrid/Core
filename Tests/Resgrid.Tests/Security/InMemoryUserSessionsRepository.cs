using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// UserSessions in memory for the shared-session tests. Each guarded statement mirrors the WHERE clause of
	/// UserSessionsRepository exactly and runs under one lock, so a test can race it the way two nodes race the database.
	/// Reads hand out copies, as a database read would.
	/// </summary>
	internal sealed class InMemoryUserSessionsRepository : IUserSessionsRepository
	{
		private readonly object _gate = new();
		public readonly List<UserSession> Rows = new();

		/// <summary>A fresh read of the row, as the next request would load it.</summary>
		public UserSession Row(string sessionId)
		{
			lock (_gate) return Copy(Rows.Single(r => r.UserSessionId == sessionId));
		}

		public void Add(UserSession session)
		{
			lock (_gate) Rows.Add(Copy(session));
		}

		private static readonly System.Reflection.MethodInfo Clone =
			typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

		private static UserSession Copy(UserSession s) => s == null ? null : (UserSession)Clone.Invoke(s, null);

		public Task<UserSession> GetByIdAsync(object id)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.SingleOrDefault(r => r.UserSessionId == (string)id)));
		}

		public Task<IReadOnlyList<UserSession>> GetActiveByUserAsync(string userId, DateTime utcNow)
		{
			lock (_gate)
				return Task.FromResult<IReadOnlyList<UserSession>>(Rows
					.Where(r => r.UserId == userId && r.State == (int)UserSessionState.Active && r.ExpiresOn > utcNow)
					.OrderByDescending(r => r.LastActiveOn).Select(Copy).ToList());
		}

		public Task<UserSession> GetByAuthorizationIdAsync(string authorizationId)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.SingleOrDefault(r => r.OpenIddictAuthorizationId == authorizationId)));
		}

		public Task<UserSession> InsertAsync(UserSession entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			lock (_gate) Rows.Add(Copy(entity));
			return Task.FromResult(entity);
		}

		public Task<bool> TryInsertWithinDepartmentSessionLimitAsync(UserSession session, int departmentId, DateTime policyGateOn, int maxConcurrentSessions,
			DateTime utcNow, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (Rows.Count(r => r.UserId == session.UserId && r.DepartmentId == departmentId && r.State == (int)UserSessionState.Active &&
						r.ExpiresOn > utcNow && r.CreatedOn >= policyGateOn) >= maxConcurrentSessions)
					return Task.FromResult(false);
				Rows.Add(Copy(session));
				return Task.FromResult(true);
			}
		}

		public Task<int> TouchAsync(string sessionId, DateTime occurredOn, DateTime writeBefore, string ipAddress, string country, string region, string city,
			string userAgent, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.UserSessionId == sessionId && r.State == (int)UserSessionState.Active && r.LastActiveOn <= writeBefore);
				if (row == null)
					return Task.FromResult(0);
				row.LastActiveOn = occurredOn;
				return Task.FromResult(1);
			}
		}

		public Task<int> TryLockAsync(string sessionId, long expectedLockVersion, int reason, DateTime lockedOnUtc, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.UserSessionId == sessionId && r.State == (int)UserSessionState.Active && r.SharedMode && !r.IsLocked &&
					r.LockVersion == expectedLockVersion);
				if (row == null)
					return Task.FromResult(0);
				row.IsLocked = true;
				row.LockVersion++;
				row.LockedOnUtc = lockedOnUtc;
				row.LockReason = reason;
				row.StateVersion++;
				return Task.FromResult(1);
			}
		}

		public Task<int> TryUnlockAsync(string userId, string sessionId, long expectedLockVersion, DateTime unlockedOnUtc, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.UserSessionId == sessionId && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
					r.State == (int)UserSessionState.Active && r.SharedMode && r.IsLocked && r.LockVersion == expectedLockVersion && r.ExpiresOn > unlockedOnUtc);
				if (row == null)
					return Task.FromResult(0);
				row.IsLocked = false;
				row.LastOperatorActivityOn = unlockedOnUtc;
				row.StateVersion++;
				return Task.FromResult(1);
			}
		}

		public Task<int> RecordOperatorActivityAsync(string sessionId, DateTime occurredOnUtc, DateTime writeBefore, DateTime idleCutoff,
			CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.UserSessionId == sessionId && r.State == (int)UserSessionState.Active && r.SharedMode && !r.IsLocked &&
					(r.LastOperatorActivityOn ?? r.CreatedOn) <= writeBefore && (r.LastOperatorActivityOn ?? r.CreatedOn) > idleCutoff);
				if (row == null)
					return Task.FromResult(0);
				row.LastOperatorActivityOn = occurredOnUtc;
				return Task.FromResult(1);
			}
		}

		public Task<int> UpdateDepartmentAsync(string targetUserId, string sessionId, int departmentId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.UserId == targetUserId && r.UserSessionId == sessionId && r.State == (int)UserSessionState.Active);
				if (row == null)
					return Task.FromResult(0);
				row.DepartmentId = departmentId;
				row.StateVersion++;
				return Task.FromResult(1);
			}
		}

		public Task<int> RevokeAsync(string targetUserId, string sessionId, string actorUserId, int reason, DateTime revokedOn, CancellationToken cancellationToken) =>
			RevokeWhere(r => r.UserId == targetUserId && r.UserSessionId == sessionId, actorUserId, reason, revokedOn);

		public Task<int> RevokeOthersAsync(string userId, string currentSessionId, int reason, DateTime revokedOn, CancellationToken cancellationToken) =>
			RevokeWhere(r => r.UserId == userId && r.UserSessionId != currentSessionId, userId, reason, revokedOn);

		public Task<int> RevokeAllAsync(string targetUserId, string actorUserId, int reason, DateTime revokedOn, CancellationToken cancellationToken) =>
			RevokeWhere(r => r.UserId == targetUserId, actorUserId, reason, revokedOn);

		public Task<int> RevokeDepartmentAsync(string targetUserId, int departmentId, int reason, DateTime revokedOn, CancellationToken cancellationToken) =>
			RevokeWhere(r => r.UserId == targetUserId && r.DepartmentId == departmentId, targetUserId, reason, revokedOn);

		private Task<int> RevokeWhere(Func<UserSession, bool> predicate, string actorUserId, int reason, DateTime revokedOn)
		{
			lock (_gate)
			{
				var rows = Rows.Where(r => predicate(r) && r.State == (int)UserSessionState.Active).ToList();
				foreach (var row in rows)
				{
					row.State = (int)UserSessionState.Revoked;
					row.StateVersion++;
					row.RevokedOn = revokedOn;
					row.RevokedByUserId = actorUserId;
					row.RevocationReason = reason;
				}

				return Task.FromResult(rows.Count);
			}
		}

		public Task<int> PurgeInactiveBeforeAsync(DateTime historyBeforeUtc, CancellationToken cancellationToken) => Task.FromResult(0);

		public Task<IReadOnlyList<UserSession>> GetStatesAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken)
		{
			lock (_gate)
				return Task.FromResult<IReadOnlyList<UserSession>>(Rows.Where(r => sessionIds.Contains(r.UserSessionId)).Select(Copy).ToList());
		}

		public Task<int> DisableApprovalsAsync(string userId, string sessionId, DateTime disabledOnUtc, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				var rows = Rows.Where(r => r.UserId == userId && r.State == (int)UserSessionState.Active &&
					r.ClientApplication == (int)UserSessionClientApplication.Responder && r.ApprovalsDisabledOnUtc == null &&
					(sessionId == null || r.UserSessionId == sessionId)).ToList();
				rows.ForEach(r => r.ApprovalsDisabledOnUtc = disabledOnUtc);
				return Task.FromResult(rows.Count);
			}
		}

		public Task<IEnumerable<UserSession>> GetAllAsync() => throw new NotSupportedException();
		public Task<IEnumerable<UserSession>> GetAllByDepartmentIdAsync(int departmentId) => throw new NotSupportedException();
		public Task<UserSession> UpdateAsync(UserSession entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
		public Task<bool> DeleteAsync(UserSession entity, CancellationToken cancellationToken) => throw new NotSupportedException();
		public Task<UserSession> SaveOrUpdateAsync(UserSession entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
		public Task<IEnumerable<UserSession>> GetAllByUserIdAsync(string userId) => throw new NotSupportedException();
		public Task<bool> DeleteMultipleAsync(UserSession entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}
}
