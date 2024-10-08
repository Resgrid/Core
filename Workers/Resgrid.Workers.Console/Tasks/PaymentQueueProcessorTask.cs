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
	public class PaymentQueueProcessorTask : IQuidjiboHandler<PaymentQueueProcessorCommand>
	{
		private bool _running = true;
		public string Name => "Payment Queue Processor";
		public int Priority => 1;
		public ILogger _logger;

		public PaymentQueueProcessorTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(PaymentQueueProcessorCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (progress != null)
				progress.Report(1, $"Starting the {Name} Task");

			RabbitInboundQueueProvider queue = new RabbitInboundQueueProvider();
			queue.PaymentEventQueueReceived += OnPaymentEventQueueReceived;

			await queue.Start("QueueProcessor-Payment");

			while (!cancellationToken.IsCancellationRequested)
			{
				Thread.Sleep(500);
			}

			if (progress != null)
				progress.Report(100, $"Finishing the {Name} Task");
		}

		private async Task OnPaymentEventQueueReceived(CqrsEvent cqrs)
		{
			_logger.LogInformation($"{Name}: Payment Queue Received with a type of {cqrs.Type}, starting processing...");
			await PaymentQueueLogic.ProcessPaymentQueueItem(cqrs);
			_logger.LogInformation($"{Name}: Finished processing of Payment queue item with type of {cqrs.Type}.");
		}
	}
}
