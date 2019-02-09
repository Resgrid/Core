using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using System;
using System.Text;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitInboundQueueProvider
	{
		private ConnectionFactory _factory;
		private IConnection _connection;
		private IModel _channel;

		public Action<CallQueueItem> CallQueueReceived;
		public Action<MessageQueueItem> MessageQueueReceived;
		public Action<DistributionListQueueItem> DistributionListQueueReceived;
		public Action<NotificationItem> NotificationQueueReceived;
		public Action<ShiftQueueItem> ShiftNotificationQueueReceived;
		public Action<CqrsEvent> CqrsEventQueueReceived;

		public RabbitInboundQueueProvider()
		{
			RabbitOutboundQueueProvider.VerifyAndCreateClients();
		}

		public void Start()
		{
			VerifyAndCreateClients();
			StartMonitoring();
		}

		private void VerifyAndCreateClients()
		{
			_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
			_connection = _factory.CreateConnection();
			_channel = _connection.CreateModel();
		}

		private void StartMonitoring()
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				var callQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				callQueueReceivedConsumer.Received += (model, ea) =>
				{
					if (ea != null && ea.Body != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body);

						try
						{
							var cqi = ObjectSerialization.Deserialize<CallQueueItem>(message);

							if (cqi != null)
							{
								CallQueueReceived?.Invoke(cqi);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}

					_channel.BasicAck(ea.DeliveryTag, false);
				};

				var messageQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				messageQueueReceivedConsumer.Received += (model, ea) =>
				{
					if (ea != null && ea.Body != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body);

						try
						{
							var mqi = ObjectSerialization.Deserialize<MessageQueueItem>(message);

							if (mqi != null)
							{
								MessageQueueReceived?.Invoke(mqi);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}

					_channel.BasicAck(ea.DeliveryTag, false);
				};

				var distributionListQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				distributionListQueueReceivedConsumer.Received += (model, ea) =>
				{
					if (ea != null && ea.Body != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body);

						try
						{
							var dlqi = ObjectSerialization.Deserialize<DistributionListQueueItem>(message);

							if (dlqi != null)
							{
								DistributionListQueueReceived?.Invoke(dlqi);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}

					_channel.BasicAck(ea.DeliveryTag, false);
				};

				var notificationQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				notificationQueueReceivedConsumer.Received += (model, ea) =>
				{
					if (ea != null && ea.Body != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body);

						try
						{
							var ni = ObjectSerialization.Deserialize<NotificationItem>(message);

							if (ni != null)
							{
								NotificationQueueReceived?.Invoke(ni);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}

					_channel.BasicAck(ea.DeliveryTag, false);
				};

				var shiftNotificationQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				shiftNotificationQueueReceivedConsumer.Received += (model, ea) =>
				{
					if (ea != null && ea.Body != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body);

						try
						{
							var sqi = ObjectSerialization.Deserialize<ShiftQueueItem>(message);

							if (sqi != null)
							{
								ShiftNotificationQueueReceived?.Invoke(sqi);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}

					_channel.BasicAck(ea.DeliveryTag, false);
				};

				var cqrsEventQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				cqrsEventQueueReceivedConsumer.Received += (model, ea) =>
				{
					if (ea != null && ea.Body != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body);

						try
						{
							var cqrs = ObjectSerialization.Deserialize<CqrsEvent>(message);

							if (cqrs != null)
							{
								CqrsEventQueueReceived?.Invoke(cqrs);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}

					_channel.BasicAck(ea.DeliveryTag, false);
				};


				String callQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: ServiceBusConfig.CallBroadcastQueueName,
					autoAck: false,
					consumer: callQueueReceivedConsumer);

				String messageQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: ServiceBusConfig.MessageBroadcastQueueName,
					autoAck: false,
					consumer: messageQueueReceivedConsumer);

				String distributionListQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: ServiceBusConfig.EmailBroadcastQueueName,
					autoAck: false,
					consumer: distributionListQueueReceivedConsumer);

				String notificationQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: ServiceBusConfig.NotificaitonBroadcastQueueName,
					autoAck: false,
					consumer: notificationQueueReceivedConsumer);

				String shiftNotificationQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: ServiceBusConfig.ShiftNotificationsQueueName,
					autoAck: false,
					consumer: shiftNotificationQueueReceivedConsumer);

				String cqrsEventQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: ServiceBusConfig.SystemQueueName,
					autoAck: false,
					consumer: cqrsEventQueueReceivedConsumer);
			}
		}
	}
}
