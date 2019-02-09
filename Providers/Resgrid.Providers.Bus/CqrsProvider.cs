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
	public class CqrsProvider : ICqrsProvider
	{
		private readonly RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;
		private readonly QueueClient _systemClient = null;

		public CqrsProvider()
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Azure)
			{
				while (_systemClient == null)
				{
					try
					{
						_systemClient = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.SystemQueueName);
					}
					catch (TimeoutException) { }
				}
			}
			else
			{
				_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
			}
		}

		public void EnqueueCqrsEvent(CqrsEvent cqrsEvent)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueCqrsEvent(cqrsEvent);
				return;
			}

			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);
			BrokeredMessage message = new BrokeredMessage(serializedObject);
			message.MessageId = string.Format("{0}", cqrsEvent.EventId);

			SendMessage(_systemClient, message);
		}

		public async Task<bool> EnqueueCqrsEventAsync(CqrsEvent cqrsEvent)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueCqrsEvent(cqrsEvent);
				return true;
			}

			var serializedObject = ObjectSerialization.Serialize(cqrsEvent);
			BrokeredMessage message = new BrokeredMessage(serializedObject);
			message.MessageId = string.Format("{0}", cqrsEvent.EventId);

			return await SendMessageAsync(_systemClient, message);
		}

		private void SendMessage(QueueClient client, BrokeredMessage message)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Azure)
			{
#pragma warning disable 4014
				Task.Run(() =>
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
			}).ConfigureAwait(false);
#pragma warning restore 4014
			}
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
