using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Drains this process's feature flag evaluation counters into FeatureFlagUsages every
	/// <c>FeatureFlagsConfig.EvaluationFlushIntervalSeconds</c>, and once more when the host stops. The counters live in a
	/// per-process static buffer, so every host that evaluates flags (web, API, workers) runs one of these from a small
	/// hosted service; a queued job would only ever flush whichever process picked it up.
	/// </summary>
	public sealed class FeatureToggleUsageFlusher
	{
		/// <summary>How long the final flush may take once the host is stopping.</summary>
		public static readonly TimeSpan ShutdownFlushBudget = TimeSpan.FromSeconds(10);

		private readonly ILifetimeScope _rootScope;
		private readonly TimeSpan _interval;

		public FeatureToggleUsageFlusher(ILifetimeScope rootScope)
			: this(rootScope, ConfiguredInterval)
		{
		}

		public FeatureToggleUsageFlusher(ILifetimeScope rootScope, TimeSpan interval)
		{
			_rootScope = rootScope;
			_interval = interval;
		}

		/// <summary>The configured flush interval; a missing or non-positive setting falls back to 60 seconds, never off,
		/// because the buffer would otherwise grow until the process restarts.</summary>
		public static TimeSpan ConfiguredInterval => TimeSpan.FromSeconds(FeatureFlagsConfig.EvaluationFlushIntervalSeconds > 0
			? Math.Max(5, FeatureFlagsConfig.EvaluationFlushIntervalSeconds)
			: 60);

		/// <summary>Flushes on every interval until <paramref name="stoppingToken"/> is cancelled, then flushes once more.</summary>
		public async Task RunAsync(CancellationToken stoppingToken)
		{
			using (var timer = new PeriodicTimer(_interval))
			{
				try
				{
					while (await timer.WaitForNextTickAsync(stoppingToken))
						await FlushAsync(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
				}
			}

			// The stopping token is already cancelled, so the last flush gets a budget of its own.
			using (var budget = new CancellationTokenSource(ShutdownFlushBudget))
				await FlushAsync(budget.Token);
		}

		/// <summary>One flush in a fresh lifetime scope (its own unit of work and connection). Never throws.</summary>
		public async Task<int> FlushAsync(CancellationToken cancellationToken)
		{
			try
			{
				using (var scope = _rootScope.BeginLifetimeScope())
					return await scope.Resolve<IFeatureToggleService>().FlushEvaluationsAsync(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return 0;
			}
			catch (Exception ex)
			{
				// The next tick tries again; FlushEvaluationsAsync keeps counts it could not save.
				Logging.LogException(ex, "FeatureToggle usage flush failed.");
				return 0;
			}
		}
	}
}
