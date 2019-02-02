using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

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

				var t1 = new Task(() =>
				{
					try
					{
						var allNotifications = _notificationService.GetAll();
						var items = new List<ProcessedNotification>();

						BrokeredMessage message = null;
						while (message != null)
						{
							try
							{
								var item = new ProcessedNotification();

								if (message.Properties["DepartmentId"] != null)
									item.DepartmentId = int.Parse(message.Properties["DepartmentId"].ToString());

								if (message.Properties["Type"] != null)
									item.Type = (EventTypes) message.Properties["Type"];

								if (message.Properties["Value"] != null)
									item.Value = message.Properties["Value"].ToString();

								item.MessageId = message.MessageId;

								try
								{
									item.Data = message.GetBody<string>();
									items.Add(item);

									// Remove message from subscription
									message.Complete();
								}
								catch (InvalidOperationException)
								{
									message.Complete();
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);

								// Indicate a problem, unlock message in subscription
								message.Abandon();
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
							queueItem.Department = _departmentsService.GetDepartmentById(group.Key, false);
							queueItem.DepartmentTextNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(group.Key);
							queueItem.NotificationSettings = allNotifications.Where(x => x.DepartmentId == group.Key).ToList();
							queueItem.Notifications = group.ToList();
							queueItem.Profiles = _userProfileService.GetAllProfilesForDepartment(group.Key);

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

		public void Clear()
		{
			_cleared = true;
			_queue.Clear();
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

		public IEnumerable<NotificationQueueItem> GetItems(int maxItemsToReturn)
		{
			var items = new List<NotificationQueueItem>();

			_eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.Notification, Timestamp = DateTime.UtcNow });

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
