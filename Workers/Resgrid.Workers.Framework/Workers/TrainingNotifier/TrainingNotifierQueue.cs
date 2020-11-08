using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Workers.Framework.Workers.TrainingNotifier
{
	public class TrainingNotifierQueue : IQueue<TrainingNotifierQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<TrainingNotifierQueueItem> _queue;

		private ITrainingService _trainingService;
		private readonly IEventAggregator _eventAggregator;

		public TrainingNotifierQueue(/*ITrainingService trainingService, */IEventAggregator eventAggregator)
		{
			//_trainingService = trainingService;
			_eventAggregator = eventAggregator;

			_queue = new Queue<TrainingNotifierQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;

				_trainingService = Bootstrapper.GetKernel().Resolve<ITrainingService>();

				var t1 = new Task(async () =>
				{
					try
					{
						var trainings = await _trainingService.GetTrainingsToNotifyAsync(DateTime.UtcNow);

						if (trainings != null)
						{
							foreach (var training in trainings)
							{
								var qi = new TrainingNotifierQueueItem();
								qi.Training = training;

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

						_trainingService = null;
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
				_queue = new Queue<TrainingNotifierQueueItem>();
		}

		public async Task<bool> Clear()
		{
			_cleared = true;
			_queue.Clear();

			return _cleared;
		}

		public void AddItem(TrainingNotifierQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public TrainingNotifierQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<TrainingNotifierQueueItem>> GetItems(int maxItemsToReturn)
		{
			var items = new List<TrainingNotifierQueueItem>();

			_eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.TrainingNotifier, Timestamp = DateTime.UtcNow});

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
