using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Workers.Framework
{
	public class CallPruneQueue : IQueue<CallPruneQueueItem>
	{
		private bool _cleared;
		private bool _isLocked;
		private static Queue<CallPruneQueueItem> _queue;

		private IDepartmentsService _departmentsService;
		private readonly IEventAggregator _eventAggregator;

		public CallPruneQueue(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;

			_queue = new Queue<CallPruneQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;
				Clear();

				_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();

				var t1 = new Task(() =>
					                   {
						                   try
						                   {
							                   var items = _departmentsService.GetAllDepartmentCallPrunings();

							                   foreach (var i in items)
							                   {
								                   var item = new CallPruneQueueItem();
								                   item.PruneSettings = i;

								                   _queue.Enqueue(item);
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

							                   _departmentsService = null;
						                   }
					                   });

				t1.Start();
			}
		}

		public void EnsureExist()
		{
			if (_queue == null)
				_queue = new Queue<CallPruneQueueItem>();
		}

		public void Clear()
		{
			_cleared = true;

			var items = _queue.AsEnumerable();
			_queue.Clear();
		}
		
		public bool IsLocked
		{
			get { return _isLocked; }
		}

		public void AddItem(CallPruneQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public CallPruneQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public IEnumerable<CallPruneQueueItem> GetItems(int maxItemsToReturn)
		{
			var items = new List<CallPruneQueueItem>();
			_eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.CallPrune, Timestamp = DateTime.UtcNow });

			if (_queue.Count <= 0)
				PopulateQueue();

			while (_isLocked)
			{
				Thread.Sleep(1000);
			}

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

			return items.AsEnumerable();
		}
	}
}
