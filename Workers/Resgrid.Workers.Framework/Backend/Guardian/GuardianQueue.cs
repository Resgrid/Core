using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Backend
{
	public class GuardianQueue : IQueue<GuardianQueueItem>
	{
		private static bool _cleared;
		private static object _lock;
		private static bool _isLocked;
		private static Queue<GuardianQueueItem> _queue;

		private readonly IJobsService _jobsService;

		public GuardianQueue(IJobsService jobsService)
		{
			_jobsService = jobsService;

			_queue = new Queue<GuardianQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;
				Clear();

				var t1 = new Task(() =>
					                   {
						                   try
						                   {
							                   var items = _jobsService.GetAllBatchJobs();

							                   foreach (var i in items)
							                   {
								                   GuardianQueueItem cqi = new GuardianQueueItem();
								                   cqi.Job = i;

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
						                   }
					                   });

				t1.Start();
			}
		}

		public void EnsureExist()
		{
			if (_queue == null)
				_queue = new Queue<GuardianQueueItem>();
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

		public void AddItem(GuardianQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public GuardianQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public IEnumerable<GuardianQueueItem> GetItems(int maxItemsToReturn)
		{
			var items = new List<GuardianQueueItem>();
			_jobsService.SetJobAsChecked(JobTypes.Guardian);

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
				items.Add(_queue.Dequeue());
			}

			return items.AsEnumerable();
		}
	}
}
