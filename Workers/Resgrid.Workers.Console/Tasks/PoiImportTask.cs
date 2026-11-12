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
	/// Worker 79, every minute: runs queued POI imports (Mapping > POIs > Import), geocoding the rows that carry only an
	/// address and saving the POIs, resumes imports whose worker stopped, and deletes finished imports after 90 days.
	/// </summary>
	public class PoiImportTask : IQuidjiboHandler<PoiImportCommand>
	{
		/// <summary>Per tick; an import can take minutes, and the rest start on a later tick.</summary>
		public const int MaxImportsPerTick = 20;

		private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);
		private static DateTime _lastCleanup = DateTime.MinValue;

		// A long import spans ticks; ticks queued behind it have nothing to add. Claims are conditional writes, so a second
		// worker process still never runs the same import.
		private static readonly SemaphoreSlim SingleFlight = new SemaphoreSlim(1, 1);

		public string Name => "POI Imports";
		public int Priority => 1;

		private readonly ILogger _logger;

		public PoiImportTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(PoiImportCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (!await SingleFlight.WaitAsync(0, cancellationToken))
				return;

			try
			{
				if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
				{
					_lastCleanup = DateTime.UtcNow;
					using var cleanupScope = Bootstrapper.GetKernel().BeginLifetimeScope();
					var removed = await cleanupScope.Resolve<IPoiImportService>().CleanupAsync(cancellationToken);
					if (removed > 0)
						_logger.LogInformation("PoiImports::Removed {Count} finished import(s)", removed);
				}

				// One scope (and unit of work) per import, so one import's failure leaves nothing behind for the next.
				for (var i = 0; i < MaxImportsPerTick && !cancellationToken.IsCancellationRequested; i++)
				{
					using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
					if (!await scope.Resolve<IPoiImportService>().ProcessNextAsync(cancellationToken))
						break;
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Resgrid.Framework.Logging.LogException(ex, "POI import worker");
			}
			finally
			{
				SingleFlight.Release();
			}
		}
	}
}
