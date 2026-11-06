using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Resgrid.Services;
using Resgrid.Workers.Framework;

namespace Resgrid.Workers.Console
{
	/// <summary>
	/// Writes this worker process's feature flag evaluation counts to FeatureFlagUsages (and LastEvaluatedOn) every
	/// <c>FeatureFlagsConfig.EvaluationFlushIntervalSeconds</c> and once more on shutdown; see <see cref="FeatureToggleUsageFlusher"/>.
	/// An in-process timer rather than a Quidjibo schedule: the counters are per process, and a scheduled job runs in
	/// whichever worker picks it up.
	/// </summary>
	public sealed class FeatureToggleUsageFlushService(DatabaseUpgradeState database) : BackgroundService
	{
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			try
			{
				// FeatureFlagUsages may not have its unique index until the upgrade has run.
				await database.WaitForCompletionAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}

			// The host's own container is not Autofac; the worker services live in the Bootstrapper kernel.
			await new FeatureToggleUsageFlusher(Bootstrapper.GetKernel()).RunAsync(stoppingToken);
		}
	}
}
