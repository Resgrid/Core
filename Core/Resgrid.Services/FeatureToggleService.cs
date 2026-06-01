using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Built-in feature toggle service. See <see cref="IFeatureToggleService"/> for the contract. The
	/// evaluation hot path is served entirely from cache; management writes invalidate the relevant
	/// cache entries and emit audit events.
	/// </summary>
	public class FeatureToggleService : IFeatureToggleService
	{
		private const string AllFlagsCacheKey = "FeatureFlags_All";
		private const string AllRulesCacheKey = "FeatureFlagTargetingRules_All";
		private const string AllPrereqsCacheKey = "FeatureFlagPrereqs_All";
		private const string DepartmentOverridesCacheKey = "FeatureFlagOverrides_{0}";

		// Static so the in-memory evaluation counters survive across (per-scope) service instances.
		private static readonly ConcurrentDictionary<string, long[]> _usageBuffer = new ConcurrentDictionary<string, long[]>();

		private readonly IFeatureFlagRepository _featureFlagRepository;
		private readonly IFeatureFlagOverrideRepository _featureFlagOverrideRepository;
		private readonly IFeatureFlagTargetingRuleRepository _featureFlagTargetingRuleRepository;
		private readonly IFeatureFlagPrerequisiteRepository _featureFlagPrerequisiteRepository;
		private readonly IFeatureFlagUsageRepository _featureFlagUsageRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IDepartmentsService _departmentsService;

		public FeatureToggleService(IFeatureFlagRepository featureFlagRepository, IFeatureFlagOverrideRepository featureFlagOverrideRepository,
			IFeatureFlagTargetingRuleRepository featureFlagTargetingRuleRepository, IFeatureFlagPrerequisiteRepository featureFlagPrerequisiteRepository,
			IFeatureFlagUsageRepository featureFlagUsageRepository, ICacheProvider cacheProvider, IEventAggregator eventAggregator,
			ISubscriptionsService subscriptionsService, IDepartmentsService departmentsService)
		{
			_featureFlagRepository = featureFlagRepository;
			_featureFlagOverrideRepository = featureFlagOverrideRepository;
			_featureFlagTargetingRuleRepository = featureFlagTargetingRuleRepository;
			_featureFlagPrerequisiteRepository = featureFlagPrerequisiteRepository;
			_featureFlagUsageRepository = featureFlagUsageRepository;
			_cacheProvider = cacheProvider;
			_eventAggregator = eventAggregator;
			_subscriptionsService = subscriptionsService;
			_departmentsService = departmentsService;
		}

		private static TimeSpan CacheLength => TimeSpan.FromMinutes(FeatureFlagsConfig.CacheDurationMinutes <= 0 ? 60 : FeatureFlagsConfig.CacheDurationMinutes);

		#region Evaluation (hot path)

		public async Task<bool> IsEnabledAsync(string key, int departmentId, bool defaultValue = false, IDictionary<string, string> context = null)
		{
			var evaluation = await EvaluateInternalAsync(key, departmentId, context, defaultValue, new HashSet<int>());
			return evaluation.IsEnabled;
		}

		public async Task<FeatureFlagEvaluation> EvaluateAsync(string key, int departmentId, IDictionary<string, string> context = null)
		{
			return await EvaluateInternalAsync(key, departmentId, context, false, new HashSet<int>());
		}

		public async Task<string> GetValueAsync(string key, int departmentId, string defaultValue = null, IDictionary<string, string> context = null)
		{
			var evaluation = await EvaluateInternalAsync(key, departmentId, context, false, new HashSet<int>());
			if (evaluation.Source == FeatureFlagEvaluationSource.NotFound)
				return defaultValue;

			return evaluation.Value ?? defaultValue;
		}

		public async Task<List<FeatureFlagEvaluation>> EvaluateAllForDepartmentAsync(int departmentId, IDictionary<string, string> context = null)
		{
			var results = new List<FeatureFlagEvaluation>();
			var flags = await GetAllFlagsAsync(includeArchived: false);

			foreach (var flag in flags)
			{
				results.Add(await EvaluateInternalAsync(flag.FlagKey, departmentId, context, false, new HashSet<int>()));
			}

			return results;
		}

		public async Task<string> GetDepartmentFlagStateHashAsync(int departmentId)
		{
			var evaluations = await EvaluateAllForDepartmentAsync(departmentId);
			return ComputeStateHash(evaluations);
		}

		private async Task<FeatureFlagEvaluation> EvaluateInternalAsync(string key, int departmentId, IDictionary<string, string> context, bool defaultValue, HashSet<int> visited)
		{
			// 0) Subsystem master switch.
			if (!FeatureFlagsConfig.FeatureFlagsEnabled)
				return BuildDefault(key, defaultValue, FeatureFlagEvaluationSource.SubsystemDisabled);

			var flags = await GetAllFlagsAsync(includeArchived: true);
			var flag = flags.FirstOrDefault(f => string.Equals(f.FlagKey, key, StringComparison.OrdinalIgnoreCase));

			// 1) Unknown flag -> caller/code default.
			if (flag == null)
				return BuildDefault(key, defaultValue, FeatureFlagEvaluationSource.NotFound);

			// Guard against prerequisite cycles (the graph is validated acyclic at write time, but be safe).
			if (!visited.Add(flag.FeatureFlagId))
				return Build(flag, false, null, FeatureFlagEvaluationSource.Prerequisite);

			FeatureFlagEvaluation evaluation = null;
			try
			{
				// 2) Archived.
				if (flag.IsArchived)
					return evaluation = Build(flag, false, flag.OffValue, FeatureFlagEvaluationSource.Archived);

				// 3) Environment scope.
				if (flag.Environment.HasValue && flag.Environment.Value != (int)SystemBehaviorConfig.Environment)
					return evaluation = Build(flag, false, flag.OffValue, FeatureFlagEvaluationSource.GlobalDefault);

				// 4) Scheduling window.
				var now = DateTime.UtcNow;
				if ((flag.EnableOn.HasValue && now < flag.EnableOn.Value) || (flag.DisableOn.HasValue && now >= flag.DisableOn.Value))
					return evaluation = Build(flag, false, flag.OffValue, FeatureFlagEvaluationSource.Schedule);

				// 5) Prerequisites.
				var prerequisites = (await GetAllPrerequisitesAsync()).Where(p => p.FeatureFlagId == flag.FeatureFlagId).ToList();
				foreach (var prerequisite in prerequisites)
				{
					var required = flags.FirstOrDefault(f => f.FeatureFlagId == prerequisite.RequiredFeatureFlagId);
					if (required == null)
						continue;

					var requiredEvaluation = await EvaluateInternalAsync(required.FlagKey, departmentId, context, false, visited);
					var satisfied = string.IsNullOrEmpty(prerequisite.RequiredValue)
						? requiredEvaluation.IsEnabled
						: requiredEvaluation.IsEnabled && string.Equals(requiredEvaluation.Value, prerequisite.RequiredValue, StringComparison.OrdinalIgnoreCase);

					if (!satisfied)
						return evaluation = Build(flag, false, flag.OffValue, FeatureFlagEvaluationSource.Prerequisite);
				}

				// 6) Per-department override (explicit, non-expired) wins over rollout/targeting.
				var overrides = await GetOverridesForDepartmentAsync(departmentId);
				var departmentOverride = overrides.FirstOrDefault(o => o.FeatureFlagId == flag.FeatureFlagId);
				if (departmentOverride != null && (!departmentOverride.ExpiresOn.HasValue || departmentOverride.ExpiresOn.Value > now))
					return evaluation = Build(flag, departmentOverride.IsEnabled, departmentOverride.FlagValue, FeatureFlagEvaluationSource.Override);

				// 7) Optional plan gate.
				if (flag.MinimumPlanType.HasValue)
				{
					var gatePassed = await PassesPlanGateAsync(flag.MinimumPlanType.Value, departmentId);
					if (!gatePassed)
						return evaluation = Build(flag, false, flag.OffValue, FeatureFlagEvaluationSource.PlanGate);
				}

				// 8) Targeting rules (first match by priority).
				var rules = (await GetAllTargetingRulesAsync()).Where(r => r.FeatureFlagId == flag.FeatureFlagId).OrderBy(r => r.Priority).ToList();
				foreach (var rule in rules)
				{
					if (!await RuleMatchesAsync(rule, departmentId, context))
						continue;

					// Optional rollout within the matched segment.
					if (rule.RolloutPercentage.HasValue && rule.RolloutPercentage.Value < 100)
					{
						if (rule.RolloutPercentage.Value <= 0)
							continue;
						if (StableBucket(flag.FlagKey + ":" + departmentId + ":rule" + rule.FeatureFlagTargetingRuleId) >= rule.RolloutPercentage.Value)
							continue;
					}

					return evaluation = Build(flag, rule.ResultEnabled, rule.ResultValue, FeatureFlagEvaluationSource.TargetingRule, rule.FeatureFlagTargetingRuleId);
				}

				// 9) Global default + percentage rollout.
				if (!flag.IsEnabledGlobally)
					return evaluation = Build(flag, false, flag.OffValue, FeatureFlagEvaluationSource.GlobalDefault);

				if (flag.RolloutPercentage.HasValue && flag.RolloutPercentage.Value < 100)
				{
					var enabled = flag.RolloutPercentage.Value > 0 && StableBucket(flag.FlagKey + ":" + departmentId) < flag.RolloutPercentage.Value;
					return evaluation = Build(flag, enabled, enabled ? flag.DefaultValue : flag.OffValue, FeatureFlagEvaluationSource.GlobalRollout);
				}

				return evaluation = Build(flag, true, flag.DefaultValue, FeatureFlagEvaluationSource.GlobalDefault);
			}
			finally
			{
				visited.Remove(flag.FeatureFlagId);
				RecordEvaluation(flag.FeatureFlagId, departmentId, evaluation?.IsEnabled);
			}
		}

		private async Task<bool> PassesPlanGateAsync(int minimumPlanType, int departmentId)
		{
			try
			{
				var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId, false);

				// When billing is not configured (no plan), do not gate - the flag behaves as a normal toggle.
				if (plan == null)
					return true;

				return plan.PlanId >= minimumPlanType;
			}
			catch (Exception ex)
			{
				// Don't let a billing outage break flag evaluation; log and treat the gate as open.
				Logging.LogException(ex, $"FeatureToggle plan gate check failed for department {departmentId}");
				return true;
			}
		}

		private async Task<bool> RuleMatchesAsync(FeatureFlagTargetingRule rule, int departmentId, IDictionary<string, string> context)
		{
			var attribute = (FeatureFlagAttributeTypes)rule.AttributeType;
			var op = (FeatureFlagOperatorTypes)rule.OperatorType;

			string attributeValue;
			string compareValue = rule.ComparisonValue;

			if (attribute == FeatureFlagAttributeTypes.Custom)
			{
				// ComparisonValue is "contextKey:expectedValue".
				var raw = rule.ComparisonValue ?? string.Empty;
				var idx = raw.IndexOf(':');
				var contextKey = idx >= 0 ? raw.Substring(0, idx) : raw;
				compareValue = idx >= 0 ? raw.Substring(idx + 1) : string.Empty;
				attributeValue = (context != null && context.TryGetValue(contextKey, out var cv)) ? cv : null;
			}
			else
			{
				attributeValue = await ResolveAttributeAsync(attribute, departmentId);
			}

			return MatchOperator(op, attributeValue, compareValue);
		}

		private async Task<string> ResolveAttributeAsync(FeatureFlagAttributeTypes attribute, int departmentId)
		{
			switch (attribute)
			{
				case FeatureFlagAttributeTypes.DepartmentId:
					return departmentId.ToString(CultureInfo.InvariantCulture);
				case FeatureFlagAttributeTypes.PlanType:
				{
					var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId, false);
					return plan?.PlanId.ToString(CultureInfo.InvariantCulture);
				}
				case FeatureFlagAttributeTypes.DepartmentType:
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
					return department?.DepartmentType;
				}
				case FeatureFlagAttributeTypes.Country:
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
					return department?.Address?.Country;
				}
				case FeatureFlagAttributeTypes.CreatedDate:
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
					return department?.CreatedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
				}
				case FeatureFlagAttributeTypes.PersonnelCount:
				{
					var counts = await _subscriptionsService.GetPlanCountsForDepartmentAsync(departmentId);
					return counts?.UsersCount.ToString(CultureInfo.InvariantCulture);
				}
				default:
					return null;
			}
		}

		private static bool MatchOperator(FeatureFlagOperatorTypes op, string attributeValue, string compareValue)
		{
			if (attributeValue == null)
				return op == FeatureFlagOperatorTypes.NotEquals || op == FeatureFlagOperatorTypes.NotIn;

			switch (op)
			{
				case FeatureFlagOperatorTypes.Equals:
					return string.Equals(attributeValue, compareValue, StringComparison.OrdinalIgnoreCase);
				case FeatureFlagOperatorTypes.NotEquals:
					return !string.Equals(attributeValue, compareValue, StringComparison.OrdinalIgnoreCase);
				case FeatureFlagOperatorTypes.In:
					return SplitList(compareValue).Contains(attributeValue, StringComparer.OrdinalIgnoreCase);
				case FeatureFlagOperatorTypes.NotIn:
					return !SplitList(compareValue).Contains(attributeValue, StringComparer.OrdinalIgnoreCase);
				case FeatureFlagOperatorTypes.Contains:
					return attributeValue.IndexOf(compareValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
				case FeatureFlagOperatorTypes.GreaterThan:
				case FeatureFlagOperatorTypes.GreaterThanOrEqual:
				case FeatureFlagOperatorTypes.LessThan:
				case FeatureFlagOperatorTypes.LessThanOrEqual:
				{
					if (double.TryParse(attributeValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
						&& double.TryParse(compareValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
					{
						if (op == FeatureFlagOperatorTypes.GreaterThan) return a > b;
						if (op == FeatureFlagOperatorTypes.GreaterThanOrEqual) return a >= b;
						if (op == FeatureFlagOperatorTypes.LessThan) return a < b;
						return a <= b;
					}
					return false;
				}
				default:
					return false;
			}
		}

		private static IEnumerable<string> SplitList(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return Enumerable.Empty<string>();

			return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim());
		}

		private FeatureFlagEvaluation Build(FeatureFlag flag, bool enabled, string explicitValue, FeatureFlagEvaluationSource source, int? matchedRuleId = null)
		{
			var value = explicitValue;
			if (value == null)
				value = enabled ? (flag.DefaultValue ?? "true") : (flag.OffValue ?? "false");

			return new FeatureFlagEvaluation
			{
				FeatureFlagId = flag.FeatureFlagId,
				Key = flag.FlagKey,
				IsEnabled = enabled,
				Value = value,
				ValueType = (FeatureFlagValueTypes)flag.FlagType,
				Source = source,
				MatchedRuleId = matchedRuleId
			};
		}

		private FeatureFlagEvaluation BuildDefault(string key, bool defaultValue, FeatureFlagEvaluationSource source)
		{
			var enabled = defaultValue;
			if (FeatureFlagsConfig.CodeDefaults != null && FeatureFlagsConfig.CodeDefaults.TryGetValue(key, out var codeDefault))
			{
				enabled = codeDefault;
				if (source == FeatureFlagEvaluationSource.NotFound)
					source = FeatureFlagEvaluationSource.CodeDefault;
			}

			return new FeatureFlagEvaluation
			{
				FeatureFlagId = 0,
				Key = key,
				IsEnabled = enabled,
				Value = enabled ? "true" : "false",
				ValueType = FeatureFlagValueTypes.Boolean,
				Source = source
			};
		}

		private static int StableBucket(string input)
		{
			// FNV-1a 32-bit -> 0..99. Deterministic across processes (unlike String.GetHashCode).
			unchecked
			{
				uint hash = 2166136261;
				foreach (var c in input)
				{
					hash ^= c;
					hash *= 16777619;
				}
				return (int)(hash % 100u);
			}
		}

		private static string ComputeStateHash(IEnumerable<FeatureFlagEvaluation> evaluations)
		{
			var builder = new StringBuilder();
			foreach (var evaluation in evaluations.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
			{
				builder.Append(evaluation.Key).Append('=').Append(evaluation.IsEnabled ? '1' : '0').Append(':').Append(evaluation.Value).Append(';');
			}

			using (var sha = SHA256.Create())
			{
				var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
				var hex = new StringBuilder(bytes.Length * 2);
				foreach (var b in bytes)
					hex.Append(b.ToString("x2"));
				return hex.ToString();
			}
		}

		#endregion

		#region Cached reads

		public async Task<List<FeatureFlag>> GetAllFlagsAsync(bool includeArchived = false, bool bypassCache = false)
		{
			async Task<List<FeatureFlag>> fetch()
			{
				var all = await _featureFlagRepository.GetAllAsync();
				return all?.ToList() ?? new List<FeatureFlag>();
			}

			List<FeatureFlag> flags;
			if (!bypassCache && SystemBehaviorConfig.CacheEnabled)
				flags = await _cacheProvider.RetrieveAsync(AllFlagsCacheKey, fetch, CacheLength);
			else
				flags = await fetch();

			flags = flags ?? new List<FeatureFlag>();
			return includeArchived ? flags : flags.Where(f => !f.IsArchived).ToList();
		}

		public async Task<FeatureFlag> GetFlagByKeyAsync(string key, bool bypassCache = false)
		{
			var flags = await GetAllFlagsAsync(includeArchived: true, bypassCache: bypassCache);
			return flags.FirstOrDefault(f => string.Equals(f.FlagKey, key, StringComparison.OrdinalIgnoreCase));
		}

		private async Task<List<FeatureFlagTargetingRule>> GetAllTargetingRulesAsync()
		{
			async Task<List<FeatureFlagTargetingRule>> fetch()
			{
				var all = await _featureFlagTargetingRuleRepository.GetAllAsync();
				return all?.ToList() ?? new List<FeatureFlagTargetingRule>();
			}

			if (SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(AllRulesCacheKey, fetch, CacheLength) ?? new List<FeatureFlagTargetingRule>();

			return await fetch();
		}

		private async Task<List<FeatureFlagPrerequisite>> GetAllPrerequisitesAsync()
		{
			async Task<List<FeatureFlagPrerequisite>> fetch()
			{
				var all = await _featureFlagPrerequisiteRepository.GetAllAsync();
				return all?.ToList() ?? new List<FeatureFlagPrerequisite>();
			}

			if (SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(AllPrereqsCacheKey, fetch, CacheLength) ?? new List<FeatureFlagPrerequisite>();

			return await fetch();
		}

		public async Task<List<FeatureFlagOverride>> GetOverridesForDepartmentAsync(int departmentId, bool bypassCache = false)
		{
			async Task<List<FeatureFlagOverride>> fetch()
			{
				var all = await _featureFlagOverrideRepository.GetAllByDepartmentIdAsync(departmentId);
				return all?.ToList() ?? new List<FeatureFlagOverride>();
			}

			if (!bypassCache && SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(string.Format(DepartmentOverridesCacheKey, departmentId), fetch, CacheLength) ?? new List<FeatureFlagOverride>();

			return await fetch();
		}

		#endregion

		#region Flag management

		public async Task<FeatureFlag> SaveFlagAsync(FeatureFlag flag, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (flag == null)
				throw new ArgumentNullException(nameof(flag));
			if (string.IsNullOrWhiteSpace(flag.FlagKey))
				throw new ArgumentException("FlagKey is required.", nameof(flag));

			var existingByKey = await GetFlagByKeyAsync(flag.FlagKey, bypassCache: true);
			if (existingByKey != null && existingByKey.FeatureFlagId != flag.FeatureFlagId)
				throw new InvalidOperationException($"A feature flag with key '{flag.FlagKey}' already exists.");

			string before = null;
			if (flag.FeatureFlagId == 0)
			{
				flag.CreatedOn = DateTime.UtcNow;
				flag.CreatedByUserId = userId;
			}
			else
			{
				before = existingByKey?.CloneJsonToString();
				flag.UpdatedOn = DateTime.UtcNow;
				flag.UpdatedByUserId = userId;
				if (flag.CreatedOn == default(DateTime) && existingByKey != null)
					flag.CreatedOn = existingByKey.CreatedOn;
			}

			var saved = await _featureFlagRepository.SaveOrUpdateAsync(flag, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, before, saved);

			return saved;
		}

		public async Task<bool> ArchiveFlagAsync(string key, string userId, bool archived = true, CancellationToken cancellationToken = default(CancellationToken))
		{
			var flag = await GetFlagByKeyAsync(key, bypassCache: true);
			if (flag == null)
				return false;

			var before = flag.CloneJsonToString();
			flag.IsArchived = archived;
			flag.UpdatedOn = DateTime.UtcNow;
			flag.UpdatedByUserId = userId;

			await _featureFlagRepository.SaveOrUpdateAsync(flag, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, before, flag);

			return true;
		}

		public async Task<FeatureFlag> SetGlobalEnabledAsync(string key, bool enabled, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var flag = await GetFlagByKeyAsync(key, bypassCache: true);
			if (flag == null)
				return null;

			var before = flag.CloneJsonToString();
			flag.IsEnabledGlobally = enabled;
			flag.UpdatedOn = DateTime.UtcNow;
			flag.UpdatedByUserId = userId;

			var saved = await _featureFlagRepository.SaveOrUpdateAsync(flag, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, before, saved);

			return saved;
		}

		public async Task<FeatureFlag> SetRolloutPercentageAsync(string key, int percentage, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var flag = await GetFlagByKeyAsync(key, bypassCache: true);
			if (flag == null)
				return null;

			var before = flag.CloneJsonToString();
			flag.RolloutPercentage = Math.Max(0, Math.Min(100, percentage));
			flag.UpdatedOn = DateTime.UtcNow;
			flag.UpdatedByUserId = userId;

			var saved = await _featureFlagRepository.SaveOrUpdateAsync(flag, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, before, saved);

			return saved;
		}

		public async Task<bool> DeleteFlagAsync(string key, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var flag = await GetFlagByKeyAsync(key, bypassCache: true);
			if (flag == null)
				return false;

			var before = flag.CloneJsonToString();

			var rules = (await _featureFlagTargetingRuleRepository.GetAllAsync())?.Where(r => r.FeatureFlagId == flag.FeatureFlagId) ?? Enumerable.Empty<FeatureFlagTargetingRule>();
			foreach (var rule in rules.ToList())
				await _featureFlagTargetingRuleRepository.DeleteAsync(rule, cancellationToken);

			var prereqs = (await _featureFlagPrerequisiteRepository.GetAllAsync())?.Where(p => p.FeatureFlagId == flag.FeatureFlagId || p.RequiredFeatureFlagId == flag.FeatureFlagId) ?? Enumerable.Empty<FeatureFlagPrerequisite>();
			foreach (var prereq in prereqs.ToList())
				await _featureFlagPrerequisiteRepository.DeleteAsync(prereq, cancellationToken);

			var overrides = (await _featureFlagOverrideRepository.GetAllAsync())?.Where(o => o.FeatureFlagId == flag.FeatureFlagId) ?? Enumerable.Empty<FeatureFlagOverride>();
			foreach (var ovr in overrides.ToList())
				await _featureFlagOverrideRepository.DeleteAsync(ovr, cancellationToken);

			var usages = (await _featureFlagUsageRepository.GetAllAsync())?.Where(u => u.FeatureFlagId == flag.FeatureFlagId) ?? Enumerable.Empty<FeatureFlagUsage>();
			foreach (var usage in usages.ToList())
				await _featureFlagUsageRepository.DeleteAsync(usage, cancellationToken);

			await _featureFlagRepository.DeleteAsync(flag, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, before, null);

			return true;
		}

		#endregion

		#region Override management

		public async Task<List<FeatureFlagOverride>> GetOverridesForFlagAsync(string key)
		{
			var flag = await GetFlagByKeyAsync(key);
			if (flag == null)
				return new List<FeatureFlagOverride>();

			var all = await _featureFlagOverrideRepository.GetAllAsync();
			return all?.Where(o => o.FeatureFlagId == flag.FeatureFlagId).ToList() ?? new List<FeatureFlagOverride>();
		}

		public async Task<FeatureFlagOverride> SetDepartmentOverrideAsync(string key, int departmentId, bool isEnabled, string value, string reason, DateTime? expiresOn, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var flag = await GetFlagByKeyAsync(key, bypassCache: true);
			if (flag == null)
				throw new InvalidOperationException($"No feature flag exists with key '{key}'.");

			var existing = (await GetOverridesForDepartmentAsync(departmentId, bypassCache: true)).FirstOrDefault(o => o.FeatureFlagId == flag.FeatureFlagId);
			string before = null;

			var ovr = existing ?? new FeatureFlagOverride
			{
				FeatureFlagId = flag.FeatureFlagId,
				DepartmentId = departmentId,
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = userId
			};

			if (existing != null)
			{
				before = existing?.CloneJsonToString();
				ovr.UpdatedOn = DateTime.UtcNow;
				ovr.UpdatedByUserId = userId;
			}

			ovr.IsEnabled = isEnabled;
			ovr.FlagValue = value;
			ovr.Reason = reason;
			ovr.ExpiresOn = expiresOn;

			var saved = await _featureFlagOverrideRepository.SaveOrUpdateAsync(ovr, cancellationToken);
			await InvalidateDepartmentOverrideCacheAsync(departmentId);
			PublishAudit(departmentId, userId, AuditLogTypes.FeatureFlagOverrideChanged, before, saved);

			return saved;
		}

		public async Task<bool> RemoveDepartmentOverrideAsync(string key, int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var flag = await GetFlagByKeyAsync(key, bypassCache: true);
			if (flag == null)
				return false;

			var existing = (await GetOverridesForDepartmentAsync(departmentId, bypassCache: true)).FirstOrDefault(o => o.FeatureFlagId == flag.FeatureFlagId);
			if (existing == null)
				return false;

			var before = existing.CloneJsonToString();
			await _featureFlagOverrideRepository.DeleteAsync(existing, cancellationToken);
			await InvalidateDepartmentOverrideCacheAsync(departmentId);
			PublishAudit(departmentId, userId, AuditLogTypes.FeatureFlagOverrideChanged, before, null);

			return true;
		}

		#endregion

		#region Targeting rules & prerequisites

		public async Task<List<FeatureFlagTargetingRule>> GetTargetingRulesForFlagAsync(string key)
		{
			var flag = await GetFlagByKeyAsync(key);
			if (flag == null)
				return new List<FeatureFlagTargetingRule>();

			return (await GetAllTargetingRulesAsync()).Where(r => r.FeatureFlagId == flag.FeatureFlagId).OrderBy(r => r.Priority).ToList();
		}

		public async Task<FeatureFlagTargetingRule> SaveTargetingRuleAsync(FeatureFlagTargetingRule rule, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (rule == null)
				throw new ArgumentNullException(nameof(rule));

			string before = null;
			if (rule.FeatureFlagTargetingRuleId == 0)
			{
				rule.CreatedOn = DateTime.UtcNow;
				rule.CreatedByUserId = userId;
			}
			else
			{
				var existing = (await _featureFlagTargetingRuleRepository.GetAllAsync())?.FirstOrDefault(r => r.FeatureFlagTargetingRuleId == rule.FeatureFlagTargetingRuleId);
				before = existing?.CloneJsonToString();
				rule.UpdatedOn = DateTime.UtcNow;
				rule.UpdatedByUserId = userId;
			}

			var saved = await _featureFlagTargetingRuleRepository.SaveOrUpdateAsync(rule, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, before, saved);

			return saved;
		}

		public async Task<bool> RemoveTargetingRuleAsync(int targetingRuleId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var rule = (await _featureFlagTargetingRuleRepository.GetAllAsync())?.FirstOrDefault(r => r.FeatureFlagTargetingRuleId == targetingRuleId);
			if (rule == null)
				return false;

			var before = rule.CloneJsonToString();
			await _featureFlagTargetingRuleRepository.DeleteAsync(rule, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, before, null);

			return true;
		}

		public async Task<List<FeatureFlagPrerequisite>> GetPrerequisitesForFlagAsync(string key)
		{
			var flag = await GetFlagByKeyAsync(key);
			if (flag == null)
				return new List<FeatureFlagPrerequisite>();

			return (await GetAllPrerequisitesAsync()).Where(p => p.FeatureFlagId == flag.FeatureFlagId).ToList();
		}

		public async Task<FeatureFlagPrerequisite> AddPrerequisiteAsync(string key, string requiredKey, string requiredValue, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var flag = await GetFlagByKeyAsync(key, bypassCache: true);
			var required = await GetFlagByKeyAsync(requiredKey, bypassCache: true);
			if (flag == null || required == null)
				throw new InvalidOperationException("Both the flag and the required flag must exist.");
			if (flag.FeatureFlagId == required.FeatureFlagId)
				throw new InvalidOperationException("A flag cannot be its own prerequisite.");

			var allPrereqs = await GetAllPrerequisitesAsync();
			if (WouldCreateCycle(allPrereqs, flag.FeatureFlagId, required.FeatureFlagId))
				throw new InvalidOperationException("Adding this prerequisite would create a dependency cycle.");

			var prereq = new FeatureFlagPrerequisite
			{
				FeatureFlagId = flag.FeatureFlagId,
				RequiredFeatureFlagId = required.FeatureFlagId,
				RequiredValue = requiredValue
			};

			var saved = await _featureFlagPrerequisiteRepository.SaveOrUpdateAsync(prereq, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, null, saved);

			return saved;
		}

		public async Task<bool> RemovePrerequisiteAsync(int prerequisiteId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var prereq = (await _featureFlagPrerequisiteRepository.GetAllAsync())?.FirstOrDefault(p => p.FeatureFlagPrerequisiteId == prerequisiteId);
			if (prereq == null)
				return false;

			var before = prereq.CloneJsonToString();
			await _featureFlagPrerequisiteRepository.DeleteAsync(prereq, cancellationToken);
			await InvalidateFlagCacheAsync();
			PublishAudit(0, userId, AuditLogTypes.FeatureFlagChanged, before, null);

			return true;
		}

		// Adding edge (dependent -> required) creates a cycle if 'required' can already reach 'dependent'.
		private static bool WouldCreateCycle(List<FeatureFlagPrerequisite> existing, int dependentId, int requiredId)
		{
			var adjacency = existing.GroupBy(p => p.FeatureFlagId).ToDictionary(g => g.Key, g => g.Select(p => p.RequiredFeatureFlagId).ToList());
			var stack = new Stack<int>();
			var seen = new HashSet<int>();
			stack.Push(requiredId);

			while (stack.Count > 0)
			{
				var current = stack.Pop();
				if (current == dependentId)
					return true;
				if (!seen.Add(current))
					continue;
				if (adjacency.TryGetValue(current, out var next))
				{
					foreach (var n in next)
						stack.Push(n);
				}
			}

			return false;
		}

		#endregion

		#region Analytics & lifecycle

		private void RecordEvaluation(int featureFlagId, int departmentId, bool? isEnabled = null)
		{
			if (!FeatureFlagsConfig.TrackEvaluations || featureFlagId == 0)
				return;

			var key = featureFlagId + "|" + departmentId + "|" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
			var counter = _usageBuffer.GetOrAdd(key, _ => new long[3]);
			Interlocked.Increment(ref counter[0]);

			// counter[1] = EnabledCount, counter[2] = DisabledCount (left untouched when the
			// resolved state is unknown, e.g. an evaluation that threw before producing a result).
			if (isEnabled.HasValue)
				Interlocked.Increment(ref counter[isEnabled.Value ? 1 : 2]);
		}

		public async Task<int> FlushEvaluationsAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			var flushed = 0;
			var touchedFlagIds = new HashSet<int>();

			foreach (var key in _usageBuffer.Keys.ToList())
			{
				if (!_usageBuffer.TryRemove(key, out var counter))
					continue;

				var parts = key.Split('|');
				if (parts.Length != 3
					|| !int.TryParse(parts[0], out var flagId)
					|| !int.TryParse(parts[1], out var departmentId)
					|| !DateTime.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
					continue;

				var usage = new FeatureFlagUsage
				{
					FeatureFlagId = flagId,
					DepartmentId = departmentId,
					UsageDate = date,
					EvaluationCount = counter[0],
					EnabledCount = counter[1],
					DisabledCount = counter[2]
				};

				await _featureFlagUsageRepository.SaveOrUpdateAsync(usage, cancellationToken);
				touchedFlagIds.Add(flagId);
				flushed++;
			}

			// Refresh LastEvaluatedOn for the flags that saw traffic (no cache invalidation - stale reads bypass cache).
			var now = DateTime.UtcNow;
			foreach (var flagId in touchedFlagIds)
			{
				var flag = await _featureFlagRepository.GetByIdAsync(flagId);
				if (flag == null)
					continue;
				flag.LastEvaluatedOn = now;
				await _featureFlagRepository.SaveOrUpdateAsync(flag, cancellationToken);
			}

			return flushed;
		}

		public async Task<List<FeatureFlagUsage>> GetUsageForFlagAsync(string key, DateTime from, DateTime to)
		{
			var flag = await GetFlagByKeyAsync(key);
			if (flag == null)
				return new List<FeatureFlagUsage>();

			var all = await _featureFlagUsageRepository.GetAllAsync();
			return all?.Where(u => u.FeatureFlagId == flag.FeatureFlagId && u.UsageDate >= from.Date && u.UsageDate <= to.Date)
				.OrderBy(u => u.UsageDate).ToList() ?? new List<FeatureFlagUsage>();
		}

		public async Task<List<FeatureFlag>> GetStaleFlagsAsync(int? olderThanDays = null)
		{
			var threshold = olderThanDays ?? FeatureFlagsConfig.StaleFlagThresholdDays;
			var cutoff = DateTime.UtcNow.AddDays(-Math.Abs(threshold));
			var flags = await GetAllFlagsAsync(includeArchived: false, bypassCache: true);

			return flags.Where(f => !f.IsPermanent && (!f.LastEvaluatedOn.HasValue || f.LastEvaluatedOn.Value < cutoff)).ToList();
		}

		#endregion

		#region Cache invalidation & audit

		public async Task InvalidateFlagCacheAsync()
		{
			await _cacheProvider.RemoveAsync(AllFlagsCacheKey);
			await _cacheProvider.RemoveAsync(AllRulesCacheKey);
			await _cacheProvider.RemoveAsync(AllPrereqsCacheKey);
		}

		public async Task InvalidateDepartmentOverrideCacheAsync(int departmentId)
		{
			await _cacheProvider.RemoveAsync(string.Format(DepartmentOverridesCacheKey, departmentId));
		}

		private void PublishAudit(int departmentId, string userId, AuditLogTypes type, string before, object after)
		{
			try
			{
				_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = departmentId,
					UserId = userId,
					Type = type,
					Before = before,
					After = after == null ? null : after.CloneJsonToString(),
					Successful = true
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Failed to publish feature flag audit event");
			}
		}

		#endregion
	}
}
