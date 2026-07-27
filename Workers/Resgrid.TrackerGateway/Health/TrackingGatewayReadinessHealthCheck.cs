using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Resgrid.TrackerGateway.Health
{
	public sealed class TrackingGatewayReadinessHealthCheck : IHealthCheck
	{
		private readonly TrackingGatewayReadinessState _readiness;

		public TrackingGatewayReadinessHealthCheck(
			TrackingGatewayReadinessState readiness)
		{
			_readiness = readiness;
		}

		public Task<HealthCheckResult> CheckHealthAsync(
			HealthCheckContext context,
			CancellationToken cancellationToken = default)
		{
			var snapshot = _readiness.GetSnapshot();
			var data = new Dictionary<string, object>
			{
				["expectedListeners"] = snapshot.ExpectedListeners,
				["boundListeners"] = snapshot.BoundListeners
			};

			if (snapshot.IsReady)
			{
				return Task.FromResult(
					HealthCheckResult.Healthy(
						"All required tracking listeners are bound.",
						data));
			}

			return Task.FromResult(
				HealthCheckResult.Unhealthy(
					"One or more required tracking listeners are not ready.",
					data: data));
		}
	}
}
