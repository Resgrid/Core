using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UnitTrackingStatusService : IUnitTrackingStatusService
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public UnitTrackingStatusService(IDepartmentSettingsService departmentSettingsService)
		{
			_departmentSettingsService = departmentSettingsService;
		}

		public async Task<UnitTrackingDeviceStatus> GetEffectiveStatusAsync(
			UnitTrackingDevice device,
			DateTime? utcNow = null,
			CancellationToken cancellationToken = default)
		{
			if (device == null)
				throw new ArgumentNullException(nameof(device));

			cancellationToken.ThrowIfCancellationRequested();
			if (!device.IsEnabled || device.IsDeleted)
				return UnitTrackingDeviceStatus.Disabled;
			if (device.LastStatus == (int)UnitTrackingDeviceStatus.Error)
				return UnitTrackingDeviceStatus.Error;

			var lastConnectivity =
				device.LastReceivedOn.HasValue && device.LastSeenOn.HasValue
					? (device.LastReceivedOn.Value > device.LastSeenOn.Value
						? device.LastReceivedOn
						: device.LastSeenOn)
					: device.LastReceivedOn ?? device.LastSeenOn;
			if (!lastConnectivity.HasValue)
				return UnitTrackingDeviceStatus.NeverSeen;

			var staleAfterSeconds =
				await _departmentSettingsService.GetHardwareTrackingStaleAfterSecondsAsync(
					device.DepartmentId);
			var now = utcNow ?? DateTime.UtcNow;
			return lastConnectivity.Value < now.AddSeconds(-Math.Max(1, staleAfterSeconds))
				? UnitTrackingDeviceStatus.Stale
				: UnitTrackingDeviceStatus.Online;
		}
	}
}
