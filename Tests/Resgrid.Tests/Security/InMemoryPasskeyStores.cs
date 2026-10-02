using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Single-process stand-in with the same observable rules as UserPasskeyRepository: one credential per (RP, id hash)
	/// across users, owner-guarded changes, and a use recorded only against the counter it was verified with. Real
	/// cross-node atomicity is covered by PasskeyDatabaseTests.
	/// </summary>
	internal sealed class InMemoryUserPasskeyRepository : IUserPasskeyRepository
	{
		private readonly object _gate = new();
		public readonly List<UserPasskey> Rows = new();

		public Task<bool> TryInsertAsync(UserPasskey passkey, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				if (Rows.Any(r => r.RpId == passkey.RpId && r.CredentialIdHash.AsSpan().SequenceEqual(passkey.CredentialIdHash)))
					return Task.FromResult(false);
				Rows.Add(Copy(passkey));
				return Task.FromResult(true);
			}
		}

		public Task<UserPasskey> GetAsync(string userPasskeyId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.FirstOrDefault(r => r.UserPasskeyId == userPasskeyId)));
		}

		public Task<UserPasskey> GetActiveByCredentialAsync(string rpId, byte[] credentialIdHash, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult(Copy(Rows.FirstOrDefault(r => r.RpId == rpId && r.RevokedOnUtc == null
					&& r.CredentialIdHash.AsSpan().SequenceEqual(credentialIdHash))));
		}

		public Task<IReadOnlyList<UserPasskey>> GetActiveForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult<IReadOnlyList<UserPasskey>>(Rows.Where(r => r.UserId == userId && r.RevokedOnUtc == null).Select(Copy).ToList());
		}

		public Task<int> CountActiveForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.Count(r => r.UserId == userId && r.RevokedOnUtc == null));
		}

		public Task<byte[]> GetUserHandleAsync(string userId, string rpId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult(Rows.Where(r => r.UserId == userId && r.RpId == rpId).OrderBy(r => r.CreatedOnUtc)
					.Select(r => r.UserHandle).FirstOrDefault());
		}

		public Task<bool> TryRenameAsync(string userPasskeyId, string userId, string displayName, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Active(userPasskeyId, userId);
				if (row == null) return Task.FromResult(false);
				row.DisplayName = displayName;
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryRevokeAsync(string userPasskeyId, string userId, PasskeyRevocationReason reason, string actorUserId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Active(userPasskeyId, userId);
				if (row == null) return Task.FromResult(false);
				Revoke(row, reason, actorUserId, utcNow);
				return Task.FromResult(true);
			}
		}

		public Task<IReadOnlyList<string>> RevokeAllForClientAsync(string userId, int clientApplication, PasskeyRevocationReason reason,
			string actorUserId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var rows = Rows.Where(r => r.UserId == userId && r.ClientApplication == clientApplication && r.RevokedOnUtc == null).ToList();
				rows.ForEach(r => Revoke(r, reason, actorUserId, utcNow));
				return Task.FromResult<IReadOnlyList<string>>(rows.Select(r => r.UserPasskeyId).ToList());
			}
		}

		public Task<bool> TryRecordUseAsync(string userPasskeyId, long expectedSignCount, long newSignCount, bool isBackedUp, int clientApplication,
			string installation, bool sharedMode, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.FirstOrDefault(r => r.UserPasskeyId == userPasskeyId && r.RevokedOnUtc == null && r.SignCount == expectedSignCount);
				if (row == null) return Task.FromResult(false);
				row.SignCount = newSignCount;
				row.IsBackedUp = isBackedUp;
				row.LastUsedOnUtc = utcNow;
				row.LastUsedClientApplication = clientApplication;
				row.LastUsedInstallation = installation;
				row.LastUsedInSharedMode = sharedMode;
				return Task.FromResult(true);
			}
		}

		public Task<bool> TrySetApprovalEnabledAsync(string userPasskeyId, string userId, bool enabled, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Active(userPasskeyId, userId);
				if (row == null) return Task.FromResult(false);
				row.ApprovalEnabled = enabled;
				row.StateVersion++;
				return Task.FromResult(true);
			}
		}

		private UserPasskey Active(string id, string userId) =>
			Rows.FirstOrDefault(r => r.UserPasskeyId == id && r.UserId == userId && r.RevokedOnUtc == null);

		private static void Revoke(UserPasskey row, PasskeyRevocationReason reason, string actor, DateTime utcNow)
		{
			row.RevokedOnUtc = utcNow;
			row.RevocationReason = (int)reason;
			row.RevokedByUserId = actor;
			row.ApprovalEnabled = false;
			row.StateVersion++;
		}

		private static UserPasskey Copy(UserPasskey row) => row == null ? null : (UserPasskey)typeof(object)
			.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(row, null);
	}

	/// <summary>Same observable rules as AuthenticationChallengeRepository.</summary>
	internal sealed class InMemoryAuthenticationChallengeRepository : IAuthenticationChallengeRepository
	{
		private readonly object _gate = new();
		public readonly Dictionary<string, AuthenticationChallenge> Rows = new();

		public Task InsertAsync(AuthenticationChallenge challenge, CancellationToken cancellationToken = default)
		{
			lock (_gate) Rows[challenge.AuthenticationChallengeId] = challenge;
			return Task.CompletedTask;
		}

		public Task<AuthenticationChallenge> GetAsync(string challengeId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				if (!Rows.TryGetValue(challengeId, out var row)) return Task.FromResult<AuthenticationChallenge>(null);
				return Task.FromResult(new AuthenticationChallenge
				{
					AuthenticationChallengeId = row.AuthenticationChallengeId, UserId = row.UserId, Purpose = row.Purpose,
					ClientApplication = row.ClientApplication, RpId = row.RpId, ParentKind = row.ParentKind, ParentId = row.ParentId,
					DepartmentId = row.DepartmentId, AuthenticationGeneration = row.AuthenticationGeneration, LockVersion = row.LockVersion,
					OptionsJson = row.OptionsJson, CreatedOnUtc = row.CreatedOnUtc, ExpiresOnUtc = row.ExpiresOnUtc, Attempts = row.Attempts,
					MaxAttempts = row.MaxAttempts, State = row.State, ConsumedOnUtc = row.ConsumedOnUtc
				});
			}
		}

		public Task<int> CountPendingForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult(Rows.Values.Count(r => r.UserId == userId && r.State == (int)AuthenticationChallengeState.Pending && r.ExpiresOnUtc > utcNow));
		}

		public Task<bool> TryConsumeAsync(string challengeId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				if (!Rows.TryGetValue(challengeId, out var row) || row.State != (int)AuthenticationChallengeState.Pending || row.ExpiresOnUtc <= utcNow
					|| row.Attempts >= row.MaxAttempts)
					return Task.FromResult(false);
				row.State = (int)AuthenticationChallengeState.Consumed;
				row.ConsumedOnUtc = utcNow;
				return Task.FromResult(true);
			}
		}

		public Task RecordFailedAttemptAsync(string challengeId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				if (Rows.TryGetValue(challengeId, out var row) && row.State == (int)AuthenticationChallengeState.Pending)
				{
					row.Attempts++;
					if (row.Attempts >= row.MaxAttempts)
						row.State = (int)AuthenticationChallengeState.Exhausted;
				}
			return Task.CompletedTask;
		}

		public Task<int> CancelPendingForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var pending = Rows.Values.Where(r => r.UserId == userId && r.State == (int)AuthenticationChallengeState.Pending).ToList();
				pending.ForEach(r => r.State = (int)AuthenticationChallengeState.Canceled);
				return Task.FromResult(pending.Count);
			}
		}

		public Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var expired = Rows.Values.Where(r => r.ExpiresOnUtc < utcCutoff).Select(r => r.AuthenticationChallengeId).ToList();
				expired.ForEach(id => Rows.Remove(id));
				return Task.FromResult(expired.Count);
			}
		}
	}

	/// <summary>Same observable rules as UserSessionMfaEvidenceRepository.</summary>
	internal sealed class InMemoryMfaEvidenceRepository : IUserSessionMfaEvidenceRepository
	{
		private readonly object _gate = new();
		public readonly List<MfaEvidence> Rows = new();

		public Task InsertAsync(MfaEvidence evidence, CancellationToken cancellationToken = default)
		{
			lock (_gate) Rows.Add(evidence);
			return Task.CompletedTask;
		}

		public Task<MfaEvidence> GetLatestAsync(string userId, string sessionKey, MfaEvidenceKind kind, long authenticationGeneration, DateTime utcNow,
			CancellationToken cancellationToken = default) =>
			Latest(userId, sessionKey, kind, null, authenticationGeneration, utcNow);

		public Task<MfaEvidence> GetLatestForPurposeAsync(string userId, string sessionKey, MfaEvidenceKind kind, MfaEvidencePurpose purpose,
			long authenticationGeneration, DateTime utcNow, CancellationToken cancellationToken = default) =>
			Latest(userId, sessionKey, kind, purpose, authenticationGeneration, utcNow);

		public Task<int> RevokeForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default) =>
			Revoke(r => r.UserId == userId, utcNow);

		public Task<int> RevokeForFactorAsync(string userId, string factorReference, DateTime utcNow, CancellationToken cancellationToken = default) =>
			Revoke(r => r.UserId == userId && r.FactorReference == factorReference, utcNow);

		public Task<int> RevokeByFactorReferenceAsync(string factorReference, DateTime utcNow, CancellationToken cancellationToken = default) =>
			Revoke(r => r.FactorReference == factorReference, utcNow);

		public Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.ExpiresOnUtc < utcCutoff));
		}

		private Task<MfaEvidence> Latest(string userId, string sessionKey, MfaEvidenceKind kind, MfaEvidencePurpose? purpose, long generation, DateTime utcNow)
		{
			lock (_gate)
				return Task.FromResult(Rows
					.Where(r => r.UserId == userId && r.SessionKey == sessionKey && r.Kind == (int)kind && r.AuthenticationGeneration == generation
						&& r.RevokedOnUtc == null && r.ExpiresOnUtc > utcNow && (purpose == null || r.Purpose == (int)purpose))
					.OrderByDescending(r => r.VerifiedOnUtc)
					.FirstOrDefault());
		}

		private Task<int> Revoke(Func<MfaEvidence, bool> match, DateTime utcNow)
		{
			lock (_gate)
			{
				var rows = Rows.Where(r => r.RevokedOnUtc == null && match(r)).ToList();
				rows.ForEach(r => r.RevokedOnUtc = utcNow);
				return Task.FromResult(rows.Count);
			}
		}
	}
}
