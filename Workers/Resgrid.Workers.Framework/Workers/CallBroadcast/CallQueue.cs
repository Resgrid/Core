using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Workers.Framework
{
	public class CallQueue : IQueue<CallQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<CallQueueItem> _queue;

		private ICallsService _callsService;
		private IQueueService _queueService;
		private readonly IEventAggregator _eventAggregator;
		private IUserProfileService _userProfileService;

		public CallQueue(/*ICallsService callsService, IQueueService queueService, IJobsService jobsService, IUserProfileService userProfileService, */IEventAggregator eventAggregator)
		{
			//_callsService = callsService;
			//_queueService = queueService;
			//_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
			
			_queue = new Queue<CallQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;

				Task t1 = new Task(() =>
									   {
										   try
										   {
											   if (Config.SystemBehaviorConfig.IsAzure)
											   {
													 _queue.Enqueue(new CallQueueItem());
											   }
											   else
											   {
													 _callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
													 _queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
													 _userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

												   var items = _queueService.Dequeue(QueueTypes.CallBroadcast);

												   foreach (var i in items)
												   {
													   var cqi = new CallQueueItem();
													   cqi.QueueItem = i;
														 cqi.Call = _callsService.GetCallById(int.Parse(i.SourceId));
													   cqi.Profiles = _userProfileService.GetSelectedUserProfiles(cqi.Call.Dispatches.Select(x => x.UserId).ToList());

													   _queue.Enqueue(cqi);
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

												 _callsService = null;
												 _queueService = null;
												 _userProfileService = null;
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
				_queue = new Queue<CallQueueItem>();
		}

		public void Clear()
		{
			_cleared = true;

			_queue.Clear();

			if (!Config.SystemBehaviorConfig.IsAzure)
			{
				try
				{
					_queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();

					var queueItems = _queue.Select(x => x.QueueItem).ToList();
					_queueService.RequeueAll(queueItems);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
				finally
				{
					_queueService = null;
				}
			}
		}

		public void AddItem(CallQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public CallQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public IEnumerable<CallQueueItem> GetItems(int maxItemsToReturn)
		{
			var items = new List<CallQueueItem>();

			_eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.Broadcast, Timestamp = DateTime.UtcNow });


			if (_queue.Count <= 0)
				PopulateQueue();

			while (_isLocked)
			{
				Thread.Sleep(100);
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
