using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Services.Health
{
	/// <summary>
	/// Full-health probe: proves Redis is reachable through the same cache provider the
	/// application uses, via a write/read round-trip. Not part of the shallow /health
	/// liveness endpoint.
	/// </summary>
	public sealed class RedisHealthCheck : IHealthCheck
	{
		private const string ProbeKey = "health-probe";

		private readonly ICacheProvider _cacheProvider;

		public RedisHealthCheck(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
		{
			try
			{
				// The provider swallows connection failures and returns false/null, so a
				// failed round-trip surfaces here as a value mismatch rather than a throw.
				var stored = await _cacheProvider.SetStringAsync(ProbeKey, "ok", TimeSpan.FromMinutes(1));

				if (!stored)
					return HealthCheckResult.Unhealthy("Redis write failed; the cache provider reports it is unavailable.");

				var value = await _cacheProvider.GetStringAsync(ProbeKey);

				if (value != "ok")
					return HealthCheckResult.Unhealthy("Redis round-trip returned an unexpected value.");

				return HealthCheckResult.Healthy("Redis round-trip succeeded.");
			}
			catch (Exception ex)
			{
				return HealthCheckResult.Unhealthy("Redis probe failed.", ex);
			}
		}
	}
}
