using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	/// <summary>
	/// ADP migration coordinator sweep: expired-lock liveness, due offboarding flips, and — when a
	/// department's overnight window is open — one night of the enrollment/offboarding state machine.
	/// Frequent on purpose: most sweeps are cheap no-ops (no queued departments, or windows closed),
	/// and the lock-expiry liveness path (plan section 20.4) should not wait long after a dead
	/// worker's safety valve passes.
	/// </summary>
	public sealed class AdpMigrationTask : IQuidjiboHandler<AdpMigrationCommand>
	{
		private readonly ILogger _logger;

		public AdpMigrationTask(ILogger logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public string Name => "ADP Migration";
		public int Priority => 1;

		public async Task ProcessAsync(AdpMigrationCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress?.Report(1, $"Starting the {Name} Task");

			try
			{
				var logic = new AdpMigrationLogic();
				var result = await logic.Process(cancellationToken);

				if (!result.Item1)
					throw new InvalidOperationException(result.Item2);

				_logger.LogInformation("AdpMigration::{Summary}", result.Item2);
				progress?.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
		}
	}
}
