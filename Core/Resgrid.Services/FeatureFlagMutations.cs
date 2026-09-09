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
		private async Task<T> MutateFlagAsync<T>(Func<Task<T>> action, CancellationToken ct)
		{
			if (_mutationObserver == null || _mutationUnit == null) return await action();
			if (_mutationActive || _mutationUnit.Transaction != null) throw new InvalidOperationException("Feature flag commands own their transaction.");
			try
			{
				_mutationActive = true; await _mutationUnit.CreateOrGetConnectionAsync(ct);
				await _mutationObserver.BeforeChangeAsync(null, ct);
				// Observe expired/scheduled policy before replacing it, even between worker sweeps.
				await _mutationObserver.AfterChangeAsync(async (key, department) => (await EvaluateFreshAsync(key, department)).IsEnabled, ct);
				var result = await action();
				await _mutationObserver.AfterChangeAsync(async (key, department) => (await EvaluateFreshAsync(key, department)).IsEnabled, ct);
				_mutationUnit.CommitChanges(); _mutationActive = false;
				if (_invalidateFlags) await InvalidateFlagCacheAsync();
				foreach (var department in _invalidateOverrides) await InvalidateDepartmentOverrideCacheAsync(department);
				foreach (var audit in _committedAudits) audit();
				return result;
			}
			catch { _mutationUnit.DiscardChanges(); throw; }
			finally { _mutationActive = false; _invalidateFlags = false; _invalidateOverrides.Clear(); _committedAudits.Clear(); }
		}
	}
}
