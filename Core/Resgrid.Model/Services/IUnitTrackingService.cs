using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IUnitTrackingService
	{
		Task<UnitTrackingDevice> GetDeviceByIdAsync(string deviceId, int departmentId);
		Task<List<UnitTrackingDevice>> GetDevicesForDepartmentAsync(int departmentId);
		Task<List<UnitTrackingDevice>> GetDevicesForUnitAsync(int departmentId, int unitId);
		Task<List<UnitTrackingCredential>> GetCredentialsForDeviceAsync(string deviceId, int departmentId);
		Task<UnitTrackingDevice> CreateDeviceAsync(
			UnitTrackingDevice device,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingDevice> UpdateDeviceAsync(
			UnitTrackingDevice device,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingDevice> DisableDeviceAsync(
			string deviceId,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingDevice> DeleteDeviceAsync(
			string deviceId,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingDevice> RebindDeviceAsync(
			string deviceId,
			int departmentId,
			int newUnitId,
			string userId,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingCredentialProvisionResult> CreateCredentialAsync(
			string deviceId,
			int departmentId,
			UnitTrackingAuthMode authMode,
			string userId,
			string headerName = null,
			string basicUsername = null,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingCredentialProvisionResult> RotateCredentialAsync(
			string deviceId,
			string credentialId,
			int departmentId,
			string userId,
			TimeSpan? overlap = null,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingCredential> RevokeCredentialAsync(
			string deviceId,
			string credentialId,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default);
	}
}
