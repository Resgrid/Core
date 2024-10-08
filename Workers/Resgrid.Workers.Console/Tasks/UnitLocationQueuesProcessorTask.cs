using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class UnitLocationQueuesProcessorTask : IQuidjiboHandler<UnitLocationQueueProcessorCommand>
	{
		private bool _running = true;
		public string Name => "Unit Location Queue Processor";
		public int Priority => 1;
		public ILogger _logger;

		public UnitLocationQueuesProcessorTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(UnitLocationQueueProcessorCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (progress != null)
				progress.Report(1, $"Starting the {Name} Task");

			RabbitInboundQueueProvider queue = new RabbitInboundQueueProvider();
			queue.UnitLocationEventQueueReceived += OnUnitLocationEventQueueReceived;

			await queue.Start("QueueProcessor-UnitLocation");

			while (!cancellationToken.IsCancellationRequested)
			{
				Thread.Sleep(500);
			}

			if (progress != null)
				progress.Report(100, $"Finishing the {Name} Task");
		}

		private async Task OnUnitLocationEventQueueReceived(UnitLocationEvent unitLocationEvent)
		{
			_logger.LogInformation($"{Name}: Unit Location Queue Received with an id of {unitLocationEvent.EventId}, starting processing...");
			await UnitLocationQueueLogic.ProcessUnitLocationQueueItem(unitLocationEvent);
			_logger.LogInformation($"{Name}: Finished processing of Unit Location queue item with an id of {unitLocationEvent.EventId}.");
		}
	}
}
