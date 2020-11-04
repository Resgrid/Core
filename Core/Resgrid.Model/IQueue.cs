using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model
{
	public interface IQueue<T>// where T : QueueItem
	{
		void EnsureExist();
		Task<bool> Clear();
		void AddItem(T item);
		void PopulateQueue();
		T GetItem();
		Task<IEnumerable<T>> GetItems(int maxItemsToReturn);
		bool IsLocked { get; }
	}
}
