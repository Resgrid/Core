using System.Text;
using Resgrid.Config;
using Resgrid.Model.Queue;
using Resgrid.Framework;
using System;
using RabbitMQ.Client;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System.Collections.Generic;
using Resgrid.Model.Events;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitOutboundQueueProvider : IRabbitOutboundQueueProvider
	{
		private static bool _hasBeenInitialized = false;

		public RabbitOutboundQueueProvider()
		{
			VerifyAndCreateClients();
		}

		public bool EnqueueCall(CallQueueItem callQueue)
		{
			string serializedObject = ObjectSerialization.Serialize(callQueue);

			return SendMessage(ServiceBusConfig.CallBroadcastQueueName, serializedObject);
		}

		public bool EnqueueMessage(MessageQueueItem messageQueue)
		{
			string serializedObject = ObjectSerialization.Serialize(messageQueue);

			if (messageQueue != null && messageQueue.Message != null && messageQueue.MessageId == 0 && messageQueue.Message.MessageId != 0)
				messageQueue.MessageId = messageQueue.Message.MessageId;

			return SendMessage(ServiceBusConfig.MessageBroadcastQueueName, serializedObject);
		}

		public bool EnqueueDistributionList(DistributionListQueueItem distributionListQueue)
		{
			string serializedObject = ObjectSerialization.Serialize(distributionListQueue);

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

		public bool EnqueueAuditEvent(AuditEvent auditEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(auditEvent);

			return SendMessage(ServiceBusConfig.AuditQueueName, serializedObject);
		}

		public bool VerifyAndCreateClients()
		{
			if (!_hasBeenInitialized)
			{
				if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				{
					try
					{
						//var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
						using (var connection = CreateConnection())
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

								channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.AuditQueueName),
											 durable: true,
											 exclusive: false,
											 autoDelete: false,
											 arguments: null);
							}
						}

						_hasBeenInitialized = true;

						return true;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
						return false;
					}
				}
			}

			return false;
		}

		private IConnection CreateConnection()
		{
			ConnectionFactory factory;
			IConnection connection;

			// I know....I know.....
			try
			{
				factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				connection = factory.CreateConnection();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				try
				{
					factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname2, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
					connection = factory.CreateConnection();
				}
				catch (Exception ex2)
				{
					Logging.LogException(ex2);

					try
					{
						factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname3, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
						connection = factory.CreateConnection();
					}
					catch (Exception ex3)
					{
						Logging.LogException(ex3);
						throw;
					}
				}
			}

			return connection;
		}

		private bool SendMessage(string queueName, string message)
		{
			//if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			//{
			try
			{
				// TODO: Maybe? https://github.com/EasyNetQ/EasyNetQ -SJ
				//var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				using (var connection = CreateConnection())
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
