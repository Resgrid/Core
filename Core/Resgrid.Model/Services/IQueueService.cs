using System.Collections.Generic;
using Resgrid.Model.Queue;

namespace Resgrid.Model.Services
{
	public interface IQueueService
	{
		void EnqueueMessageBroadcast(MessageQueueItem mqi);
		void EnqueueCallBroadcast(CallQueueItem cqi);
		List<QueueItem> Dequeue(QueueTypes type);
		void Requeue(QueueItem item);
		void RequeueAll(IEnumerable<QueueItem> items);
		void SetQueueItemCompleted(int queueItemId);
		QueueItem GetQueueItemById(int queueItemId);
		void EnqueueDistributionListBroadcast(DistributionListQueueItem dlqi);
	}
}