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
	/// <summary>Worker 97 (ApprovalsLogic): approval step announcements, escalations and closed-request sync.</summary>
	public class ApprovalsTask : IQuidjiboHandler<ApprovalsCommand>
	{
		public string Name => "Approvals";
		public int Priority => 1;
		public ILogger _logger;

		public ApprovalsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(ApprovalsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new ApprovalsLogic().Process(cancellationToken);

				if (result.Total > 0)
					_logger.LogInformation($"Approvals::Announced {result.Announced}, escalated {result.Escalated}, synced {result.Synced}");

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
