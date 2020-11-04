using Microsoft.Azure.ServiceBus;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus.Rabbit;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus
{
	public class OutboundQueueProvider : IOutboundQueueProvider
	{
		private readonly RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;
		private static QueueClient _callClient = null;
		private static QueueClient _messageClient = null;
		private static QueueClient _notificationClient = null;
		private static QueueClient _shiftsClient = null;
		private static QueueClient _distributionListClient = null;

		public OutboundQueueProvider()
		{
			_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();

			VerifyAndCreateClients();
		}

		public async Task<bool> EnqueueCall(CallQueueItem callQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return _rabbitOutboundQueueProvider.EnqueueCall(callQueue);

			VerifyAndCreateClients();
			string serializedObject = String.Empty;

			try
			{
				serializedObject = ObjectSerialization.Serialize(callQueue);

				// We are limited to 256KB in azure queue messages
				var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
				if (size > 220000)
				{
					callQueue.Profiles = null;
					serializedObject = ObjectSerialization.Serialize(callQueue);
				}
			}
			catch { }

			// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				callQueue.Profiles = null;
				serializedObject = ObjectSerialization.Serialize(callQueue);
			}

			Message message = new Message(Encoding.UTF8.GetBytes(serializedObject));
			message.MessageId = string.Format("{0}|{1}", callQueue.Call.CallId, callQueue.Call.DispatchCount);

			return await SendMessage(_callClient, message);
		}

		public async Task<bool> EnqueueMessage(MessageQueueItem messageQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return _rabbitOutboundQueueProvider.EnqueueMessage(messageQueue);


			VerifyAndCreateClients();
			string serializedObject = String.Empty;

			if (messageQueue != null && messageQueue.Message != null && messageQueue.MessageId == 0 && messageQueue.Message.MessageId != 0)
				messageQueue.MessageId = messageQueue.Message.MessageId;

			try
			{
				serializedObject = ObjectSerialization.Serialize(messageQueue);

				// We are limited to 256KB in azure queue messages
				var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
				if (size > 220000)
				{
					messageQueue.Profiles = null;
					serializedObject = ObjectSerialization.Serialize(messageQueue);
				}

				if (ASCIIEncoding.Unicode.GetByteCount(serializedObject) > 220000)
				{
					messageQueue.Message.MessageRecipients = null;
					serializedObject = ObjectSerialization.Serialize(messageQueue);
				}
			}
			catch { }

			// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				messageQueue.Profiles = null;
				messageQueue.Message.MessageRecipients = null;
				serializedObject = ObjectSerialization.Serialize(messageQueue);
			}

			Message message = new Message(Encoding.UTF8.GetBytes(serializedObject));
			message.MessageId = string.Format("{0}|{1}", messageQueue.Message.MessageId, messageQueue.Message.ReceivingUserId);

			return await SendMessage(_messageClient, message);
		}

		public async Task<bool> EnqueueDistributionList(DistributionListQueueItem distributionListQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return _rabbitOutboundQueueProvider.EnqueueDistributionList(distributionListQueue);


			VerifyAndCreateClients();
			string serializedObject = String.Empty;

			try
			{
				serializedObject = ObjectSerialization.Serialize(distributionListQueue);

				// We are limited to 256KB in azure queue messages
				var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
				if (size > 220000)
				{
					distributionListQueue.Users = null;
					serializedObject = ObjectSerialization.Serialize(distributionListQueue);
				}

				// If were still too big, strip out some attachments
				if (size > 220000)
				{
					distributionListQueue.Message.Attachments = null;
					serializedObject = ObjectSerialization.Serialize(distributionListQueue);
				}
			}
			catch { }

			// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				distributionListQueue.Users = null;
				distributionListQueue.Message.Attachments = null;
				serializedObject = ObjectSerialization.Serialize(distributionListQueue);
			}

			Message message = new Message(Encoding.UTF8.GetBytes(serializedObject));
			message.MessageId = string.Format("{0}|{1}", distributionListQueue.Message.MessageID, distributionListQueue.List.DistributionListId);

			return await SendMessage(_distributionListClient, message);
		}

		public async Task<bool> EnqueueNotification(NotificationItem notificationQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return _rabbitOutboundQueueProvider.EnqueueNotification(notificationQueue);

			VerifyAndCreateClients();

			Message message = new Message(Encoding.UTF8.GetBytes(ObjectSerialization.Serialize(notificationQueue)));
			message.MessageId = string.Format("{0}", notificationQueue.GetHashCode());

			return await SendMessage(_notificationClient, message);
		}

		public async Task<bool> EnqueueShiftNotification(ShiftQueueItem shiftQueueItem)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				return _rabbitOutboundQueueProvider.EnqueueShiftNotification(shiftQueueItem);

			VerifyAndCreateClients();

			Message message = new Message(Encoding.UTF8.GetBytes(ObjectSerialization.Serialize(shiftQueueItem)));
			message.MessageId = Guid.NewGuid().ToString();

			return await SendMessage(_shiftsClient, message);
		}

		private async Task<bool> SendMessage(QueueClient client, Message message)
		{
			if (client != null)
			{
				int retry = 0;
				bool sent = false;

				while (!sent)
				{
					try
					{
						await client.SendAsync(message);
						sent = true;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, message.ToString());

						if (retry >= 5)
							return false;

						Thread.Sleep(1000);
						retry++;
					}
				}

				return sent;
			}

			return false;
		}

		private void VerifyAndCreateClients()
		{
			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueConnectionString))
			{
				while (_callClient == null || _callClient.IsClosedOrClosing)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5),
							EntityPath = Config.ServiceBusConfig.CallBroadcastQueueName
						};

						_callClient = new QueueClient(builder);
					}
					catch (TimeoutException) { }
				}
			}

			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueMessageConnectionString))
			{
				while (_messageClient == null || _messageClient.IsClosedOrClosing)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueMessageConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5),
							EntityPath = Config.ServiceBusConfig.MessageBroadcastQueueName
						};

						_messageClient = new QueueClient(builder);
					}
					catch (TimeoutException) { }
				}
			}


			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueNotificationConnectionString))
			{
				while (_notificationClient == null || _notificationClient.IsClosedOrClosing)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueNotificationConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5),
							EntityPath = Config.ServiceBusConfig.NotificaitonBroadcastQueueName
						};

						_notificationClient = new QueueClient(builder);
					}
					catch (TimeoutException) { }
				}
			}

			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueShiftsConnectionString))
			{
				while (_shiftsClient == null || _shiftsClient.IsClosedOrClosing)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueShiftsConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5),
							EntityPath = Config.ServiceBusConfig.ShiftNotificationsQueueName
						};

						_shiftsClient = new QueueClient(builder);
					}
					catch (TimeoutException) { }
				}
			}

			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueEmailConnectionString))
			{
				while (_distributionListClient == null || _distributionListClient.IsClosedOrClosing)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueEmailConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5),
							EntityPath = Config.ServiceBusConfig.EmailBroadcastQueueName
						};

						_distributionListClient = new QueueClient(builder);
					}
					catch (TimeoutException) { }
				}
			}
		}
	}
}
