using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.Notification;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	public class NotificationBroadcastLogic
	{
		public static async Task<bool> ProcessNotificationItem(NotificationItem ni, string messageId, string body)
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
					item.DepartmentId = await _notificationService.GetDepartmentIdForTypeAsync(ni);

				if (ConfigHelper.CanTransmit(item.DepartmentId))
				{
					item.Type = (EventTypes)ni.Type;
					item.Value = ni.Value;
					item.MessageId = messageId;

					if (!String.IsNullOrWhiteSpace(body))
						item.Data = body;
					else
						item.Data = ObjectSerialization.Serialize(ni);

					item.ItemId = ni.ItemId;

					var queueItem = new NotificationQueueItem();
					queueItem.Department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId, false);
					queueItem.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.DepartmentId);
					queueItem.NotificationSettings = await _notificationService.GetNotificationsByDepartmentAsync(item.DepartmentId);
					queueItem.Profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(item.DepartmentId);

					queueItem.Notifications = new List<ProcessedNotification>();
					queueItem.Notifications.Add(item);

					var notificaitons = await _notificationService.ProcessNotificationsAsync(queueItem.Notifications, queueItem.NotificationSettings);
					if (notificaitons != null)
					{
						foreach (var notification in notificaitons)
						{
							var text = await _notificationService.GetMessageForTypeAsync(notification);

							if (!String.IsNullOrWhiteSpace(text))
							{
								foreach (var user in notification.Users)
								{
									if (queueItem.Profiles.ContainsKey(user))
									{
										var profile = queueItem.Profiles[user];

										if (!_notificationService.AllowToSendViaSms(notification.Type))
											profile.SendNotificationSms = false;

										await _communicationService.SendNotificationAsync(user, notification.DepartmentId, text, queueItem.DepartmentTextNumber, "Notification", profile);
									}
								}
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

			return true;
		}
	}
}
