using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;
using Message = Microsoft.Azure.ServiceBus.Message;

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
						_systemClient = new QueueClient(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.PaymentQueueName);
					}
					catch (TimeoutException) { }
				}
			}
			else
			{
				_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
			}
		}

		public async Task<bool> EnqueuePaymentEventAsync(CqrsEvent cqrsEvent)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueuePaymentEvent(cqrsEvent);
				return true;
			}

			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);
			Message message = new Message(Encoding.UTF8.GetBytes(serializedObject));
			message.MessageId = string.Format("{0}", cqrsEvent.EventId);

			return await SendMessageAsync(_systemClient, message);
		}

		private async Task<bool> SendMessageAsync(QueueClient client, Message message)
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
