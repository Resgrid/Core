using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Web.Broker.Services
{
	/// <summary>
	/// Readiness check behind /health/ready. The broker is ready only when it can do its job: the KMS answers and
	/// still accepts the broker's certificate, and the database (policy, keys, audit chain, replay store) is reachable.
	/// /health stays a liveness check that runs nothing.
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

		private readonly IKeyWrappingProvider _keyWrappingProvider;
		private readonly IHealthService _healthService;

		public BrokerReadinessHealthCheck(IKeyWrappingProvider keyWrappingProvider, IHealthService healthService)
		{
			_keyWrappingProvider = keyWrappingProvider;
			_healthService = healthService;
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
