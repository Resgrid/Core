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
	/// Worker 81, every minute with a second pass 30 seconds in: Enhanced AI destination advice reviews. A pass that runs long (model calls
	/// wait for an idle inference slot) makes ticks queued behind it skip; claims are leased, so a second worker process never reviews the
	/// same call at once.
	/// </summary>
	public class AiDestinationAdviceTask : IQuidjiboHandler<AiDestinationAdviceCommand>
	{
		private static readonly SemaphoreSlim SingleFlight = new SemaphoreSlim(1, 1);

		public string Name => "AI Destination Advice";
		public int Priority => 1;

		private readonly ILogger _logger;

		public AiDestinationAdviceTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(AiDestinationAdviceCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (!await SingleFlight.WaitAsync(0, cancellationToken))
				return;

			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var started = DateTime.UtcNow;
				await RunPassAsync(cancellationToken);

				// The 60 s debounce plus a one-minute tick would leave advice up to two minutes behind a note; a second pass halves that.
				var wait = TimeSpan.FromSeconds(30) - (DateTime.UtcNow - started);
				if (Resgrid.Config.AiDestinationConfig.Enabled && wait > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
				{
					await Task.Delay(wait, cancellationToken);
					await RunPassAsync(cancellationToken);
				}

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
			finally
			{
				SingleFlight.Release();
			}
		}

		private async Task RunPassAsync(CancellationToken cancellationToken)
		{
			var result = await new AiDestinationAdviceLogic().Process(cancellationToken);
			if (result.Item1)
				_logger.LogInformation($"AiDestinationAdvice::{result.Item2}");
			else
				_logger.LogError($"AiDestinationAdvice::Failed to process destination reviews. {result.Item2}");
		}
	}
}
