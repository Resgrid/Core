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
	/// (its own keys; DepartmentSettingsService's cache switch does not know them).
	/// </summary>
	public class DispatcherDeskConfigService : IDispatcherDeskConfigService
	{
		// The settings are cached under the department's current version and every save gives the department a new one (the
		// road closure precedent). A read that began before a save and stores what it read after the save stores it under a
		// version nobody reads again; with one fixed key that late store brought the old settings back for the whole TTL.
		private const string CacheKey = "DSetDispatcherDeskConfig2_{0}_{1}";
		private const string VersionKey = "DSetDispatcherDeskConfigVersion1_{0}";

		// Short on purpose: when a save cannot move the version (Redis down at that moment, back later with the old keys), the
		// old settings are served at most this long after the cache returns. The version key slides while it is in use.
		private static readonly TimeSpan CacheLength = TimeSpan.FromMinutes(15);
		private static readonly TimeSpan VersionLength = TimeSpan.FromDays(30);

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

			string value = null;
			var read = false;
			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
				{
					// The version is read before the row is (see CacheKey). Null means the cache is unavailable.
					var version = await _cacheProvider.GetOrAddStringAsync(string.Format(VersionKey, departmentId), NewVersion(), VersionLength);
					if (!string.IsNullOrWhiteSpace(version))
					{
						var key = string.Format(CacheKey, departmentId, version);
						var cached = await _cacheProvider.GetStringAsync(key);
						if (cached != null)
						{
							value = cached;
							read = true;
						}
						else
						{
							value = await getSetting();
							read = true;
							await _cacheProvider.SetStringAsync(key, value, CacheLength);
						}
					}
				}
			}
			catch (Exception ex)
			{
				// Routing must never fail because the cache did: read the row directly.
				Logging.LogException(ex, $"Dispatcher desk config read for department {departmentId}");
			}

			if (!read)
				value = await getSetting();

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

			// After the write commits: a new version moves every reader to a fresh key (see CacheKey); the old entry ages out.
			// The save itself has succeeded either way, so a failed move is logged, not reported as a failed save; the short
			// CacheLength bounds how long the old settings can still be served.
			if (Config.SystemBehaviorConfig.CacheEnabled)
			{
				var moved = false;
				try
				{
					moved = await _cacheProvider.SetStringAsync(string.Format(VersionKey, departmentId), NewVersion(), VersionLength);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher desk config cache invalidation for department {departmentId}");
				}

				if (!moved)
					Logging.LogWarning($"Dispatcher desk config for department {departmentId} was saved, but its cache version could not be moved; " +
									   $"readers may see the previous settings for up to {CacheLength.TotalMinutes:0} minutes.");
			}

			return normalized;
		}

		private static string NewVersion() => Guid.NewGuid().ToString("N");
	}
}
