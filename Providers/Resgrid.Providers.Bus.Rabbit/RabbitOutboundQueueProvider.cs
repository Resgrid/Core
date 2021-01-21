using System.Text;
using Resgrid.Config;
using Resgrid.Model.Queue;
using Resgrid.Framework;
using System;
using RabbitMQ.Client;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System.Collections.Generic;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitOutboundQueueProvider : IRabbitOutboundQueueProvider
	{
		public RabbitOutboundQueueProvider()
		{
			VerifyAndCreateClients();
		}

		public bool EnqueueCall(CallQueueItem callQueue)
		{
			string serializedObject = String.Empty;

			//try
			//{
			//	serializedObject = ObjectSerialization.Serialize(callQueue);

			//	// We are limited to 256KB in azure queue messages
			//	var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
			//	if (size > 220000)
			//	{
			//		callQueue.Profiles = null;
			//		serializedObject = ObjectSerialization.Serialize(callQueue);
			//	}
			//}
			//catch { }

			//// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			//if (String.IsNullOrWhiteSpace(serializedObject))
			//{
			//	callQueue.Profiles = null;
			serializedObject = ObjectSerialization.Serialize(callQueue);
			//}

			return SendMessage(ServiceBusConfig.CallBroadcastQueueName, serializedObject);
		}

		public bool EnqueueMessage(MessageQueueItem messageQueue)
		{
			string serializedObject = String.Empty;

			if (messageQueue != null && messageQueue.Message != null && messageQueue.MessageId == 0 && messageQueue.Message.MessageId != 0)
				messageQueue.MessageId = messageQueue.Message.MessageId;

			//try
			//{
			//	serializedObject = ObjectSerialization.Serialize(messageQueue);

			//	// We are limited to 256KB in azure queue messages
			//	var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
			//	if (size > 220000)
			//	{
			//		messageQueue.Profiles = null;
			//		serializedObject = ObjectSerialization.Serialize(messageQueue);
			//	}

			//	if (ASCIIEncoding.Unicode.GetByteCount(serializedObject) > 220000)
			//	{
			//		messageQueue.Message.MessageRecipients = null;
			//		serializedObject = ObjectSerialization.Serialize(messageQueue);
			//	}
			//}
			//catch { }

			//// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			//if (String.IsNullOrWhiteSpace(serializedObject))
			//{
			//	messageQueue.Profiles = null;
			//	messageQueue.Message.MessageRecipients = null;
			serializedObject = ObjectSerialization.Serialize(messageQueue);
			//}

			return SendMessage(ServiceBusConfig.MessageBroadcastQueueName, serializedObject);
		}

		public bool EnqueueDistributionList(DistributionListQueueItem distributionListQueue)
		{
			string serializedObject = String.Empty;

			//try
			//{
			//	serializedObject = ObjectSerialization.Serialize(distributionListQueue);

			//	// We are limited to 256KB in azure queue messages
			//	var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
			//	if (size > 220000)
			//	{
			//		distributionListQueue.Users = null;
			//		serializedObject = ObjectSerialization.Serialize(distributionListQueue);
			//	}

			//	// If were still too big, strip out some attachments
			//	if (size > 220000)
			//	{
			//		distributionListQueue.Message.Attachments = null;
			//		serializedObject = ObjectSerialization.Serialize(distributionListQueue);
			//	}
			//}
			//catch { }

			//// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			//if (String.IsNullOrWhiteSpace(serializedObject))
			//{
			//	distributionListQueue.Users = null;
			//	distributionListQueue.Message.Attachments = null;
			serializedObject = ObjectSerialization.Serialize(distributionListQueue);
			//}

			return SendMessage(ServiceBusConfig.EmailBroadcastQueueName, serializedObject);
		}

		public bool EnqueueNotification(NotificationItem notificationQueue)
		{
			string serializedObject = String.Empty;

			serializedObject = ObjectSerialization.Serialize(notificationQueue);

			return SendMessage(ServiceBusConfig.NotificaitonBroadcastQueueName, serializedObject);
		}

		public bool EnqueueShiftNotification(ShiftQueueItem shiftQueueItem)
		{
			string serializedObject = String.Empty;

			serializedObject = ObjectSerialization.Serialize(shiftQueueItem);

			return SendMessage(ServiceBusConfig.ShiftNotificationsQueueName, serializedObject);
		}

		public bool EnqueueCqrsEvent(CqrsEvent cqrsEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);

			return SendMessage(ServiceBusConfig.SystemQueueName, serializedObject);
		}

		public bool EnqueuePaymentEvent(CqrsEvent cqrsEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);

			return SendMessage(ServiceBusConfig.PaymentQueueName, serializedObject);
		}

		public bool VerifyAndCreateClients()
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				try
				{
					var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
					using (var connection = factory.CreateConnection())
					{
						using (var channel = connection.CreateModel())
						{
							channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.SystemQueueName),
										 durable: true,
										 exclusive: false,
										 autoDelete: false,
										 arguments: null);

							channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.CallBroadcastQueueName),
										 durable: true,
										 exclusive: false,
										 autoDelete: false,
										 arguments: null);

							channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.MessageBroadcastQueueName),
										 durable: true,
										 exclusive: false,
										 autoDelete: false,
										 arguments: null);

							channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.EmailBroadcastQueueName),
										 durable: true,
										 exclusive: false,
										 autoDelete: false,
										 arguments: null);

							channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.NotificaitonBroadcastQueueName),
										 durable: true,
										 exclusive: false,
										 autoDelete: false,
										 arguments: null);

							channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.ShiftNotificationsQueueName),
										 durable: true,
										 exclusive: false,
										 autoDelete: false,
										 arguments: null);

							channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.PaymentQueueName),
										 durable: true,
										 exclusive: false,
										 autoDelete: false,
										 arguments: null);
						}
					}

					return true;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return false;
				}
			}

			return false;
		}

		private bool SendMessage(string queueName, string message)
		{
			//if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			//{
			try
			{
				// TODO: Maybe? https://github.com/EasyNetQ/EasyNetQ -SJ
				var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				using (var connection = factory.CreateConnection())
				{
					using (var channel = connection.CreateModel())
					{
						IBasicProperties props = channel.CreateBasicProperties();
						props.DeliveryMode = 2;
						props.Expiration = "36000000";
						props.Headers = new Dictionary<string, object>();
						props.Headers.Add("x-redelivered-count", 0);

						channel.BasicPublish(exchange: ServiceBusConfig.RabbbitExchange,
										 routingKey: SetQueueNameForEnv(queueName),
										 basicProperties: props,
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
			//}

			//return false;
		}

		private static string SetQueueNameForEnv(string cacheKey)
		{
			if (Config.SystemBehaviorConfig.Environment == SystemEnvironment.Dev)
				return $"DEV{cacheKey}";
			else if (Config.SystemBehaviorConfig.Environment == SystemEnvironment.QA)
				return $"QA{cacheKey}";
			else if (Config.SystemBehaviorConfig.Environment == SystemEnvironment.Staging)
				return $"ST{cacheKey}";

			return cacheKey;
		}
	}
}
