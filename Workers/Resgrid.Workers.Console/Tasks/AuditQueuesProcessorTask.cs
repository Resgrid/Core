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
	public class AuditQueuesProcessorTask : IQuidjiboHandler<AuditQueueProcessorCommand>
	{
		private bool _running = true;
		public string Name => "Audit Queue Processor";
		public int Priority => 1;
		public ILogger _logger;

		public AuditQueuesProcessorTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(AuditQueueProcessorCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (progress != null)
				progress.Report(1, $"Starting the {Name} Task");

			RabbitInboundQueueProvider queue = new RabbitInboundQueueProvider();
			queue.AuditEventQueueReceived += OnAuditEventQueueReceived;

			await queue.Start("QueueProcessor-Audit");

			while (!cancellationToken.IsCancellationRequested)
			{
				Thread.Sleep(500);
			}

			if (progress != null)
				progress.Report(100, $"Finishing the {Name} Task");
		}

		private async Task OnAuditEventQueueReceived(AuditEvent auditEvent)
		{
			_logger.LogInformation($"{Name}: Audit Queue Received with an id of {auditEvent.EventId}, starting processing...");
			await AuditQueueLogic.ProcessAuditQueueItem(auditEvent);
			_logger.LogInformation($"{Name}: Finished processing of Audit queue item with an id of {auditEvent.EventId}.");
		}
	}
}
