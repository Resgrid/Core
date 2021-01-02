using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.Notification;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.InteropExtensions;
using Message = Microsoft.Azure.ServiceBus.Message;

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
					//_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueNotificationConnectionString, Config.ServiceBusConfig.NotificaitonBroadcastQueueName);
					_client = new QueueClient(Config.ServiceBusConfig.AzureQueueNotificationConnectionString, Config.ServiceBusConfig.NotificaitonBroadcastQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public async Task<bool> Process(NotificationItem item)
		{
			if (Config.SystemBehaviorConfig.IsAzure)
			{
				var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
				{
					MaxConcurrentCalls = 1,
					AutoComplete = false
				};

				// Register the function that will process messages
				_client.RegisterMessageHandler(ProcessQueueMessage, messageHandlerOptions);

				//ProcessQueueMessage(_client.Receive());
			}
			else
			{
				return await ProcessNotificationItem(item, Guid.NewGuid().ToString(), "");
			}

			return false;
		}

		public async Task<Tuple<bool, string>> ProcessQueueMessage(Message message, CancellationToken token)
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
							//message.DeadLetter();
							await _client.DeadLetterAsync(message.SystemProperties.LockToken); 
						}

						await ProcessNotificationItem(ni, message.MessageId, body);
					}
					else
					{
						success = false;
						result = "Message body is null or empty";
					}

					try
					{
						//message.Complete();
						await _client.CompleteAsync(message.SystemProperties.LockToken);
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
					//message.Abandon();
					await _client.AbandonAsync(message.SystemProperties.LockToken); 
				}
			}

			return new Tuple<bool, string>(success, result);
		}

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

				item.Type = (EventTypes)ni.Type;
				item.Value = ni.Value;
				item.MessageId = messageId;
				item.Data = body;
				item.ItemId = ni.ItemId;

				var queueItem = new NotificationQueueItem();
				queueItem.Department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId, false);
				queueItem.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.DepartmentId);
				queueItem.NotificationSettings = await _notificationService.GetNotificationsByDepartmentAsync(item.DepartmentId);
				queueItem.Profiles = await  _userProfileService.GetAllProfilesForDepartmentAsync(item.DepartmentId);

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

				_notificationService = null;
				_communicationService = null;
				_departmentsService = null;
				_userProfileService = null;
				_departmentSettingsService = null;
			}

			return true;
		}

		static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
		{
			//Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
			//var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
			//Console.WriteLine("Exception context for troubleshooting:");
			//Console.WriteLine($"- Endpoint: {context.Endpoint}");
			//Console.WriteLine($"- Entity Path: {context.EntityPath}");
			//Console.WriteLine($"- Executing Action: {context.Action}");
			return Task.CompletedTask;
		}
	}
}
