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

namespace Resgrid.Workers.Framework.Workers.CalendarNotifier
{
	public class CalendarNotifierQueue : IQueue<CalendarNotifierQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<CalendarNotifierQueueItem> _queue;

		private ICalendarService _calendarService;
		private readonly IEventAggregator _eventAggregator;

		public CalendarNotifierQueue(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;

			_queue = new Queue<CalendarNotifierQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;

				_calendarService = Bootstrapper.GetKernel().Resolve<ICalendarService>();

				var t1 = new Task(async () =>
				{
					try
					{
						var calendarItems = await _calendarService.GetCalendarItemsToNotifyAsync(DateTime.UtcNow);

						if (calendarItems != null)
						{
							foreach (var calendarItem in calendarItems)
							{
								var qi = new CalendarNotifierQueueItem();
								qi.CalendarItem = calendarItem;

								_queue.Enqueue(qi);
							}
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

						_calendarService = null;
					}
				});

				t1.Start();
			}
		}

		public bool IsLocked
		{
			get { return _isLocked; }
		}

		public void EnsureExist()
		{
			if (_queue == null)
				_queue = new Queue<CalendarNotifierQueueItem>();
		}

		public async Task<bool> Clear()
		{
			_cleared = true;
			_queue.Clear();

			return _cleared;
		}

		public void AddItem(CalendarNotifierQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public CalendarNotifierQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<CalendarNotifierQueueItem>> GetItems(int maxItemsToReturn)
		{
			var items = new List<CalendarNotifierQueueItem>();

			await _eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.CalendarNotifier, Timestamp = DateTime.UtcNow});

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
