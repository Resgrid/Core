using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed class ChecklistAccessMutationObserver : IFeatureFlagMutationObserver
	{
		private readonly IChecklistRepository _store;
		private readonly IDepartmentSettingsRepository _settings;
		private readonly TimeProvider _clock;
		private readonly List<int> _departments = new();
		public ChecklistAccessMutationObserver(IChecklistRepository store, IDepartmentSettingsRepository settings, TimeProvider clock = null)
		{ _store = store; _settings = settings; _clock = clock ?? TimeProvider.System; }
		public async Task BeforeChangeAsync(int? departmentId, CancellationToken ct)
		{
			_departments.Clear(); await _store.LockAccessFenceAsync(ct);
			if (departmentId.HasValue) _departments.Add(departmentId.Value);
			else for (var after = 0; ;)
			{
				var page = await _store.SchedulingDepartmentsAsync(after, ct); if (page.Count == 0) break;
				_departments.AddRange(page); after = page[^1];
			}
			// Same order as scheduling commands: access fence, department, then policy reads/writes.
			foreach (var department in _departments) await _store.LockDepartmentAsync(department, ct);
		}
		public async Task AfterChangeAsync(Func<string, int, Task<bool>> evaluate, CancellationToken ct)
		{
			foreach (var department in _departments)
			{
				var row = await _settings.GetDepartmentSettingByIdTypeAsync(department, DepartmentSettingTypes.ModuleSettings);
				var module = row == null ? new DepartmentModuleSettings() : ObjectSerialization.Deserialize<DepartmentModuleSettings>(row.Setting);
				var enabled = module != null && !module.ChecklistsDisabled && await evaluate(FeatureFlagKeys.ChecklistsSystem, department);
				await _store.ApplyAccessStateAsync(department, enabled, _clock.GetUtcNow().UtcDateTime, ct, module?.InventoryDisabled != true);
			}
		}
	}
}
