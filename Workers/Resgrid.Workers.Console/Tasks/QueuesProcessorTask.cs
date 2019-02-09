using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;
using Serilog.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class QueuesProcessorTask : IQuidjiboHandler<QueuesProcessorCommand>
	{
		private IQuidjiboProgress _progress;
		private bool _running = true;

		//public IFrequency[] Frequencies => new IFrequency[] { new RunAlways() };
		public string Name => "Queues Processor";
		public int Priority => 1;

		public async Task ProcessAsync(QueuesProcessorCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			RabbitInboundQueueProvider queue = new RabbitInboundQueueProvider();
			queue.CallQueueReceived += OnCallQueueReceived;
			queue.MessageQueueReceived += OnMessageQueueReceived;
			queue.DistributionListQueueReceived += OnDistributionListQueueReceived;
			queue.NotificationQueueReceived += OnNotificationQueueReceived;
			queue.ShiftNotificationQueueReceived += OnShiftNotificationQueueReceived;

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

		private void OnCallQueueReceived(CallQueueItem cqi)
		{
			_progress.Report(3, $"{Name}: Call Queue Received with a number of {cqi.Call.Number}, starting processing...");
			BroadcastCallLogic.ProcessCallQueueItem(cqi);
			_progress.Report(4, $"{Name}: Finished processing of call {cqi.Call.Number}.");
		}

		private void OnMessageQueueReceived(MessageQueueItem mqi)
		{
			_progress.Report(4, $"{Name}: Message Queue Received with a message id of {mqi.Message.MessageId}, starting processing...");
			BroadcastMessageLogic.ProcessMessageQueueItem(mqi);
			_progress.Report(5, $"{Name}: Finished processing of message {mqi.Message.MessageId}.");
		}

		private void OnDistributionListQueueReceived(DistributionListQueueItem dlqi)
		{
			_progress.Report(6, $"{Name}: Distribution List Queue Received with a message id of {dlqi.Message.MessageID}, starting processing...");
			DistributionListLogic.ProcessDistributionListQueueItem(dlqi);
			_progress.Report(7, $"{Name}: Finished processing of distribution message {dlqi.Message.MessageID}.");
		}

		private void OnNotificationQueueReceived(NotificationItem ni)
		{
			_progress.Report(8, $"{Name}: Notification Queue Received with a type of {ni.Type} and id of {ni.ItemId}, starting processing...");
			NotificationBroadcastLogic.ProcessNotificationItem(ni, Guid.NewGuid().ToString(), "");
			_progress.Report(9, $"{Name}: Finished processing of Notification with type of {ni.Type} and id of {ni.ItemId}.");
		}

		private void OnShiftNotificationQueueReceived(ShiftQueueItem sqi)
		{
			_progress.Report(10, $"{Name}: Shift Queue for shift id {sqi.ShiftId}, starting processing...");
			ShiftNotificationLogic.ProcessShiftQueueItem(sqi);
			_progress.Report(11, $"{Name}: Finished processing of shift with id of {sqi.ShiftId}.");
		}
	}
}
