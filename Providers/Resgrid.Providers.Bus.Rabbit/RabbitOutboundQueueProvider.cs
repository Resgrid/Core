using System.Text;
using Resgrid.Config;
using Resgrid.Model.Queue;
using Resgrid.Framework;
using System;
using RabbitMQ.Client;
using Resgrid.Model;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitOutboundQueueProvider
	{
		public RabbitOutboundQueueProvider()
		{
			VerifyAndCreateClients();
		}

		public void EnqueueCall(CallQueueItem callQueue)
		{
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

			// If we get an Excpetion, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				callQueue.Profiles = null;
				serializedObject = ObjectSerialization.Serialize(callQueue);
			}

			//BrokeredMessage message = new BrokeredMessage(serializedObject);
			//message.MessageId = string.Format("{0}|{1}", callQueue.Call.CallId, callQueue.Call.DispatchCount);

			SendMessage(ServiceBusConfig.CallBroadcastQueueName, serializedObject);
		}

		public void EnqueueMessage(MessageQueueItem messageQueue)
		{
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

			// If we get an Excpetion, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				messageQueue.Profiles = null;
				messageQueue.Message.MessageRecipients = null;
				serializedObject = ObjectSerialization.Serialize(messageQueue);
			}

			SendMessage(ServiceBusConfig.MessageBroadcastQueueName, serializedObject);
		}

		public void EnqueueDistributionList(DistributionListQueueItem distributionListQueue)
		{
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

			// If we get an Excpetion, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				distributionListQueue.Users = null;
				distributionListQueue.Message.Attachments = null;
				serializedObject = ObjectSerialization.Serialize(distributionListQueue);
			}

			SendMessage(ServiceBusConfig.EmailBroadcastQueueName, serializedObject);
		}

		public void EnqueueNotification(NotificationItem notificationQueue)
		{
			string serializedObject = String.Empty;

			serializedObject = ObjectSerialization.Serialize(notificationQueue);

			SendMessage(ServiceBusConfig.NotificaitonBroadcastQueueName, serializedObject);
		}

		public void EnqueueShiftNotification(ShiftQueueItem shiftQueueItem)
		{
			string serializedObject = String.Empty;

			serializedObject = ObjectSerialization.Serialize(shiftQueueItem);

			SendMessage(ServiceBusConfig.ShiftNotificationsQueueName, serializedObject);
		}

		public void EnqueueCqrsEvent(CqrsEvent cqrsEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);

			SendMessage(ServiceBusConfig.SystemQueueName, serializedObject);
		}

		public static void VerifyAndCreateClients()
		{
			try
			{
				var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				using (var connection = factory.CreateConnection())
				{
					using (var channel = connection.CreateModel())
					{
						channel.QueueDeclare(queue: ServiceBusConfig.SystemQueueName,
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: ServiceBusConfig.CallBroadcastQueueName,
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: ServiceBusConfig.MessageBroadcastQueueName,
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: ServiceBusConfig.EmailBroadcastQueueName,
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: ServiceBusConfig.NotificaitonBroadcastQueueName,
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: ServiceBusConfig.ShiftNotificationsQueueName,
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);
					}
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		private bool SendMessage(string queueName, string message)
		{
			try
			{
				var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				using (var connection = factory.CreateConnection())
				{
					using (var channel = connection.CreateModel())
					{
						channel.BasicPublish(exchange: ServiceBusConfig.RabbbitExchange,
									 routingKey: queueName,
									 basicProperties: null,
									 body: Encoding.ASCII.GetBytes(message));
					}

					return true;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}
	}
}
