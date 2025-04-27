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
			return await _rabbitOutboundQueueProvider.EnqueueCall(callQueue);
		}

		public async Task<bool> EnqueueMessage(MessageQueueItem messageQueue)
		{
			return await _rabbitOutboundQueueProvider.EnqueueMessage(messageQueue);
		}

		public async Task<bool> EnqueueDistributionList(DistributionListQueueItem distributionListQueue)
		{
			return await _rabbitOutboundQueueProvider.EnqueueDistributionList(distributionListQueue);
		}

		public async Task<bool> EnqueueNotification(NotificationItem notificationQueue)
		{
			return await _rabbitOutboundQueueProvider.EnqueueNotification(notificationQueue);
		}

		public async Task<bool> EnqueueShiftNotification(ShiftQueueItem shiftQueueItem)
		{
			return await _rabbitOutboundQueueProvider.EnqueueShiftNotification(shiftQueueItem);
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
