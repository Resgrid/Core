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

		public bool EnqueueUnitLocationEvent(UnitLocationEvent unitLocationEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(unitLocationEvent);

			return SendMessage(ServiceBusConfig.UnitLoactionQueueName, serializedObject, false, "300000");
		}

		public bool EnqueuePersonnelLocationEvent(PersonnelLocationEvent personnelLocationEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(personnelLocationEvent);

			return SendMessage(ServiceBusConfig.PersonnelLoactionQueueName, serializedObject, false, "300000");
		}

		public bool EnqueueSecurityRefreshEvent(SecurityRefreshEvent securityRefreshEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(securityRefreshEvent);

			return SendMessage(ServiceBusConfig.SecurityRefreshQueueName, serializedObject, false, "300000");
		}

		public bool VerifyAndCreateClients()
		{
			return RabbitConnection.VerifyAndCreateClients();
		}

		private bool SendMessage(string queueName, string message, bool durable = true, string expiration = "36000000")
		{
			if (String.IsNullOrWhiteSpace(queueName))
				throw new ArgumentNullException("queueName");

			if (String.IsNullOrWhiteSpace(message))
				throw new ArgumentNullException("message");

			//if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			//{
			try
			{
				// TODO: Maybe? https://github.com/EasyNetQ/EasyNetQ -SJ
				//var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				//using (var connection = RabbitConnection.CreateConnection())
				//{
				var connection = RabbitConnection.CreateConnection();
				if (connection != null)
				{
					using (var channel = connection.CreateModel())
					{
						if (channel != null)
						{
							IBasicProperties props = channel.CreateBasicProperties();
							props.Headers = new Dictionary<string, object>();

							if (durable)
							{
								props.DeliveryMode = 2;
								props.Headers.Add("x-redelivered-count", 0);
							}
							else
								props.DeliveryMode = 1;

							props.Expiration = expiration;

							channel.BasicPublish(exchange: ServiceBusConfig.RabbbitExchange,
											 routingKey: RabbitConnection.SetQueueNameForEnv(queueName),
											 basicProperties: props,
											 body: Encoding.ASCII.GetBytes(message));

							return true;
						}
						else
						{
							Logging.LogError("RabbitOutboundQueueProvider->SendMessage channel is null.");
						}
					}
				}
				else
				{
					Logging.LogError("RabbitOutboundQueueProvider->SendMessage connection is null.");
				}

				return false;
				//}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
			//}

			//return false;
		}
	}
}
