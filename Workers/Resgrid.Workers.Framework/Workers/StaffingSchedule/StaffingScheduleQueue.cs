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
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Workers.Framework
{
	public class StaffingScheduleQueue : IQueue<StaffingScheduleQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<StaffingScheduleQueueItem> _queue;

		private IDepartmentsService _departmentsService;
		private IScheduledTasksService _scheduledTasksService;
		private readonly IEventAggregator _eventAggregator;

		public StaffingScheduleQueue(/*IDepartmentsService departmentsService, IScheduledTasksService scheduledTasksService, */IEventAggregator eventAggregator)
		{
			//_departmentsService = departmentsService;
			//_scheduledTasksService = scheduledTasksService;
			_eventAggregator = eventAggregator;

			_queue = new Queue<StaffingScheduleQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			Logging.LogTrace("StaffingJob: Entering PopulateQueue");

			if (!_isLocked)
			{
				_isLocked = true;
				_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				_scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();

				var t1 = new Task(async () =>
				{
					try
					{
						var allItems = await _scheduledTasksService.GetUpcomingScheduledTasksAsync();
						Logging.LogTrace(string.Format("StaffingJob: Analyzing {0} schedule tasks", allItems.Count));

						Dictionary<int, Department> departments = new Dictionary<int, Department>();
						foreach (var item in allItems)
						{
							if (!departments.ContainsKey(item.DepartmentId))
								departments.Add(item.DepartmentId, await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId));
						}

						// TODO: Prob a bug here as st.DepartmentId can be null
						// Filter only the past items and ones that are 5 minutes 30 seconds in the future
						var items = from st in allItems
							let department = departments[st.DepartmentId]
							let runTime = st.WhenShouldJobBeRun(TimeConverterHelper.TimeConverter(DateTime.UtcNow, department))
							where
								(st.TaskType == (int) TaskTypes.DepartmentStaffingReset || st.TaskType == (int) TaskTypes.UserStaffingLevel) &&
								runTime.HasValue && runTime.Value >= TimeConverterHelper.TimeConverter(DateTime.UtcNow, department) &&
								runTime.Value <= TimeConverterHelper.TimeConverter(DateTime.UtcNow, department).AddMinutes(5).AddSeconds(30)
							select new
							{
								ScheduledTask = st,
								Department = department
							};

						foreach (var i in items)
						{
							var qi = new StaffingScheduleQueueItem();
							qi.ScheduledTask = i.ScheduledTask;
							qi.Department = i.Department;

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
					}
				}, TaskCreationOptions.LongRunning);

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
				_queue = new Queue<StaffingScheduleQueueItem>();
		}

		public async Task<bool> Clear()
		{
			_cleared = true;
			_queue.Clear();

			return _cleared;
		}

		public void AddItem(StaffingScheduleQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public StaffingScheduleQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<StaffingScheduleQueueItem>> GetItems(int maxItemsToReturn)
		{
			var items = new List<StaffingScheduleQueueItem>();

			_eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.StaffingChange, Timestamp = DateTime.UtcNow });

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
