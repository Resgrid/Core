using Resgrid.Model.Queue;

namespace Resgrid.Model.Providers
{
	public interface IRabbitOutboundQueueProvider
	{
		void EnqueueCall(CallQueueItem callQueue);
		void EnqueueMessage(MessageQueueItem messageQueue);
		void EnqueueDistributionList(DistributionListQueueItem distributionListQueue);
		void EnqueueNotification(NotificationItem notificationQueue);
		void EnqueueShiftNotification(ShiftQueueItem shiftQueueItem);
		void EnqueueCqrsEvent(CqrsEvent cqrsEvent);
	}
}
