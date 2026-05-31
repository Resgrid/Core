using System.Collections.Generic;

namespace Resgrid.Config
{
	/// <summary>
	/// Global configuration for the built-in feature toggle subsystem. Loaded by ConfigProcessor via
	/// reflection (keys: "FeatureFlagsConfig.Field" in JSON or "RESGRID:FeatureFlagsConfig:Field" env).
	/// </summary>
	public static class FeatureFlagsConfig
	{
		/// <summary>
		/// Master switch for the whole subsystem. When false, evaluations short-circuit to the
		/// caller-supplied (or code-registered) default and no flag/override data is consulted.
		/// </summary>
		public static bool FeatureFlagsEnabled = true;

		/// <summary>
		/// How long the flag set and per-department overrides are cached. Flag/override writes invalidate
		/// the relevant cache immediately, so this can be generous.
		/// </summary>
		public static int CacheDurationMinutes = 60;

		/// <summary>
		/// When true, evaluations increment in-memory counters that the usage-flush worker persists to
		/// FeatureFlagUsages and uses to refresh LastEvaluatedOn (for stale-flag detection).
		/// </summary>
		public static bool TrackEvaluations = true;

		/// <summary>How often the usage-flush worker drains the in-memory evaluation counters.</summary>
		public static int EvaluationFlushIntervalSeconds = 60;

		/// <summary>Non-permanent flags not evaluated within this many days are reported as stale.</summary>
		public static int StaleFlagThresholdDays = 90;

		/// <summary>
		/// Code-registered boolean defaults keyed by flag key. Used as the fallback when a flag has not
		/// yet been seeded in the database, so new flags behave predictably before they exist as rows.
		/// </summary>
		public static Dictionary<string, bool> CodeDefaults = new Dictionary<string, bool>();
	}
}
