using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Providers.Bus
{
	public class PaymentProvider : IPaymentProvider
	{
		private readonly RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;
		private readonly QueueClient _systemClient = null;

		public PaymentProvider()
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Azure)
			{
				while (_systemClient == null)
				{
					try
					{
						_systemClient = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.PaymentQueueName);
					}
					catch (TimeoutException) { }
				}
			}
			else
			{
				_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
			}
		}

		public bool EnqueuePaymentEvent(CqrsEvent cqrsEvent)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueuePaymentEvent(cqrsEvent);
				return true;
			}

			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);
			BrokeredMessage message = new BrokeredMessage(serializedObject);
			message.MessageId = string.Format("{0}", cqrsEvent.EventId);

			return SendMessage(_systemClient, message);
		}

		public async Task<bool> EnqueuePaymentEventAsync(CqrsEvent cqrsEvent)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueuePaymentEvent(cqrsEvent);
				return true;
			}

			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);
			BrokeredMessage message = new BrokeredMessage(serializedObject);
			message.MessageId = string.Format("{0}", cqrsEvent.EventId);

			return await SendMessageAsync(_systemClient, message);
		}

		private bool SendMessage(QueueClient client, BrokeredMessage message)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Azure)
			{
				int retry = 0;
				bool sent = false;

				while (!sent)
				{
					try
					{
						client.Send(message);
						sent = true;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, message.ToString());

						if (retry >= 5)
							return false;

						Thread.Sleep(250);
						retry++;
					}
				}

				return sent;
			}

			return false;
		}

		private async Task<bool> SendMessageAsync(QueueClient client, BrokeredMessage message)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Azure)
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

						Thread.Sleep(250);
						retry++;
					}
				}

				return sent;
			}

			return false;
		}
	}
}
