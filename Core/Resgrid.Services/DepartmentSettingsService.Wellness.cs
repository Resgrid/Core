using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Services
{
	public partial class DepartmentSettingsService
	{
		/// <summary>
		/// Wellness settings (setting 141, §13b WL0). Read uncached on purpose: the wellness officer role decides who reads members'
		/// health entries, so a change must hold on the next request, not after a cache window.
		/// </summary>
		public async Task<WellnessSettings> GetWellnessSettingsAsync(int departmentId)
		{
			var setting = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.WellnessSettings);
			if (!String.IsNullOrWhiteSpace(setting?.Setting))
			{
				try
				{
					var settings = ObjectSerialization.Deserialize<WellnessSettings>(setting.Setting);
					if (settings != null)
						return settings.Normalized();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Unreadable wellness settings for department {departmentId}");
				}
			}

			return new WellnessSettings();
		}

		public async Task<DepartmentSetting> SetWellnessSettingsAsync(int departmentId, WellnessSettings settings, CancellationToken cancellationToken = default(CancellationToken))
		{
			var stored = (settings ?? new WellnessSettings()).Normalized();
			return await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(stored), DepartmentSettingTypes.WellnessSettings, cancellationToken);
		}
	}
}
