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
	/// Worker 82, every minute: one tick of the dispatcher desk (M0324, dispatchers-on-duty-plan.md): expired offers move on,
	/// live work nobody can take overflows, long waits are escalated, pending and upcoming scheduled calls are added to the
	/// desk, break reminders / due / over / overdue alerts and auto return, dispatchers signed off at the end of their duty,
	/// and old closed items purged.
	/// </summary>
	public class DispatcherDeskTask : IQuidjiboHandler<DispatcherDeskCommand>
	{
		// A slow tick (many departments) can outlast the minute; ticks queued behind it have nothing to add. Every desk write is
		// conditional (item Version, break state), so a second worker process still never double-acts.
		private static readonly SemaphoreSlim SingleFlight = new SemaphoreSlim(1, 1);

		public string Name => "Dispatcher Desk";
		public int Priority => 1;

		private readonly ILogger _logger;

		public DispatcherDeskTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(DispatcherDeskCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (!await SingleFlight.WaitAsync(0, cancellationToken))
				return;

			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var changes = await scope.Resolve<IDispatcherDeskWorkerService>().TickAsync(DateTime.UtcNow, cancellationToken);

				if (changes > 0)
					_logger.LogInformation("DispatcherDesk::{Count} desk change(s) this tick", changes);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Resgrid.Framework.Logging.LogException(ex, "Dispatcher desk worker");
			}
			finally
			{
				SingleFlight.Release();
			}
		}
	}
}
