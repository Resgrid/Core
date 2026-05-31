using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Built-in feature toggle service. Resolves a flag's value for a department through a layered
	/// evaluation ladder (subsystem switch, archive, environment, schedule, prerequisites, per-department
	/// override, optional plan gate, targeting rules, then global default + percentage rollout) and
	/// provides flag/override/targeting/prerequisite management plus evaluation analytics. All reads on
	/// the evaluation hot path are served from cache.
	/// </summary>
	public interface IFeatureToggleService
	{
		#region Evaluation (hot path)

		/// <summary>Returns whether a flag is enabled for a department, falling back to defaultValue when unknown.</summary>
		Task<bool> IsEnabledAsync(string key, int departmentId, bool defaultValue = false, IDictionary<string, string> context = null);

		/// <summary>Full evaluation including the value and the reason (source) it resolved that way.</summary>
		Task<FeatureFlagEvaluation> EvaluateAsync(string key, int departmentId, IDictionary<string, string> context = null);

		/// <summary>Resolved string value for a multivariate flag (or defaultValue when off/unknown).</summary>
		Task<string> GetValueAsync(string key, int departmentId, string defaultValue = null, IDictionary<string, string> context = null);

		/// <summary>Evaluates every active flag for a department — used by the bulk poll API.</summary>
		Task<List<FeatureFlagEvaluation>> EvaluateAllForDepartmentAsync(int departmentId, IDictionary<string, string> context = null);

		/// <summary>A stable hash of a department's full resolved flag state, for ETag/304 polling.</summary>
		Task<string> GetDepartmentFlagStateHashAsync(int departmentId);

		#endregion

		#region Flag management (SystemAdmin)

		Task<List<FeatureFlag>> GetAllFlagsAsync(bool includeArchived = false, bool bypassCache = false);

		Task<FeatureFlag> GetFlagByKeyAsync(string key, bool bypassCache = false);

		/// <summary>Creates or updates a flag definition (matched by FlagKey). Audited.</summary>
		Task<FeatureFlag> SaveFlagAsync(FeatureFlag flag, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> ArchiveFlagAsync(string key, string userId, bool archived = true, CancellationToken cancellationToken = default(CancellationToken));

		Task<FeatureFlag> SetGlobalEnabledAsync(string key, bool enabled, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<FeatureFlag> SetRolloutPercentageAsync(string key, int percentage, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeleteFlagAsync(string key, string userId, CancellationToken cancellationToken = default(CancellationToken));

		#endregion

		#region Override management (SystemAdmin or department admin for own department)

		Task<List<FeatureFlagOverride>> GetOverridesForFlagAsync(string key);

		Task<List<FeatureFlagOverride>> GetOverridesForDepartmentAsync(int departmentId, bool bypassCache = false);

		/// <summary>Creates or updates a department's override for a flag. Audited against the department.</summary>
		Task<FeatureFlagOverride> SetDepartmentOverrideAsync(string key, int departmentId, bool isEnabled, string value, string reason, DateTime? expiresOn, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> RemoveDepartmentOverrideAsync(string key, int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		#endregion

		#region Targeting rules & prerequisites (SystemAdmin)

		Task<List<FeatureFlagTargetingRule>> GetTargetingRulesForFlagAsync(string key);

		Task<FeatureFlagTargetingRule> SaveTargetingRuleAsync(FeatureFlagTargetingRule rule, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> RemoveTargetingRuleAsync(int targetingRuleId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<FeatureFlagPrerequisite>> GetPrerequisitesForFlagAsync(string key);

		/// <summary>Adds a prerequisite edge. Throws if it would introduce a cycle.</summary>
		Task<FeatureFlagPrerequisite> AddPrerequisiteAsync(string key, string requiredKey, string requiredValue, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> RemovePrerequisiteAsync(int prerequisiteId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		#endregion

		#region Analytics & lifecycle

		/// <summary>Persists buffered evaluation counts and refreshes LastEvaluatedOn. Called by the worker.</summary>
		Task<int> FlushEvaluationsAsync(CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Aggregated daily usage rows for a flag within a date range (admin analytics).</summary>
		Task<List<FeatureFlagUsage>> GetUsageForFlagAsync(string key, DateTime from, DateTime to);

		/// <summary>Non-permanent flags whose LastEvaluatedOn is older than the threshold (or never evaluated).</summary>
		Task<List<FeatureFlag>> GetStaleFlagsAsync(int? olderThanDays = null);

		#endregion

		#region Cache invalidation

		Task InvalidateFlagCacheAsync();

		Task InvalidateDepartmentOverrideCacheAsync(int departmentId);

		#endregion
	}
}
