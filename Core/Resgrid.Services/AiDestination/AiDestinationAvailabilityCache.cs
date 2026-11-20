using System;
using System.Collections.Concurrent;

namespace Resgrid.Services.AiDestination
{
	/// <summary>
	/// Per-process memory of whether a department may use destination advice (the blocking outcome, or null when it may). The review
	/// triggers run on every call save and note, and the entitlement gate is an uncached Billing API call, so the answer is reused for
	/// <c>AiDestinationConfig.AvailabilityCacheSeconds</c>. Registered as a singleton; bounded so it can never grow without limit.
	/// </summary>
	public sealed class AiDestinationAvailabilityCache
	{
		private const int MaxEntries = 20000;
		private readonly ConcurrentDictionary<int, (string Outcome, DateTime ExpiresOnUtc)> _entries = new ConcurrentDictionary<int, (string, DateTime)>();

		/// <summary>True with the remembered outcome (null = available) when there is a live entry.</summary>
		public bool TryGet(int departmentId, DateTime nowUtc, out string outcome)
		{
			if (_entries.TryGetValue(departmentId, out var entry) && entry.ExpiresOnUtc > nowUtc)
			{
				outcome = entry.Outcome;
				return true;
			}

			outcome = null;
			return false;
		}

		public void Set(int departmentId, string outcome, DateTime expiresOnUtc)
		{
			if (_entries.Count >= MaxEntries)
				_entries.Clear();

			_entries[departmentId] = (outcome, expiresOnUtc);
		}

		public void Remove(int departmentId) => _entries.TryRemove(departmentId, out _);
	}
}
