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
	/// <summary>Worker 90 (SignaturesLogic): webhook hints, envelope reconcile, request expiry, pending seals, re-timestamps.</summary>
	public class SignaturesTask : IQuidjiboHandler<SignaturesCommand>
	{
		public string Name => "Signatures";
		public int Priority => 1;
		public ILogger _logger;

		public SignaturesTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(SignaturesCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new SignaturesLogic().Process(cancellationToken);

				if (result.Total > 0)
					_logger.LogInformation($"Signatures::Hints {result.Hints}, reconciled {result.Reconciled}, expired {result.Expired}, sealed {result.Sealed}, re-timestamped {result.Retimestamped}, purged {result.Purged}, reminded {result.Reminded}");

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
