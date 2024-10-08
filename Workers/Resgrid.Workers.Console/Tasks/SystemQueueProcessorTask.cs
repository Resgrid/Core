﻿using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
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
			if (progress != null)
				progress.Report(1, $"Starting the {Name} Task");

			RabbitInboundQueueProvider queue = new RabbitInboundQueueProvider();
			queue.CqrsEventQueueReceived += OnCqrsEventQueueReceived;

			await queue.Start("QueueProcessor-System");

			while (!cancellationToken.IsCancellationRequested)
			{
				Thread.Sleep(500);
			}

			//await Task.Factory.StartNew(() =>
			//{
			//	// Keep alive
			//	while (_running)
			//	{
			//		Thread.Sleep(1000);
			//	}
			//}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			if (progress != null)
				progress.Report(100, $"Finishing the {Name} Task");
		}

		private async Task OnCqrsEventQueueReceived(CqrsEvent cqrs)
		{
			_logger.LogInformation($"{Name}: System Queue Received with a type of {cqrs.Type}, starting processing...");
			await SystemQueueLogic.ProcessSystemQueueItem(cqrs);
			_logger.LogInformation($"{Name}: Finished processing of system queue item with type of {cqrs.Type}.");
		}
	}
}
