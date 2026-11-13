using System;
using Resgrid.Config;

namespace Resgrid.Providers.ProtectedData
{
	/// <summary>
	/// The broker's mTLS client certificate for the OpenBao cert auth method
	/// (<see cref="DataProtectionConfig.OpenBaoClientCertificatePath"/>), re-read from disk as the step-ca sidecar
	/// renews it. Registered once per broker by <see cref="ProtectedDataProviderModule"/> when the OpenBao provider is
	/// selected, so the provider (which presents it on each new handshake) and the readiness check (which reads its
	/// expiry) see the same certificate.
	///
	/// Construction fails when the path is unset or the file cannot be loaded. The broker's startup check resolves the
	/// key wrapping provider, which depends on this, so bad configuration still stops the broker at startup.
	/// </summary>
	public sealed class OpenBaoClientCertificate : ReloadingPkcs12Certificate
	{
		public OpenBaoClientCertificate(TimeProvider timeProvider)
			: base(RequiredPath(), DataProtectionConfig.OpenBaoClientCertificatePassword, ConfiguredPollInterval, timeProvider)
		{
		}

		private static string RequiredPath()
		{
			if (string.IsNullOrWhiteSpace(DataProtectionConfig.OpenBaoClientCertificatePath))
				throw new InvalidOperationException(
					"DataProtectionConfig.OpenBaoClientCertificatePath is not configured. The OpenBao cert auth method requires the broker's mTLS client certificate.");

			return DataProtectionConfig.OpenBaoClientCertificatePath;
		}
	}
}
