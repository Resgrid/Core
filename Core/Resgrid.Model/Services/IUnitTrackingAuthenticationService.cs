using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IUnitTrackingAuthenticationService
	{
		UnitTrackingGeneratedCredential GenerateCredential();
		string ComputeSecretHash(string token);
		bool VerifySecret(string token, string storedHash);
		Task<UnitTrackingAuthenticationResult> AuthenticateAsync(
			string token,
			DateTime? utcNow = null,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingDevice> GetEnabledDeviceByEndpointIdAsync(
			string deviceId,
			CancellationToken cancellationToken = default);
		Task<UnitTrackingDevice> GetEnabledDeviceByProtocolIdentifierAsync(
			string protocolKey,
			string deviceIdentifier,
			CancellationToken cancellationToken = default);
		Task<IReadOnlyCollection<UnitTrackingCredential>> GetActiveCredentialsForDeviceAsync(
			string deviceId,
			DateTime? utcNow = null,
			CancellationToken cancellationToken = default);
		Task InvalidateCredentialAsync(string secretHash);
		Task InvalidateDeviceAsync(UnitTrackingDevice device);
	}
}
