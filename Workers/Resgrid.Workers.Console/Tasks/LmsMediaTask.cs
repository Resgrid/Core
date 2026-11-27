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
	/// <summary>Worker 87 (LmsMediaLogic): transcodes queued LMS video uploads to HLS.</summary>
	public class LmsMediaTask : IQuidjiboHandler<LmsMediaCommand>
	{
		public string Name => "LMS Media";
		public int Priority => 1;
		public ILogger _logger;

		public LmsMediaTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(LmsMediaCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new LmsMediaLogic().Process(cancellationToken);
				if (!result.Item1)
					_logger.LogError("LmsMedia::" + result.Item2);
				else if (!string.IsNullOrEmpty(result.Item2))
					_logger.LogInformation("LmsMedia::" + result.Item2);

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
