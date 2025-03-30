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
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitOutboundQueueProvider : IRabbitOutboundQueueProvider
	{
		private readonly string _clientName = "Resgrid-Outbound";

		public async Task<bool> EnqueueCall(CallQueueItem callQueue)
		{
			string serializedObject = ObjectSerialization.Serialize(callQueue);

			return await SendMessage(ServiceBusConfig.CallBroadcastQueueName, serializedObject);
		}

		public async Task<bool> EnqueueMessage(MessageQueueItem messageQueue)
		{
			string serializedObject = ObjectSerialization.Serialize(messageQueue);

			if (messageQueue != null && messageQueue.Message != null && messageQueue.MessageId == 0 && messageQueue.Message.MessageId != 0)
				messageQueue.MessageId = messageQueue.Message.MessageId;

			return await SendMessage(ServiceBusConfig.MessageBroadcastQueueName, serializedObject);
		}

		public async Task<bool> EnqueueDistributionList(DistributionListQueueItem distributionListQueue)
		{
			string serializedObject = ObjectSerialization.Serialize(distributionListQueue);

			return await SendMessage(ServiceBusConfig.EmailBroadcastQueueName, serializedObject);
		}

		public async Task<bool> EnqueueNotification(NotificationItem notificationQueue)
		{
			string serializedObject = String.Empty;

			serializedObject = ObjectSerialization.Serialize(notificationQueue);

			return await SendMessage(ServiceBusConfig.NotificaitonBroadcastQueueName, serializedObject);
		}

		public async Task<bool> EnqueueShiftNotification(ShiftQueueItem shiftQueueItem)
		{
			string serializedObject = String.Empty;

			serializedObject = ObjectSerialization.Serialize(shiftQueueItem);

			return await SendMessage(ServiceBusConfig.ShiftNotificationsQueueName, serializedObject);
		}

		public async Task<bool> EnqueueCqrsEvent(CqrsEvent cqrsEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);

			return await SendMessage(ServiceBusConfig.SystemQueueName, serializedObject);
		}

		public async Task<bool> EnqueuePaymentEvent(CqrsEvent cqrsEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);

			return await SendMessage(ServiceBusConfig.PaymentQueueName, serializedObject);
		}

		public async Task<bool> EnqueueAuditEvent(AuditEvent auditEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(auditEvent);

			return await SendMessage(ServiceBusConfig.AuditQueueName, serializedObject);
		}

		public async Task<bool> EnqueueUnitLocationEvent(UnitLocationEvent unitLocationEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(unitLocationEvent);

			return await SendMessage(ServiceBusConfig.UnitLoactionQueueName, serializedObject, false, "300000");
		}

		public async Task<bool> EnqueuePersonnelLocationEvent(PersonnelLocationEvent personnelLocationEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(personnelLocationEvent);

			return await SendMessage(ServiceBusConfig.PersonnelLoactionQueueName, serializedObject, false, "300000");
		}

		public async Task<bool> EnqueueSecurityRefreshEvent(SecurityRefreshEvent securityRefreshEvent)
		{
			var serializedObject = ObjectSerialization.Serialize(securityRefreshEvent);

			return await SendMessage(ServiceBusConfig.SecurityRefreshQueueName, serializedObject, false, "300000");
		}

		private async Task<bool> SendMessage(string queueName, string message, bool durable = true, string expiration = "36000000")
		{
			if (String.IsNullOrWhiteSpace(queueName))
				throw new ArgumentNullException("queueName");

			if (String.IsNullOrWhiteSpace(message))
				throw new ArgumentNullException("message");

			try
			{
				var connection = await RabbitConnection.CreateConnection(_clientName);
				if (connection != null)
				{
					using (var channel = await connection.CreateChannelAsync())
					{
						if (channel != null)
						{
							var props = new BasicProperties();
							props.Headers = new Dictionary<string, object>();

							if (durable)
							{
								props.DeliveryMode = DeliveryModes.Persistent;
								props.Headers.Add("x-redelivered-count", 0);
							}
							else
								props.DeliveryMode = DeliveryModes.Transient;

							props.Expiration = expiration;

							await channel.BasicPublishAsync(exchange: ServiceBusConfig.RabbbitExchange,
											 routingKey: RabbitConnection.SetQueueNameForEnv(queueName),
											 mandatory: true,
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
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> VerifyAndCreateClients()
		{
			return await RabbitConnection.VerifyAndCreateClients(_clientName);
		}
	}
}
