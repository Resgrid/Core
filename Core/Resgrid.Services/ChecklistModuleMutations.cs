using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public partial class DepartmentSettingsService
	{
		private readonly IUnitOfWork _moduleUnit;
		private readonly IFeatureFlagMutationObserver _moduleObserver;
		private readonly Lazy<IFeatureToggleService> _moduleFlags;
		private async Task<T> MutateModuleAsync<T>(int departmentId, Func<Task<T>> action, CancellationToken ct)
		{
			if (_moduleUnit == null || _moduleFlags == null || _moduleUnit.Transaction != null)
				throw new InvalidOperationException("Module setting commands require their own transaction.");
			try
			{
				await _moduleUnit.CreateOrGetConnectionAsync(ct);
				await _moduleObserver.BeforeChangeAsync(departmentId, ct);
				// Observe expired/scheduled policy before replacing it, even between worker sweeps.
				await _moduleObserver.AfterChangeAsync(async (key, department) => (await _moduleFlags.Value.EvaluateFreshAsync(key, department)).IsEnabled, ct);
				var result = await action();
				await _moduleObserver.AfterChangeAsync(async (key, department) => (await _moduleFlags.Value.EvaluateFreshAsync(key, department)).IsEnabled, ct);
				_moduleUnit.CommitChanges();
				await InvalidateSettingCacheAsync(departmentId, DepartmentSettingTypes.ModuleSettings);
				return result;
			}
			catch { _moduleUnit.DiscardChanges(); throw; }
		}
	}
}
