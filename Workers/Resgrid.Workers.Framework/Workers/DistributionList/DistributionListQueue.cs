using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Workers.Framework.Workers.DistributionList
{
	public class DistributionListQueue : IQueue<DistributionListQueueItem>
	{
		private bool _cleared;
		private bool _isLocked;
		private static Queue<DistributionListQueueItem> _queue;

		private IDistributionListsService _distributionListsService;
		private readonly IEventAggregator _eventAggregator;

		public DistributionListQueue(/*IDistributionListsService distributionListsService, */IEventAggregator eventAggregator)
		{
			//_distributionListsService = distributionListsService;
			_eventAggregator = eventAggregator;

			_queue = new Queue<DistributionListQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;
				Clear();

				_distributionListsService = Bootstrapper.GetKernel().Resolve<IDistributionListsService>();

				var t1 = new Task(async () =>
														 {
															 try
															 {
																 var items = await _distributionListsService.GetAllActiveDistributionListsAsync();

																 foreach (var i in items)
																 {
																	 var qi = new DistributionListQueueItem();
																	 qi.List = i;

																	 _queue.Enqueue(qi);
																 }
															 }
															 catch (Exception ex)
															 {
																 Logging.LogException(ex);
															 }
															 finally
															 {
																 _isLocked = false;
																 _cleared = false;

																 _distributionListsService = null;
															 }
														 });

				t1.Start();
			}
		}

		public void EnsureExist()
		{
			if (_queue == null)
				_queue = new Queue<DistributionListQueueItem>();
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

		public void AddItem(DistributionListQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public DistributionListQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<DistributionListQueueItem>> GetItems(int maxItemsToReturn)
		{
			List<DistributionListQueueItem> items = new List<DistributionListQueueItem>();

			await _eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.DistributionList, Timestamp = DateTime.UtcNow });

			int count = 0;
			if (_queue.Count < maxItemsToReturn)
				count = _queue.Count;
			else
				count = maxItemsToReturn;

			for (int i = 0; i < count; i++)
			{
				if (_queue.Count > 0)
					items.Add(_queue.Dequeue());
			}

			if (_queue.Count <= 0)
				PopulateQueue();

			return items.AsEnumerable();
		}
	}
}
