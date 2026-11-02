using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;

namespace Resgrid.Workers.Console.Tasks
{
	/// <summary>
	/// Worker 77, every minute: builds queued offline map packs (base-map cells cut from the PMTiles archive, custom map
	/// rasters, department sprites; maplibre-mapping-routing-navigation-plan.md §19.4), requeues packs whose builder died,
	/// and deletes packs nobody requested for 60 days. Does nothing until MappingConfig.OfflinePackStorage is set.
	/// </summary>
	public class OfflineMapPackTask : IQuidjiboHandler<OfflineMapPackCommand>
	{
		/// <summary>Per tick; the rest wait for the next minute (a base-map cell can take a while to download).</summary>
		public const int MaxPacksPerTick = 10;

		private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
		private static DateTime _lastCleanup = DateTime.MinValue;

		// A long build spans ticks; ticks queued behind it have nothing to add. Claims are conditional writes, so a second
		// worker process still never builds the same pack.
		private static readonly SemaphoreSlim SingleFlight = new SemaphoreSlim(1, 1);

		public string Name => "Offline Map Packs";
		public int Priority => 1;

		private readonly ILogger _logger;

		public OfflineMapPackTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(OfflineMapPackCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (!await SingleFlight.WaitAsync(0, cancellationToken))
				return;

			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var offlineMaps = scope.Resolve<IOfflineMapPackService>();

				if (!offlineMaps.IsInstallEnabled)
					return;

				if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
				{
					_lastCleanup = DateTime.UtcNow;
					var cleaned = await offlineMaps.CleanupAsync(cancellationToken);
					if (cleaned > 0)
						_logger.LogInformation("OfflineMapPacks::Requeued or removed {Count} pack(s)", cleaned);
				}

				var built = await offlineMaps.ProcessQueueAsync(MaxPacksPerTick, cancellationToken);
				if (built > 0)
					_logger.LogInformation("OfflineMapPacks::Processed {Count} pack(s)", built);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Resgrid.Framework.Logging.LogException(ex, "Offline map pack worker");
			}
			finally
			{
				SingleFlight.Release();
			}
		}
	}
}
