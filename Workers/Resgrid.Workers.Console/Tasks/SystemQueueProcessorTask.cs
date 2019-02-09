using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class SystemQueueProcessorTask : IQuidjiboHandler<SystemQueueProcessorCommand>
	{
		private bool _running = true;
		public string Name => "System Queue Processor";
		public int Priority => 1;
		public ILogger _logger;

		public SystemQueueProcessorTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(SystemQueueProcessorCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			RabbitInboundQueueProvider queue = new RabbitInboundQueueProvider();
			queue.CqrsEventQueueReceived += OnCqrsEventQueueReceived;

			queue.Start();

			await Task.Factory.StartNew(() =>
			{
				// Keep alive
				while (_running)
				{
					Thread.Sleep(1000);
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			progress.Report(100, $"Finishing the {Name} Task");
		}

		private void OnCqrsEventQueueReceived(CqrsEvent cqrs)
		{
			_logger.LogInformation($"{Name}: System Queue Received with a type of {cqrs.Type}, starting processing...");
			SystemQueueLogic.ProcessSystemQueueItem(cqrs);
			_logger.LogInformation($"{Name}: Finished processing of system queue item with type of {cqrs.Type}.");
		}
	}
}
