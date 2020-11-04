using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Azure.ServiceBus.InteropExtensions;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Message = Microsoft.Azure.ServiceBus.Message;

namespace Resgrid.Workers.Framework.Workers.Notification
{
	public class NotificationQueue : IQueue<NotificationQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<NotificationQueueItem> _queue;

		private IDepartmentsService _departmentsService;
		private INotificationService _notificationService;
		private readonly IEventAggregator _eventAggregator;
		private IUserProfileService _userProfileService;
		private IDepartmentSettingsService _departmentSettingsService;

		public NotificationQueue(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;

			_queue = new Queue<NotificationQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;

				_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				_notificationService = Bootstrapper.GetKernel().Resolve<INotificationService>();
				_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
				_departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();

				var t1 = new Task(async () =>
				{
					try
					{
						var allNotifications = await _notificationService.GetAllAsync();
						var items = new List<ProcessedNotification>();

						Message message = null;
						while (message != null)
						{
							try
							{
								var item = new ProcessedNotification();

								if (message.UserProperties["DepartmentId"] != null)
									item.DepartmentId = int.Parse(message.UserProperties["DepartmentId"].ToString());

								if (message.UserProperties["Type"] != null)
									item.Type = (EventTypes) message.UserProperties["Type"];

								if (message.UserProperties["Value"] != null)
									item.Value = message.UserProperties["Value"].ToString();

								item.MessageId = message.MessageId;

								try
								{
									item.Data = message.GetBody<string>();
									items.Add(item);

									// Remove message from subscription
									//message.Complete();
								}
								catch (InvalidOperationException)
								{
									//message.Complete();
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);

								// Indicate a problem, unlock message in subscription
								//message.Abandon();
							}
						}

						var groupedItems = from i in items
							group i by i.DepartmentId
							into itemGroup
							orderby itemGroup.Key
							select itemGroup;

						foreach (var group in groupedItems)
						{
							var queueItem = new NotificationQueueItem();
							queueItem.Department = await _departmentsService.GetDepartmentByIdAsync(group.Key, false);
							queueItem.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(group.Key);
							queueItem.NotificationSettings = allNotifications.Where(x => x.DepartmentId == group.Key).ToList();
							queueItem.Notifications = group.ToList();
							queueItem.Profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(group.Key);

							_queue.Enqueue(queueItem);
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
						_notificationService = null;
						_userProfileService = null;
						_departmentSettingsService = null;
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
				_queue = new Queue<NotificationQueueItem>();
		}

		public async Task<bool> Clear()
		{
			_cleared = true;
			_queue.Clear();
			return _cleared;
		}

		public void AddItem(NotificationQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public NotificationQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<NotificationQueueItem>> GetItems(int maxItemsToReturn)
		{
			var items = new List<NotificationQueueItem>();

			await _eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.Notification, Timestamp = DateTime.UtcNow });

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
