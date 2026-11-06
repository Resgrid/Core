using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Hosting;
using Resgrid.Services;

namespace Resgrid.Web.Middleware
{
	/// <summary>
	/// Writes this host's feature flag evaluation counts to FeatureFlagUsages (and LastEvaluatedOn) every
	/// <c>FeatureFlagsConfig.EvaluationFlushIntervalSeconds</c> and once more on shutdown; see <see cref="FeatureToggleUsageFlusher"/>.
	/// </summary>
	public sealed class FeatureToggleUsageFlushService : BackgroundService
	{
		private readonly FeatureToggleUsageFlusher _flusher;

		public FeatureToggleUsageFlushService(ILifetimeScope rootScope)
		{
			_flusher = new FeatureToggleUsageFlusher(rootScope);
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken) => _flusher.RunAsync(stoppingToken);
	}
}
