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

namespace Resgrid.Workers.Framework.Workers.MessageBroadcast
{
	public class MessageQueue : IQueue<MessageQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<MessageQueueItem> _queue;

		private IMessageService _messageService;
		private IQueueService _queueService;
		private readonly IEventAggregator _eventAggregator;
		private IUserProfileService _userProfileService;

		public MessageQueue(/*IMessageService messageService, IQueueService queueService, IUserProfileService userProfileService, */IEventAggregator eventAggregator)
		{
			//_messageService = messageService;
			//_queueService = queueService;
			_eventAggregator = eventAggregator;
			//_userProfileService = userProfileService;

			_queue = new Queue<MessageQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;

				var t1 = new Task(async () =>
															{
																try
																{
																	if (Config.SystemBehaviorConfig.IsAzure)
																	{
																		_queue.Enqueue(new MessageQueueItem());
																	}
																	else
																	{
																		_messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();
																		_queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
																		_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

																		var items = await _queueService.DequeueAsync(QueueTypes.MessageBroadcast);

																		foreach (var i in items)
																		{
																			var cqi = new MessageQueueItem();
																			cqi.QueueItem = i;
																			cqi.Message = await _messageService.GetMessageByIdAsync(int.Parse(i.SourceId));

																			var users = new List<string>();

																			if (!String.IsNullOrWhiteSpace(cqi.Message.ReceivingUserId))
																				users.Add(cqi.Message.ReceivingUserId);

																			if (!String.IsNullOrWhiteSpace(cqi.Message.SendingUserId))
																				users.Add(cqi.Message.SendingUserId);

																			cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(users);

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

																	_messageService = null;
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
				_queue = new Queue<MessageQueueItem>();
		}

		public async Task<bool> Clear()
		{
			_cleared = true;

			_queue.Clear();

			if (!Config.SystemBehaviorConfig.IsAzure)
			{
				try
				{
					_queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();

					var queueItems = _queue.Select(x => x.QueueItem).ToList();
					await _queueService.RequeueAllAsync(queueItems);
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

			return true;
		}

		public void AddItem(MessageQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public MessageQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<MessageQueueItem>> GetItems(int maxItemsToReturn)
		{
			var items = new List<MessageQueueItem>();

			await _eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.MessageBroadcast, Timestamp = DateTime.UtcNow });

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
