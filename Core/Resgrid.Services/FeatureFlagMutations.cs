using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public partial class FeatureToggleService
	{
		private readonly IUnitOfWork _mutationUnit;
		private readonly IFeatureFlagMutationObserver _mutationObserver;
		private bool _mutationActive;
		private bool _invalidateFlags;
		private readonly HashSet<int> _invalidateOverrides = new();
		private readonly List<Action> _committedAudits = new();
		private async Task InvalidateCacheAfterCommitAsync(string key)
		{
			try { await _cacheProvider.RemoveAsync(key); }
			catch (Exception ex) { Resgrid.Framework.Logging.LogError($"Feature flag cache invalidation failed after commit for {key}: {ex.GetType().FullName}."); }
		}
		private async Task<T> MutateFlagAsync<T>(Func<Task<T>> action, CancellationToken ct)
		{
			if (_mutationObserver == null || _mutationUnit == null) return await action();
			if (_mutationActive || _mutationUnit.Transaction != null) throw new InvalidOperationException("Feature flag commands own their transaction.");
			T result;
			try
			{
				try
				{
					_mutationActive = true; await _mutationUnit.CreateOrGetConnectionAsync(ct);
					await _mutationObserver.BeforeChangeAsync(null, ct);
					// Observe expired/scheduled policy before replacing it, even between worker sweeps.
					await _mutationObserver.AfterChangeAsync(async (key, department) => (await EvaluateFreshAsync(key, department)).IsEnabled, ct);
					result = await action();
					await _mutationObserver.AfterChangeAsync(async (key, department) => (await EvaluateFreshAsync(key, department)).IsEnabled, ct);
					_mutationUnit.CommitChanges();
				}
				catch { _mutationUnit.DiscardChanges(); throw; }
				_mutationActive = false;
				// Cache failures cannot roll back committed writes or suppress their audit publication.
				if (_invalidateFlags)
					foreach (var key in new[] { AllFlagsCacheKey, AllRulesCacheKey, AllPrereqsCacheKey })
						await InvalidateCacheAfterCommitAsync(key);
				foreach (var department in _invalidateOverrides)
					await InvalidateCacheAfterCommitAsync(string.Format(DepartmentOverridesCacheKey, department));
				// PublishAudit already isolates individual publication failures.
				foreach (var audit in _committedAudits) audit();
				return result;
			}
			finally { _mutationActive = false; _invalidateFlags = false; _invalidateOverrides.Clear(); _committedAudits.Clear(); }
		}
	}
}
