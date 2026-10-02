using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Single-process stand-in with the same observable rules as UserMfaStateRepository (strictly increasing TOTP steps,
	/// one redemption per code, compare-and-delete legacy import). Real cross-node atomicity is covered by
	/// MfaFactorStateDatabaseTests.
	/// </summary>
	internal sealed class InMemoryUserMfaStateRepository : IUserMfaStateRepository
	{
		private readonly object _gate = new();
		public readonly Dictionary<string, UserTotpState> Totp = new();
		public readonly List<(string UserId, byte[] Hash, DateTime? UsedOn)> Codes = new();

		/// <summary>The legacy AspNetUserTokens value per user; null means no legacy token.</summary>
		public readonly Dictionary<string, string> LegacyTokens = new();

		public Exception ThrowOnConsume { get; set; }
		public int ConsumeCalls { get; private set; }

		public Task<bool> TryConsumeTotpTimeStepAsync(string userId, long timeStep, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				ConsumeCalls++;
				if (ThrowOnConsume != null) throw ThrowOnConsume;
				if (Totp.TryGetValue(userId, out var state) && state.LastAcceptedTimeStep >= timeStep)
					return Task.FromResult(false);

				Totp[userId] = new UserTotpState { UserId = userId, LastAcceptedTimeStep = timeStep, LastAcceptedOnUtc = utcNow, EnrolledOnUtc = state?.EnrolledOnUtc };
				return Task.FromResult(true);
			}
		}

		public Task<UserTotpState> GetTotpStateAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Totp.TryGetValue(userId, out var state) ? state : null);
		}

		public Task RecordTotpEnrollmentAsync(string userId, DateTime utcNow, TotpEnrollmentContext context, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				if (Totp.TryGetValue(userId, out var state))
				{
					state.EnrolledOnUtc = utcNow;
					state.EnrolledInSharedMode = context?.SharedMode == true;
					state.EnrolledClientApplication = context?.ClientApplication;
					state.EnrolledInstallation = context?.Installation;
				}
			return Task.CompletedTask;
		}

		public Task<int> CountUnusedRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Codes.Count(c => c.UserId == userId && c.UsedOn == null));
		}

		public Task<bool> TryRedeemRecoveryCodeAsync(string userId, byte[] codeHash, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var index = Codes.FindIndex(c => c.UserId == userId && c.UsedOn == null && c.Hash.AsSpan().SequenceEqual(codeHash));
				if (index < 0) return Task.FromResult(false);
				Codes[index] = (userId, codeHash, utcNow);
				return Task.FromResult(true);
			}
		}

		public Task ReplaceRecoveryCodesAsync(string userId, IReadOnlyCollection<byte[]> codeHashes, int hashVersion, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				LegacyTokens.Remove(userId);
				Codes.RemoveAll(c => c.UserId == userId);
				Codes.AddRange(codeHashes.Select(h => (userId, h, (DateTime?)null)));
			}
			return Task.CompletedTask;
		}

		public Task<bool> ImportLegacyRecoveryCodesAsync(string userId, string legacyTokenValue, IReadOnlyCollection<byte[]> codeHashes,
			int hashVersion, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				if (!LegacyTokens.TryGetValue(userId, out var current) || current != legacyTokenValue)
					return Task.FromResult(false);

				LegacyTokens.Remove(userId);
				Codes.AddRange(codeHashes.Select(h => (userId, h, (DateTime?)null)));
				return Task.FromResult(true);
			}
		}

		public readonly Dictionary<string, int> Preferences = new();

		public Task<int?> GetPreferredMethodAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult(Preferences.TryGetValue(userId, out var method) ? method : (int?)null);
		}

		public Task SetPreferredMethodAsync(string userId, int method, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				Preferences[userId] = method;
			return Task.CompletedTask;
		}
	}
}
