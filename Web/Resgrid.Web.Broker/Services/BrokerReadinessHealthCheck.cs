using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.ProtectedData;

namespace Resgrid.Web.Broker.Services
{
	/// <summary>
	/// Readiness check behind /health/ready. The broker is ready only when it can do its job: its OpenBao client
	/// certificate is not about to lapse, the KMS answers and still accepts that certificate, and the database (policy,
	/// keys, audit chain, replay store) is reachable. /health stays a liveness check that runs nothing.
	///
	/// The certificate check exists because the KMS check alone can pass on an expired certificate: the OpenBao token
	/// from the last cert login stays valid for its lease. The step-ca sidecar renews the day-long leaf at about two
	/// thirds of its life, so a certificate inside its last <see cref="ClientCertificateRenewalWindow"/> means renewal
	/// has been failing for hours, and the broker is about to lose the KMS.
	///
	/// The endpoint is unauthenticated for kubelet probes. Its body is only the status word; reason codes go to the
	/// log, once per change of state, so an outage logs once rather than on every probe.
	/// </summary>
	public sealed class BrokerReadinessHealthCheck : IHealthCheck
	{
		public const string ReadyTag = "ready";

		// Under the kubelet's probe timeout (set timeoutSeconds >= 5): a slow KMS reads as not ready, not as a hung probe.
		private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

		// 1 = last reported healthy, 0 = last reported unhealthy, -1 = nothing reported yet.
		private static int _lastReported = -1;

		/// <summary>Not ready once the OpenBao client certificate has this little life left (or none).</summary>
		public static readonly TimeSpan ClientCertificateRenewalWindow = TimeSpan.FromHours(2);

		private readonly IKeyWrappingProvider _keyWrappingProvider;
		private readonly IHealthService _healthService;
		private readonly TimeProvider _timeProvider;
		private readonly OpenBaoClientCertificate _clientCertificate;

		/// <param name="clientCertificate">
		/// Registered only when the OpenBao provider is selected; null under LocalDev, which has no client certificate
		/// to watch.
		/// </param>
		public BrokerReadinessHealthCheck(IKeyWrappingProvider keyWrappingProvider, IHealthService healthService,
			TimeProvider timeProvider, OpenBaoClientCertificate clientCertificate = null)
		{
			_keyWrappingProvider = keyWrappingProvider;
			_healthService = healthService;
			_timeProvider = timeProvider;
			_clientCertificate = clientCertificate;
		}

		public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
		{
			var reason = await ProbeAsync(cancellationToken);
			Report(reason);

			return reason == null ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(reason);
		}

		/// <summary>Null when ready; otherwise a value-free reason code.</summary>
		private async Task<string> ProbeAsync(CancellationToken cancellationToken)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(ProbeTimeout);

			try
			{
				var certificate = CheckClientCertificate();
				if (certificate != null)
					return certificate;

				var kms = await _keyWrappingProvider.CheckKmsHealthAsync(timeout.Token);
				if (kms == null || !kms.IsHealthy)
					return kms?.Reason ?? "kms_unhealthy";

				// GetDatabaseTimestamp takes no token and returns null on failure; WaitAsync bounds it.
				var timestamp = await _healthService.GetDatabaseTimestamp().WaitAsync(timeout.Token);
				return string.IsNullOrWhiteSpace(timestamp) ? "database_unavailable" : null;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return "probe_timeout";
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protected Data Broker readiness probe failed");
				return "probe_failed";
			}
		}

		/// <summary>
		/// Null when there is no client certificate to watch or it has more than <see cref="ClientCertificateRenewalWindow"/>
		/// left. Reads the certificate the next handshake would present, which also picks up a renewal on disk.
		/// </summary>
		private string CheckClientCertificate()
		{
			if (_clientCertificate == null)
				return null;

			var remaining = _clientCertificate.Current.NotAfter.ToUniversalTime() - _timeProvider.GetUtcNow().UtcDateTime;
			if (remaining <= TimeSpan.Zero)
				return "kms_client_certificate_expired";

			return remaining <= ClientCertificateRenewalWindow ? "kms_client_certificate_expiring" : null;
		}

		private static void Report(string reason)
		{
			var healthy = reason == null ? 1 : 0;
			if (Interlocked.Exchange(ref _lastReported, healthy) == healthy)
				return;

			if (healthy == 1)
				Logging.LogInfo("Protected Data Broker is ready: KMS and database reachable.");
			else
				Logging.LogError($"Protected Data Broker is NOT ready ({reason}); protected operations fail closed until it recovers.");
		}
	}
}
