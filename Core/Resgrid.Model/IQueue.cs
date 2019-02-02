using System.Collections.Generic;

namespace Resgrid.Model
{
	public interface IQueue<T>// where T : QueueItem
	{
		void EnsureExist();
		void Clear();
		void AddItem(T item);
		void PopulateQueue();
		T GetItem();
		IEnumerable<T> GetItems(int maxItemsToReturn);
		bool IsLocked { get; }
	}
}