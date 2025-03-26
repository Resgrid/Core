using Resgrid.Config;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus.Rabbit;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus
{
	public class OutboundQueueProvider : IOutboundQueueProvider
	{
		private readonly RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;

		public OutboundQueueProvider()
		{
			_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
		}

		public async Task<bool> EnqueueCall(CallQueueItem callQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return await _rabbitOutboundQueueProvider.EnqueueCall(callQueue);

			return false;
		}

		public async Task<bool> EnqueueMessage(MessageQueueItem messageQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return await _rabbitOutboundQueueProvider.EnqueueMessage(messageQueue);

			return false;
		}

		public async Task<bool> EnqueueDistributionList(DistributionListQueueItem distributionListQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return await _rabbitOutboundQueueProvider.EnqueueDistributionList(distributionListQueue);

			return false;
		}

		public async Task<bool> EnqueueNotification(NotificationItem notificationQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return await _rabbitOutboundQueueProvider.EnqueueNotification(notificationQueue);

			return false;
		}

		public async Task<bool> EnqueueShiftNotification(ShiftQueueItem shiftQueueItem)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return await _rabbitOutboundQueueProvider.EnqueueShiftNotification(shiftQueueItem);

			return false;
		}

		public async Task<bool> EnqueueAuditEvent(AuditEvent auditEvent)
		{
			return await _rabbitOutboundQueueProvider.EnqueueAuditEvent(auditEvent);
		}

		public async Task<bool> EnqueueSecurityRefreshEvent(SecurityRefreshEvent securityRefreshEvent)
		{
			return await _rabbitOutboundQueueProvider.EnqueueSecurityRefreshEvent(securityRefreshEvent);
		}
	}
}
