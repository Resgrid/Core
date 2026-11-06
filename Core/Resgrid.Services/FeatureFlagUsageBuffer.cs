using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Resgrid.Services
{
	/// <summary>
	/// In-memory feature flag evaluation counters for one process, keyed by (flag, department, UTC day). Evaluations add to
	/// it; the usage flush drains it into FeatureFlagUsages and puts back whatever it failed to persist. Draining removes
	/// the entries, so the buffer only ever holds what was evaluated since the last successful flush.
	/// </summary>
	public sealed class FeatureFlagUsageBuffer
	{
		private readonly ConcurrentDictionary<(int FeatureFlagId, int DepartmentId, DateTime UsageDate), Counter> _counters =
			new ConcurrentDictionary<(int FeatureFlagId, int DepartmentId, DateTime UsageDate), Counter>();

		/// <summary>Number of (flag, department, day) entries waiting for the next flush.</summary>
		public int Count => _counters.Count;

		/// <summary>Counts one evaluation; a null <paramref name="isEnabled"/> (evaluation threw) counts towards neither state.</summary>
		public void Record(int featureFlagId, int departmentId, DateTime utcNow, bool? isEnabled)
		{
			Add((featureFlagId, departmentId, utcNow.Date), 1, isEnabled == true ? 1 : 0, isEnabled == false ? 1 : 0);
		}

		/// <summary>Removes and returns every pending entry.</summary>
		public List<PendingFeatureFlagUsage> Drain()
		{
			var drained = new List<PendingFeatureFlagUsage>();
			// Enumerating the dictionary itself takes no locks (Keys would lock every bucket against evaluations).
			foreach (var pair in _counters)
			{
				var key = pair.Key;
				if (!_counters.TryRemove(key, out var counter))
					continue;

				var (evaluations, enabled, disabled) = counter.Close();
				if (evaluations > 0 || enabled > 0 || disabled > 0)
					drained.Add(new PendingFeatureFlagUsage(key.FeatureFlagId, key.DepartmentId, key.UsageDate, evaluations, enabled, disabled));
			}

			return drained;
		}

		/// <summary>Puts a drained entry back (adding to any evaluations recorded since), so the next flush retries it.</summary>
		public void Restore(PendingFeatureFlagUsage usage)
		{
			Add((usage.FeatureFlagId, usage.DepartmentId, usage.UsageDate.Date), usage.EvaluationCount, usage.EnabledCount, usage.DisabledCount);
		}

		private void Add((int FeatureFlagId, int DepartmentId, DateTime UsageDate) key, long evaluations, long enabled, long disabled)
		{
			// A drain can remove and close the counter between the lookup and the add; it then belongs to that flush, so
			// look again and add to its replacement instead of losing the count.
			while (!_counters.GetOrAdd(key, _ => new Counter()).TryAdd(evaluations, enabled, disabled))
			{
			}
		}

		private sealed class Counter
		{
			private long _evaluations;
			private long _enabled;
			private long _disabled;
			private bool _closed;

			public bool TryAdd(long evaluations, long enabled, long disabled)
			{
				lock (this)
				{
					if (_closed)
						return false;

					_evaluations += evaluations;
					_enabled += enabled;
					_disabled += disabled;
					return true;
				}
			}

			public (long Evaluations, long Enabled, long Disabled) Close()
			{
				lock (this)
				{
					_closed = true;
					return (_evaluations, _enabled, _disabled);
				}
			}
		}
	}

	/// <summary>One drained (flag, department, UTC day) entry of a <see cref="FeatureFlagUsageBuffer"/>.</summary>
	public sealed class PendingFeatureFlagUsage
	{
		public PendingFeatureFlagUsage(int featureFlagId, int departmentId, DateTime usageDate, long evaluationCount, long enabledCount, long disabledCount)
		{
			FeatureFlagId = featureFlagId;
			DepartmentId = departmentId;
			UsageDate = usageDate;
			EvaluationCount = evaluationCount;
			EnabledCount = enabledCount;
			DisabledCount = disabledCount;
		}

		public int FeatureFlagId { get; }
		public int DepartmentId { get; }
		public DateTime UsageDate { get; }
		public long EvaluationCount { get; }
		public long EnabledCount { get; }
		public long DisabledCount { get; }
	}
}
