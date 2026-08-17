using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class CommunicationTestTask : IQuidjiboHandler<CommunicationTestCommand>
	{
		public string Name => "Communication Test";
		public int Priority => 1;
		public ILogger _logger;

		public CommunicationTestTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(CommunicationTestCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var communicationTestService = Bootstrapper.GetKernel().Resolve<ICommunicationTestService>();

				_logger.LogInformation("CommunicationTest::Processing scheduled tests");
				await communicationTestService.ProcessScheduledTestsAsync(cancellationToken);

				// Picks up runs whose delivery was interrupted (process restart mid-send) so the
				// remaining recipients still get their message instead of the run stalling.
				_logger.LogInformation("CommunicationTest::Delivering pending runs");
				await communicationTestService.DeliverPendingRunsAsync(cancellationToken);

				_logger.LogInformation("CommunicationTest::Completing expired runs");
				await communicationTestService.CompleteExpiredRunsAsync(cancellationToken);

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
