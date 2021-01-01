using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitInboundQueueProvider
	{
		private ConnectionFactory _factory;
		private IConnection _connection;
		private IModel _channel;

		public Func<CallQueueItem, Task> CallQueueReceived;
		public Func<MessageQueueItem, Task> MessageQueueReceived;
		public Func<DistributionListQueueItem, Task> DistributionListQueueReceived;
		public Func<NotificationItem, Task> NotificationQueueReceived;
		public Func<ShiftQueueItem, Task> ShiftNotificationQueueReceived;
		public Func<CqrsEvent, Task> CqrsEventQueueReceived;
		public Func<CqrsEvent, Task> PaymentEventQueueReceived;

		public RabbitInboundQueueProvider()
		{
			RabbitOutboundQueueProvider provider = new RabbitOutboundQueueProvider();
		}

		public async Task Start()
		{
			VerifyAndCreateClients();
			await StartMonitoring();
		}

		private void VerifyAndCreateClients()
		{
			_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
			_connection = _factory.CreateConnection();
			_channel = _connection.CreateModel();
		}

		private async Task StartMonitoring()
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				var callQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				callQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body.ToArray());

						try
						{
							var cqi = ObjectSerialization.Deserialize<CallQueueItem>(message);

							if (cqi != null)
							{
								if (CallQueueReceived != null)
								{
									await CallQueueReceived.Invoke(cqi);
									_channel.BasicAck(ea.DeliveryTag, false);
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				};

				var messageQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				messageQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body.ToArray());

						try
						{
							var mqi = ObjectSerialization.Deserialize<MessageQueueItem>(message);

							if (mqi != null)
							{
								if (MessageQueueReceived != null)
								{
									await MessageQueueReceived.Invoke(mqi);
									_channel.BasicAck(ea.DeliveryTag, false);
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				};

				var distributionListQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				distributionListQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body.ToArray());

						try
						{
							var dlqi = ObjectSerialization.Deserialize<DistributionListQueueItem>(message);

							if (dlqi != null)
							{
								if (DistributionListQueueReceived != null)
								{
									await DistributionListQueueReceived.Invoke(dlqi);
									_channel.BasicAck(ea.DeliveryTag, false);
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				};

				var notificationQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				notificationQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body.ToArray());

						try
						{
							var ni = ObjectSerialization.Deserialize<NotificationItem>(message);

							if (ni != null)
							{
								if (NotificationQueueReceived != null)
								{
									await NotificationQueueReceived.Invoke(ni);
									_channel.BasicAck(ea.DeliveryTag, false);
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				};

				var shiftNotificationQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				shiftNotificationQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body.ToArray());

						try
						{
							var sqi = ObjectSerialization.Deserialize<ShiftQueueItem>(message);

							if (sqi != null)
							{
								if (ShiftNotificationQueueReceived != null)
								{
									await ShiftNotificationQueueReceived.Invoke(sqi);
									_channel.BasicAck(ea.DeliveryTag, false);
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				};

				var cqrsEventQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				cqrsEventQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body.ToArray());

						try
						{
							var cqrs = ObjectSerialization.Deserialize<CqrsEvent>(message);

							if (cqrs != null)
							{
								if (CqrsEventQueueReceived != null)
								{
									await CqrsEventQueueReceived.Invoke(cqrs);
									_channel.BasicAck(ea.DeliveryTag, false);
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				};

				var paymentEventQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				paymentEventQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						var body = ea.Body;
						var message = Encoding.UTF8.GetString(body.ToArray());

						try
						{
							var cqrs = ObjectSerialization.Deserialize<CqrsEvent>(message);

							if (cqrs != null)
							{
								if (PaymentEventQueueReceived != null)
								{
									await PaymentEventQueueReceived.Invoke(cqrs);
									_channel.BasicAck(ea.DeliveryTag, false);
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				};


				String callQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: SetQueueNameForEnv(ServiceBusConfig.CallBroadcastQueueName),
					autoAck: false,
					consumer: callQueueReceivedConsumer);

				String messageQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: SetQueueNameForEnv(ServiceBusConfig.MessageBroadcastQueueName),
					autoAck: false,
					consumer: messageQueueReceivedConsumer);

				String distributionListQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: SetQueueNameForEnv(ServiceBusConfig.EmailBroadcastQueueName),
					autoAck: false,
					consumer: distributionListQueueReceivedConsumer);

				String notificationQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: SetQueueNameForEnv(ServiceBusConfig.NotificaitonBroadcastQueueName),
					autoAck: false,
					consumer: notificationQueueReceivedConsumer);

				String shiftNotificationQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: SetQueueNameForEnv(ServiceBusConfig.ShiftNotificationsQueueName),
					autoAck: false,
					consumer: shiftNotificationQueueReceivedConsumer);

				String cqrsEventQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: SetQueueNameForEnv(ServiceBusConfig.SystemQueueName),
					autoAck: false,
					consumer: cqrsEventQueueReceivedConsumer);

				String paymentEventQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: SetQueueNameForEnv(ServiceBusConfig.PaymentQueueName),
					autoAck: false,
					consumer: paymentEventQueueReceivedConsumer);
			}
		}

		public bool IsConnected()
		{
			if (_channel == null)
				return false;

			return _channel.IsOpen;
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
