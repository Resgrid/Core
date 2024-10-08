using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.Security;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class QueuesProcessorTask : IQuidjiboHandler<QueuesProcessorCommand>
	{
		private bool _running = true;

		//public IFrequency[] Frequencies => new IFrequency[] { new RunAlways() };
		public string Name => "Queues Processor";
		public int Priority => 1;
		public ILogger _logger;
		private CancellationToken _cancellationToken;

		private SecurityLogic _securityLogic;

		public QueuesProcessorTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(QueuesProcessorCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			_cancellationToken = cancellationToken;

			if (progress != null)
				progress.Report(1, $"Starting the {Name} Task");

			RabbitInboundQueueProvider queue = new RabbitInboundQueueProvider();
			queue.CallQueueReceived += OnCallQueueReceived;
			queue.MessageQueueReceived += OnMessageQueueReceived;
			queue.DistributionListQueueReceived += OnDistributionListQueueReceived;
			queue.NotificationQueueReceived += OnNotificationQueueReceived;
			queue.ShiftNotificationQueueReceived += OnShiftNotificationQueueReceived;
			queue.CqrsEventQueueReceived += OnCqrsEventQueueReceived;
			queue.PaymentEventQueueReceived = null;//queue.PaymentEventQueueReceived += OnPaymentEventQueueReceived;
			queue.AuditEventQueueReceived += OnAuditEventQueueReceived;
			queue.UnitLocationEventQueueReceived += OnUnitLocationEventQueueReceived;
			queue.PersonnelLocationEventQueueReceived += OnPersonnelLocationEventQueueReceived;
			queue.SecurityRefreshEventQueueReceived += OnSecurityRefreshEventQueueReceived;

			await queue.Start("QueueProcessor-CQRS");

			while (!_cancellationToken.IsCancellationRequested)
			{
				Thread.Sleep(500);
			}

			if (progress != null)
				progress.Report(100, $"Finishing the {Name} Task");
		}

		private async Task OnCallQueueReceived(CallQueueItem cqi)
		{
			_logger.LogInformation($"{Name}: Call Queue Received with a number of {cqi.Call.Number}, starting processing...");
			await BroadcastCallLogic.ProcessCallQueueItem(cqi);
			_logger.LogInformation($"{Name}: Finished processing of call {cqi.Call.Number}.");
		}

		private async Task OnMessageQueueReceived(MessageQueueItem mqi)
		{
			_logger.LogInformation($"{Name}: Message Queue Received with a message id of {mqi.Message.MessageId}, starting processing...");
			await BroadcastMessageLogic.ProcessMessageQueueItem(mqi);
			_logger.LogInformation($"{Name}: Finished processing of message {mqi.Message.MessageId}.");
		}

		private async Task OnDistributionListQueueReceived(DistributionListQueueItem dlqi)
		{
			_logger.LogInformation($"{Name}: Distribution List Queue Received with a message id of {dlqi.Message.MessageID}, starting processing...");
			await DistributionListLogic.ProcessDistributionListQueueItem(dlqi);
			_logger.LogInformation($"{Name}: Finished processing of distribution message {dlqi.Message.MessageID}.");
		}

		private async Task OnNotificationQueueReceived(NotificationItem ni)
		{
			_logger.LogInformation($"{Name}: Notification Queue Received with a type of {ni.Type} and id of {ni.ItemId}, starting processing...");
			await NotificationBroadcastLogic.ProcessNotificationItem(ni, Guid.NewGuid().ToString(), "");
			_logger.LogInformation($"{Name}: Finished processing of Notification with type of {ni.Type} and id of {ni.ItemId}.");
		}

		private async Task OnShiftNotificationQueueReceived(ShiftQueueItem sqi)
		{
			_logger.LogInformation($"{Name}: Shift Queue for shift id {sqi.ShiftId}, starting processing...");
			await ShiftNotificationLogic.ProcessShiftQueueItem(sqi);
			_logger.LogInformation($"{Name}: Finished processing of shift with id of {sqi.ShiftId}.");
		}

		private async Task OnCqrsEventQueueReceived(CqrsEvent cqrs)
		{
			_logger.LogInformation($"{Name}: System Queue Received with a type of {cqrs.Type}, starting processing...");
			await SystemQueueLogic.ProcessSystemQueueItem(cqrs, _cancellationToken);
			_logger.LogInformation($"{Name}: Finished processing of system cqrs queue item with type of {cqrs.Type}.");
		}

		//private async Task OnPaymentEventQueueReceived(CqrsEvent cqrs)
		//{
		//	_logger.LogInformation($"{Name}: Payment Queue Received with a type of {cqrs.Type}, starting processing...");
		//	await PaymentQueueLogic.ProcessPaymentQueueItem(cqrs);
		//	_logger.LogInformation($"{Name}: Finished processing of Payment queue item with type of {cqrs.Type}.");
		//}

		private async Task OnAuditEventQueueReceived(AuditEvent auditEvent)
		{
			_logger.LogInformation($"{Name}: Audit Queue Received with an id of {auditEvent.EventId}, starting processing...");
			await AuditQueueLogic.ProcessAuditQueueItem(auditEvent, _cancellationToken);
			_logger.LogInformation($"{Name}: Finished processing of Audit queue item with an id of {auditEvent.EventId}.");
		}

		private async Task OnUnitLocationEventQueueReceived(UnitLocationEvent unitLocationEvent)
		{
			_logger.LogInformation($"{Name}: Unit Location Queue Received with an id of {unitLocationEvent.EventId}, starting processing...");
			await UnitLocationQueueLogic.ProcessUnitLocationQueueItem(unitLocationEvent, _cancellationToken);
			_logger.LogInformation($"{Name}: Finished processing of Unit Location queue item with an id of {unitLocationEvent.EventId}.");
		}

		private async Task OnPersonnelLocationEventQueueReceived(PersonnelLocationEvent personnelLocationEvent)
		{
			_logger.LogInformation($"{Name}: Personnel Location Queue Received with an id of {personnelLocationEvent.EventId}, starting processing...");
			await PersonnelLocationQueueLogic.ProcessPersonnelLocationQueueItem(personnelLocationEvent);
			_logger.LogInformation($"{Name}: Finished processing of Personnel Location queue item with an id of {personnelLocationEvent.EventId}.");
		}

		private async Task OnSecurityRefreshEventQueueReceived(SecurityRefreshEvent securityRefreshEvent)
		{
			_logger.LogInformation($"{Name}: Security Refresh Queue Received with an id of {securityRefreshEvent.EventId}, starting processing...");

			if (_securityLogic == null)
				_securityLogic = new SecurityLogic();

			SecurityQueueItem item = new SecurityQueueItem();
			item.DepartmentId = securityRefreshEvent.DepartmentId;
			item.Type = securityRefreshEvent.Type;

			await _securityLogic.Process(item);
			_logger.LogInformation($"{Name}: Finished processing of Security Refresh queue item with an id of {securityRefreshEvent.EventId}.");
		}
	}
}
