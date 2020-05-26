using System;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Workers.Notification
{
	public class NotificationCommand : ICommand<NotificationQueueItem>
	{
		private INotificationService _notificationService;
		private ICommunicationService _communicationService;

		public NotificationCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(NotificationQueueItem item)
		{
			if (item != null && item.Department != null && item.Notifications.Count > 0 && item.NotificationSettings.Count > 0)
			{
				_notificationService = Bootstrapper.GetKernel().Resolve<INotificationService>();
				_communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();

				var notificaitons = _notificationService.ProcessNotifications(item.Notifications, item.NotificationSettings);

				if (notificaitons != null)
				{
					foreach (var notification in notificaitons)
					{
						var text = _notificationService.GetMessageForType(notification);

						if (!String.IsNullOrWhiteSpace(text))
						{
							foreach (var user in notification.Users)
							{
								if (item.Profiles.ContainsKey(user))
								{
									var profile = item.Profiles[user];

									if (!_notificationService.AllowToSendViaSms(notification.Type))
										profile.SendNotificationSms = false;

									_communicationService.SendNotification(user, notification.DepartmentId, text, item.DepartmentTextNumber,
										"Notification", profile);
								}
								else
									_communicationService.SendNotification(user, notification.DepartmentId, text, item.DepartmentTextNumber);
							}
						}
					}
				}
			}

			_notificationService = null;
			_communicationService = null;
		}
	}
}
