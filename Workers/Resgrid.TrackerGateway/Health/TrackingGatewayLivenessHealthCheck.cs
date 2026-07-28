using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Resgrid.TrackerGateway.Health
{
	public sealed class TrackingGatewayLivenessHealthCheck : IHealthCheck
	{
		public Task<HealthCheckResult> CheckHealthAsync(
			HealthCheckContext context,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(
				HealthCheckResult.Healthy("Tracker gateway process is running."));
		}
	}
}
