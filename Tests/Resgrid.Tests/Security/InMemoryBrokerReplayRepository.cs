using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The broker replay store's contract in memory: a key is claimed once, even after it expires, until it is purged.
	/// MfaEvidenceDatabaseTests-style database tests prove the SQL on both engines.
	/// </summary>
	internal sealed class InMemoryBrokerReplayRepository : IBrokerReplayRepository
	{
		private readonly ConcurrentDictionary<string, DateTime> _keys = new(StringComparer.Ordinal);

		public bool Faulted { get; set; }

		public int Count => _keys.Count;

		public Task<bool> TryClaimAsync(string replayKey, BrokerReplayKind kind, DateTime expiresOnUtc, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			if (Faulted)
				throw new TimeoutException("replay store unavailable");
			return Task.FromResult(_keys.TryAdd(replayKey, expiresOnUtc));
		}

		public Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			var expired = _keys.Where(pair => pair.Value < utcCutoff).Select(pair => pair.Key).ToList();
			foreach (var key in expired)
				_keys.TryRemove(key, out _);
			return Task.FromResult(expired.Count);
		}
	}
}
