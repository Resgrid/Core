using Resgrid.Model.Queue;

namespace Resgrid.Model.Providers
{
	public interface IOutboundQueueProvider
	{
		void EnqueueCall(CallQueueItem callQueue);
		void EnqueueMessage(MessageQueueItem callQueue);
		void EnqueueNotification(NotificationItem notificationQueue);
		void EnqueueShiftNotification(ShiftQueueItem shiftQueueItem);
		void EnqueueDistributionList(DistributionListQueueItem distributionListQueue);
	}
}