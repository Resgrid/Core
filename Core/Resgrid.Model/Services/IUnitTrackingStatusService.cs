using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IUnitTrackingStatusService
	{
		Task<UnitTrackingDeviceStatus> GetEffectiveStatusAsync(
			UnitTrackingDevice device,
			DateTime? utcNow = null,
			CancellationToken cancellationToken = default);
	}
}
