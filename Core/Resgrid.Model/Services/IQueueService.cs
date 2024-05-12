using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Queue;

namespace Resgrid.Model.Services
{
	public interface IQueueService
	{
		/// <summary>
		/// Gets the queue item by identifier asynchronous.
		/// </summary>
		/// <param name="queueItemId">The queue item identifier.</param>
		/// <returns>Task&lt;QueueItem&gt;.</returns>
		Task<QueueItem> GetQueueItemByIdAsync(int queueItemId);

		/// <summary>
		/// Dequeues the asynchronous.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;List&lt;QueueItem&gt;&gt;.</returns>
		Task<List<QueueItem>> DequeueAsync(QueueTypes type, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Requeues the asynchronous.
		/// </summary>
		/// <param name="item">The item.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;QueueItem&gt;.</returns>
		Task<QueueItem> RequeueAsync(QueueItem item, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Requeues all asynchronous.
		/// </summary>
		/// <param name="items">The items.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> RequeueAllAsync(IEnumerable<QueueItem> items, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Enqueues the message broadcast asynchronous.
		/// </summary>
		/// <param name="mqi">The mqi.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> EnqueueMessageBroadcastAsync(MessageQueueItem mqi, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Enqueues the call broadcast asynchronous.
		/// </summary>
		/// <param name="cqi">The cqi.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> EnqueueCallBroadcastAsync(CallQueueItem cqi, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Enqueues the distribution list broadcast asynchronous.
		/// </summary>
		/// <param name="dlqi">The dlqi.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> EnqueueDistributionListBroadcastAsync(DistributionListQueueItem dlqi, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the queue item completed asynchronous.
		/// </summary>
		/// <param name="queueItemId">The queue item identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;QueueItem&gt;.</returns>
		Task<QueueItem> SetQueueItemCompletedAsync(int queueItemId, CancellationToken cancellationToken = default(CancellationToken));

		Task<QueueItem> GetPendingDeleteDepartmentQueueItemAsync(int departmentId);

		Task<QueueItem> EnqueuePendingDeleteDepartmentAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<QueueItem>> GetAllPendingDeleteDepartmentQueueItemsAsync();

		Task<QueueItem> UpdateQueueItem(QueueItem item, CancellationToken cancellationToken = default(CancellationToken));

		Task<QueueItem> CancelPendingDepartmentDeletionRequest(int departmentId, string name, CancellationToken cancellationToken = default(CancellationToken));
	}
}
