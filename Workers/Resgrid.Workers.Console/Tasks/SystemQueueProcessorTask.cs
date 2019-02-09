using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;
using Serilog.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class SystemQueueProcessorTask : IQuidjiboHandler<SystemQueueProcessorCommand>
	{
		private IQuidjiboProgress _progress;
		private bool _running = true;
		public string Name => "System Queue Processor";
		public int Priority => 1;

		public async Task ProcessAsync(SystemQueueProcessorCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			_progress = progress;

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

			progress.Report(2, $"Finishing the {Name} Task");
		}

		private void OnCqrsEventQueueReceived(CqrsEvent cqrs)
		{
			_progress.Report(2, $"{Name}: System Queue Received with a type of {cqrs.Type}, starting processing...");
			SystemQueueLogic.ProcessSystemQueueItem(cqrs);
			_progress.Report(2, $"{Name}: Finished processing of system queue item with type of {cqrs.Type}.");
		}
	}
}
