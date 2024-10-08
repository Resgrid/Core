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
	public class PersonnelLocationQueuesProcessorTask : IQuidjiboHandler<PersonnelLocationQueueProcessorCommand>
	{
		private bool _running = true;
		public string Name => "Personnel Location Queue Processor";
		public int Priority => 1;
		public ILogger _logger;

		public PersonnelLocationQueuesProcessorTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(PersonnelLocationQueueProcessorCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (progress != null)
				progress.Report(1, $"Starting the {Name} Task");

			RabbitInboundQueueProvider queue = new RabbitInboundQueueProvider();
			queue.PersonnelLocationEventQueueReceived += OnPersonnelLocationEventQueueReceived;

			await queue.Start("QueueProcessor-PersonnelLocation");

			while (!cancellationToken.IsCancellationRequested)
			{
				Thread.Sleep(500);
			}

			if (progress != null)
				progress.Report(100, $"Finishing the {Name} Task");
		}

		private async Task OnPersonnelLocationEventQueueReceived(PersonnelLocationEvent personnelLocationEvent)
		{
			_logger.LogInformation($"{Name}: Personnel Location Queue Received with an id of {personnelLocationEvent.EventId}, starting processing...");
			await PersonnelLocationQueueLogic.ProcessPersonnelLocationQueueItem(personnelLocationEvent);
			_logger.LogInformation($"{Name}: Finished processing of Personnel Location queue item with an id of {personnelLocationEvent.EventId}.");
		}
	}
}
