using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Backend.Scout
{
	public class ScoutQueue : IQueue<ScoutQueueItem>
	{
		private static bool _cleared;
		private static object _lock;
		private static bool _isLocked;
		private static Queue<ScoutQueueItem> _queue;
		private readonly IJobsService _jobsService;

		public ScoutQueue(IJobsService jobsService)
		{
			_jobsService = jobsService;
			_queue = new Queue<ScoutQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;
				Clear();

				var item = new ScoutQueueItem();
				item.DepartmentId = 1;
				item.Username = "TestUser";
				item.DepartmentCode = "XXXX";

				_queue.Enqueue(item);

				_isLocked = false;
				_cleared = false;
			}
		}

		public void EnsureExist()
		{
			if (_queue == null)
				_queue = new Queue<ScoutQueueItem>();
		}

		public async Task<bool> Clear()
		{
			_cleared = true;

			_queue.Clear();

			return _cleared;
		}

		public bool IsLocked
		{
			get { return _isLocked; }
		}

		public void AddItem(ScoutQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public ScoutQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<ScoutQueueItem>> GetItems(int maxItemsToReturn)
		{
			var items = new List<ScoutQueueItem>();
			await _jobsService.SetJobAsCheckedAsync(JobTypes.Scout);

			if (_queue.Count <= 0)
				PopulateQueue();

			while (_isLocked)
			{
				Thread.Sleep(250);
			}

			int count = 0;
			if (_queue.Count < maxItemsToReturn)
				count = _queue.Count;
			else
				count = maxItemsToReturn;

			for (int i = 0; i < count; i++)
			{
				items.Add(_queue.Dequeue());
			}

			return items.AsEnumerable();
		}
	}
}
