using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resgrid.Config;

namespace Resgrid.Web.Services.Health
{
	/// <summary>
	/// Full-health probe: proves the TTS microservice is reachable by calling its shallow
	/// /health endpoint. Deliberately does NOT call the TTS /health/full endpoint — that
	/// spawns real synthesis work and belongs to the TTS service's own monitoring. Not
	/// part of the shallow /health liveness endpoint.
	/// </summary>
	public sealed class TtsServiceHealthCheck : IHealthCheck
	{
		private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

		private readonly IHttpClientFactory _httpClientFactory;

		public TtsServiceHealthCheck(IHttpClientFactory httpClientFactory)
		{
			_httpClientFactory = httpClientFactory;
		}

		public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(TtsConfig.ServiceBaseUrl))
				return HealthCheckResult.Degraded("TTS service base URL is not configured; dynamic voice prompts fall back to native Twilio speech.");

			using var timeout = new CancellationTokenSource(ProbeTimeout);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

			try
			{
				var client = _httpClientFactory.CreateClient("ByPassSSLHttpClient");
				var healthUrl = $"{TtsConfig.ServiceBaseUrl.TrimEnd('/')}/health";

				using var response = await client.GetAsync(healthUrl, linked.Token);

				if (response.IsSuccessStatusCode)
					return HealthCheckResult.Healthy("TTS service is reachable and reports healthy.");

				return HealthCheckResult.Unhealthy($"TTS service /health returned HTTP {(int)response.StatusCode}.");
			}
			catch (OperationCanceledException) when (timeout.IsCancellationRequested)
			{
				return HealthCheckResult.Unhealthy($"TTS service probe timed out after {ProbeTimeout.TotalSeconds:0} seconds.");
			}
			catch (Exception ex)
			{
				return HealthCheckResult.Unhealthy("TTS service probe failed.", ex);
			}
		}
	}
}
