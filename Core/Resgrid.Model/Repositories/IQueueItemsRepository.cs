using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IQueueItemsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.QueueItem}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.QueueItem}" />
	public interface IQueueItemsRepository: IRepository<QueueItem>
	{
		/// <summary>
		/// Gets the pending queue items by type identifier asynchronous.
		/// </summary>
		/// <param name="typeId">The type identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;QueueItem&gt;&gt;.</returns>
		Task<IEnumerable<QueueItem>> GetPendingQueueItemsByTypeIdAsync(int typeId);
	}
}
