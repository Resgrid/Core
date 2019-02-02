using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Workers.Framework.Workers.ReportDelivery
{
	public class ReportDeliveryQueue : IQueue<ReportDeliveryQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<ReportDeliveryQueueItem> _queue;

		private IDepartmentsService _departmentsService;
		private IScheduledTasksService _scheduledTasksService;
		private IUsersService _usersService;
		private readonly IEventAggregator _eventAggregator;

		public ReportDeliveryQueue(/*IDepartmentsService departmentsService, IScheduledTasksService scheduledTasksService, IUsersService usersService, */IEventAggregator eventAggregator)
		{
			//_departmentsService = departmentsService;
			//_scheduledTasksService = scheduledTasksService;
			//_usersService = usersService;
			_eventAggregator = eventAggregator;

			_queue = new Queue<ReportDeliveryQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;

				_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				_scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
				_usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();

				Task t1 = new Task(() =>
				{
					try
					{
						var allItems = _scheduledTasksService.GetUpcomingScheduledTaks();

						// Filter only the past items and ones that are 5 minutes 30 seconds in the future
						var items = from st in allItems
							let department = _departmentsService.GetDepartmentByUserId(st.UserId)
							let email = _usersService.GetMembershipByUserId(st.UserId).Email
							let runTime = st.WhenShouldJobBeRun(TimeConverterHelper.TimeConverter(DateTime.UtcNow, department))
							where
								st.TaskType == (int) TaskTypes.ReportDelivery && runTime.HasValue &&
								runTime.Value >= TimeConverterHelper.TimeConverter(DateTime.UtcNow, department) &&
								runTime.Value <= TimeConverterHelper.TimeConverter(DateTime.UtcNow, department).AddMinutes(5).AddSeconds(30)
							select new
							{
								ScheduledTask = st,
								Department = department,
								Email = email
							};

						foreach (var i in items)
						{
							var qi = new ReportDeliveryQueueItem();
							qi.ScheduledTask = i.ScheduledTask;
							qi.Department = i.Department;
							qi.Email = i.Email;

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

						_departmentsService = null;
						_scheduledTasksService = null;
						_usersService = null;
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
				_queue = new Queue<ReportDeliveryQueueItem>();
		}

		public void Clear()
		{
			_cleared = true;
			_queue.Clear();
		}

		public void AddItem(ReportDeliveryQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public ReportDeliveryQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public IEnumerable<ReportDeliveryQueueItem> GetItems(int maxItemsToReturn)
		{
			var items = new List<ReportDeliveryQueueItem>();

			_eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.ReportDelivery, Timestamp = DateTime.UtcNow });

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
