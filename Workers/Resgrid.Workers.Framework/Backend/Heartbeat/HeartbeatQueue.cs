using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.InteropExtensions;
using Resgrid.Framework;
using Resgrid.Model;
using Message = Microsoft.Azure.ServiceBus.Message;

namespace Resgrid.Workers.Framework.Backend.Heartbeat
{
	public class HeartbeatQueue : IQueue<HeartbeatQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<HeartbeatQueueItem> _queue;

		public HeartbeatQueue()
		{
			_queue = new Queue<HeartbeatQueueItem>();
			_cleared = false;

		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;

				var t1 = new Task(() =>
				{
					try
					{
						Message message = null;
						while (message != null)
						{
							try
							{
								var queueItem = new HeartbeatQueueItem();

								if (message.UserProperties["Type"] != null)
									queueItem.Type = (HeartbeatTypes) int.Parse(message.UserProperties["Type"].ToString());

								//if (message.Properties["Timestamp"] != null)
								//	queueItem.Timestamp = DateTime.Parse(message.Properties["Timestamp"].ToString());

								try
								{
									queueItem.Data = message.GetBody<string>();
									_queue.Enqueue(queueItem);

									// Remove message from subscription
									//message.Complete();
								}
								catch (System.ServiceModel.FaultException)
								{
									message = null;
								}
								catch (TimeoutException)
								{
									message = null;
								}
								catch (MessageLockLostException)
								{
								}
								catch (InvalidOperationException)
								{
									//message.Complete();
								}
							}
							catch (MessageLockLostException)
							{
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);

								// Indicate a problem, unlock message in subscription
								//message.Abandon();
							}
						}
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

		public bool IsLocked
		{
			get { return _isLocked; }
		}

		public void EnsureExist()
		{
			if (_queue == null)
				_queue = new Queue<HeartbeatQueueItem>();
		}

		public async Task<bool> Clear()
		{
			_cleared = true;
			_queue.Clear();

			return _cleared;
		}

		public void AddItem(HeartbeatQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public HeartbeatQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<HeartbeatQueueItem>> GetItems(int maxItemsToReturn)
		{
			var items = new List<HeartbeatQueueItem>();

			if (_queue.Count <= 0)
				PopulateQueue();

			while (_isLocked)
			{
				Thread.Sleep(500);
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
