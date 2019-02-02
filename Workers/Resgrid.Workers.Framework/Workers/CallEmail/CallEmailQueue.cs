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

namespace Resgrid.Workers.Framework
{
	public class CallEmailQueue : IQueue<CallEmailQueueItem>
	{
		private bool _cleared;
		private bool _isLocked;
		private static Queue<CallEmailQueueItem> _queue;

		private IDepartmentsService _departmentsService;
		private readonly IEventAggregator _eventAggregator;

		public CallEmailQueue(/*IDepartmentsService departmentsService, */IEventAggregator eventAggregator)
		{
			//_departmentsService = departmentsService;
			_eventAggregator = eventAggregator;

			_queue = new Queue<CallEmailQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;
				Clear();

				_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();

				var task = new Task(() =>
									   {
										   try
										   {
											   var items = _departmentsService.GetAllDepartmentEmailSettings();

											   foreach (var i in items)
											   {
												   var cqi = new CallEmailQueueItem();
												   cqi.EmailSettings = i;

												   _queue.Enqueue(cqi);
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

				task.Start();
			}
		}

		public void EnsureExist()
		{
			if (_queue == null)
				_queue = new Queue<CallEmailQueueItem>();
		}

		public void Clear()
		{
			_cleared = true;

			_queue.Clear();
		}

		public bool IsLocked
		{
			get { return _isLocked; }
		}

		public void AddItem(CallEmailQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public CallEmailQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public IEnumerable<CallEmailQueueItem> GetItems(int maxItemsToReturn)
		{
			var items = new List<CallEmailQueueItem>();
			_eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.CallEmail, Timestamp = DateTime.UtcNow });

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
