﻿using System.Text;
using Resgrid.Config;
using Resgrid.Model.Queue;
using Resgrid.Framework;
using System;
using RabbitMQ.Client;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Events;
using System.Threading;
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

		public async Task<bool> EnqueueChatbotMessage(ChatbotMessageQueueItem chatbotMessageQueue)
		{
			string serializedObject = ObjectSerialization.Serialize(chatbotMessageQueue);

			return await SendMessage(ServiceBusConfig.ChatbotProcessingQueueName, serializedObject);
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

			var expiration = ((long)Math.Max(1, UnitTrackingConfig.QueueMessageTtlSeconds) * 1000L).ToString();
			return await SendMessage(ServiceBusConfig.UnitLocationQueueV2Name, serializedObject, true, expiration, true);
		}

		public async Task<bool> EnqueueUnitLocationEvents(
			IReadOnlyCollection<UnitLocationEvent> unitLocationEvents,
			CancellationToken cancellationToken = default)
		{
			if (unitLocationEvents == null)
				throw new ArgumentNullException(nameof(unitLocationEvents));
			if (unitLocationEvents.Count == 0)
				return true;

			var serializedMessages = unitLocationEvents
				.Select(ObjectSerialization.Serialize)
				.ToList();
			var expiration =
				((long)Math.Max(1, UnitTrackingConfig.QueueMessageTtlSeconds) * 1000L).ToString();

			return await SendMessagesWithConfirmation(
				ServiceBusConfig.UnitLocationQueueV2Name,
				serializedMessages,
				expiration,
				cancellationToken);
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

		public async Task<bool> EnqueueWorkflowEvent(Resgrid.Model.Queue.WorkflowQueueItem item)
		{
			var serializedObject = ObjectSerialization.Serialize(item);

			return await SendMessage(ServiceBusConfig.WorkflowQueueName, serializedObject);
		}

		private async Task<bool> SendMessage(string queueName, string message, bool durable = true, string expiration = "36000000",
			bool requirePublisherConfirmation = false)
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
					// await using so the channel is closed via DisposeAsync(): the synchronous Dispose() on a
					// v7 IChannel skips the async Channel.Close/CloseOk handshake that releases the channel
					// number back to the SessionManager, leaking channels until the connection hits its limit
					// (ChannelAllocationException: "The connection cannot support any more channels").
					var channelOptions = requirePublisherConfirmation
						? new CreateChannelOptions(true, true)
						: null;

					await using (var channel = channelOptions == null
						? await connection.CreateChannelAsync()
						: await connection.CreateChannelAsync(channelOptions))
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

							using var publishTimeout = requirePublisherConfirmation
								? new System.Threading.CancellationTokenSource(
									TimeSpan.FromSeconds(Math.Max(1, UnitTrackingConfig.QueuePublishTimeoutSeconds)))
								: null;

							await channel.BasicPublishAsync(
								exchange: ServiceBusConfig.RabbbitExchange,
								routingKey: RabbitConnection.SetQueueNameForEnv(queueName),
								mandatory: true,
								basicProperties: props,
								body: Encoding.ASCII.GetBytes(message),
								cancellationToken: publishTimeout?.Token ?? default);

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

		private async Task<bool> SendMessagesWithConfirmation(
			string queueName,
			IReadOnlyCollection<string> messages,
			string expiration,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(queueName))
				throw new ArgumentNullException(nameof(queueName));
			if (messages == null || messages.Count == 0)
				return true;
			if (messages.Any(string.IsNullOrWhiteSpace))
				throw new ArgumentException("Queue messages cannot be empty.", nameof(messages));

			try
			{
				var connection = await RabbitConnection.CreateConnection(_clientName);
				if (connection == null)
				{
					Logging.LogError("RabbitOutboundQueueProvider->SendMessagesWithConfirmation connection is null.");
					return false;
				}

				await using var channel =
					await connection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
				var props = new BasicProperties
				{
					DeliveryMode = DeliveryModes.Persistent,
					Expiration = expiration,
					Headers = new Dictionary<string, object>
					{
						["x-redelivered-count"] = 0
					}
				};

				using var publishTimeout =
					CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				publishTimeout.CancelAfter(
					TimeSpan.FromSeconds(Math.Max(1, UnitTrackingConfig.QueuePublishTimeoutSeconds)));

				foreach (var message in messages)
				{
					await channel.BasicPublishAsync(
						exchange: ServiceBusConfig.RabbbitExchange,
						routingKey: RabbitConnection.SetQueueNameForEnv(queueName),
						mandatory: true,
						basicProperties: props,
						body: Encoding.ASCII.GetBytes(message),
						cancellationToken: publishTimeout.Token);
				}

				return true;
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
