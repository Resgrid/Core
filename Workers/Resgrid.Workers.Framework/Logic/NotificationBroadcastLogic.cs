using Microsoft.ServiceBus.Messaging;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.Notification;
using System;
using System.Collections.Generic;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	public class NotificationBroadcastLogic
	{
		private QueueClient _client = null;

		public NotificationBroadcastLogic()
		{
			while (_client == null)
			{
				try
				{
					_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueNotificationConnectionString, Config.ServiceBusConfig.NotificaitonBroadcastQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(NotificationItem item)
		{
			if (Config.SystemBehaviorConfig.IsAzure)
			{
				ProcessQueueMessage(_client.Receive());
			}
			else
			{
				ProcessNotificationItem(item, Guid.NewGuid().ToString(), "");
			}
		}

		public static Tuple<bool, string> ProcessQueueMessage(BrokeredMessage message)
		{
			bool success = true;
			string result = "";

			if (message != null)
			{
				try
				{
					var body = message.GetBody<string>();

					if (!String.IsNullOrWhiteSpace(body))
					{
						NotificationItem ni = null;
						try
						{
							ni = ObjectSerialization.Deserialize<NotificationItem>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							message.DeadLetter();
						}

						ProcessNotificationItem(ni, message.MessageId, body);
					}
					else
					{
						success = false;
						result = "Message body is null or empty";
					}

					try
					{
						message.Complete();
					}
					catch (MessageLockLostException)
					{

					}
				}
				catch (Exception ex)
				{
					success = false;
					result = ex.ToString();

					Logging.LogException(ex);
					message.Abandon();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static void ProcessNotificationItem(NotificationItem ni, string messageId, string body)
		{
			if (ni != null)
			{
				var _notificationService = Bootstrapper.GetKernel().Resolve<INotificationService>();
				var _communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
				var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var _userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
				var _departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();

				var item = new ProcessedNotification();

				if (ni.DepartmentId != 0)
					item.DepartmentId = ni.DepartmentId;
				else
					item.DepartmentId = _notificationService.GetDepartmentIdForType(ni);

				item.Type = (EventTypes)ni.Type;
				item.Value = ni.Value;
				item.MessageId = messageId;
				item.Data = body;
				item.ItemId = ni.ItemId;

				var queueItem = new NotificationQueueItem();
				queueItem.Department = _departmentsService.GetDepartmentById(item.DepartmentId, false);
				queueItem.DepartmentTextNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(item.DepartmentId);
				queueItem.NotificationSettings = _notificationService.GetNotificationsByDepartment(item.DepartmentId);
				queueItem.Profiles = _userProfileService.GetAllProfilesForDepartment(item.DepartmentId);

				queueItem.Notifications = new List<ProcessedNotification>();
				queueItem.Notifications.Add(item);

				var notificaitons = _notificationService.ProcessNotifications(queueItem.Notifications, queueItem.NotificationSettings);
				if (notificaitons != null)
				{
					foreach (var notification in notificaitons)
					{
						var text = _notificationService.GetMessageForType(notification);

						if (!String.IsNullOrWhiteSpace(text))
						{
							foreach (var user in notification.Users)
							{
								if (queueItem.Profiles.ContainsKey(user))
									_communicationService.SendNotification(user, notification.DepartmentId, text, queueItem.DepartmentTextNumber, "Notification", queueItem.Profiles[user]);
								//else
								//	_communicationService.SendNotification(user, notification.DepartmentId, text, queueItem.DepartmentTextNumber);
							}
						}
					}
				}

				_notificationService = null;
				_communicationService = null;
				_departmentsService = null;
				_userProfileService = null;
				_departmentSettingsService = null;
			}
		}
	}
}
