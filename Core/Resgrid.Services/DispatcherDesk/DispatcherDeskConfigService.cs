using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// <see cref="DepartmentSettingTypes.DispatcherDeskConfig"/> (setting 128). Read on every routing decision, so it is cached
	/// (its own key, invalidated here after each save; DepartmentSettingsService's cache switch does not know this key).
	/// </summary>
	public class DispatcherDeskConfigService : IDispatcherDeskConfigService
	{
		private const string CacheKey = "DSetDispatcherDeskConfig_{0}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromDays(1);

		private readonly IDepartmentSettingsRepository _departmentSettingsRepository;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICacheProvider _cacheProvider;

		public DispatcherDeskConfigService(IDepartmentSettingsRepository departmentSettingsRepository, IDepartmentSettingsService departmentSettingsService,
			ICacheProvider cacheProvider)
		{
			_departmentSettingsRepository = departmentSettingsRepository;
			_departmentSettingsService = departmentSettingsService;
			_cacheProvider = cacheProvider;
		}

		public async Task<DispatcherDeskConfig> GetConfigAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var setting = await _departmentSettingsRepository.GetDepartmentSettingByIdTypeAsync(departmentId, DepartmentSettingTypes.DispatcherDeskConfig);
				return setting?.Setting ?? string.Empty;
			}

			string value;
			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
					value = await _cacheProvider.RetrieveAsync<string>(string.Format(CacheKey, departmentId), getSetting, CacheLength);
				else
					value = await getSetting();
			}
			catch (Exception ex)
			{
				// Routing must never fail because the cache did: read the row directly.
				Logging.LogException(ex, $"Dispatcher desk config read for department {departmentId}");
				value = await getSetting();
			}

			if (!string.IsNullOrWhiteSpace(value))
			{
				try
				{
					var config = ObjectSerialization.Deserialize<DispatcherDeskConfig>(value);
					if (config != null)
						return config.Normalize();
				}
				catch (Exception)
				{
					// A corrupt blob means the desk is off, never a failed dispatch.
				}
			}

			return new DispatcherDeskConfig();
		}

		public async Task<DispatcherDeskConfig> SaveConfigAsync(int departmentId, DispatcherDeskConfig config, string userId,
			CancellationToken cancellationToken = default)
		{
			var normalized = (config ?? new DispatcherDeskConfig()).Normalize();

			await _departmentSettingsService.SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(normalized),
				DepartmentSettingTypes.DispatcherDeskConfig, cancellationToken);

			// After the write commits, never before (a concurrent reader would re-cache the old value).
			try
			{
				await _cacheProvider.RemoveAsync(string.Format(CacheKey, departmentId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk config cache invalidation for department {departmentId}");
			}

			return normalized;
		}
	}
}
