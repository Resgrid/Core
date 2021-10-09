using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using System;
using System.Collections.Generic;
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
		public Func<AuditEvent, Task> AuditEventQueueReceived;

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
			// I know....I know.....
			try
			{
				_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				_connection = _factory.CreateConnection();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				try
				{
					_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname2, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
					_connection = _factory.CreateConnection();
				}
				catch (Exception ex2)
				{
					Logging.LogException(ex2);

					try
					{
						_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname3, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
						_connection = _factory.CreateConnection();
					}
					catch (Exception ex3)
					{
						Logging.LogException(ex3);
						throw;
					}
				}
			}

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
						CallQueueItem cqi = null;
						try
						{
							var body = ea.Body;
							var message = Encoding.UTF8.GetString(body.ToArray());
							cqi = ObjectSerialization.Deserialize<CallQueueItem>(message);
						}
						catch (Exception ex)
						{
							_channel.BasicNack(ea.DeliveryTag, false, false);
							Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
						}

						try
						{
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
							if (RetryQueueItem(ea, ex))
								_channel.BasicNack(ea.DeliveryTag, false, false);
							else
								_channel.BasicNack(ea.DeliveryTag, false, true);
						}
					}
				};

				var messageQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				messageQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						MessageQueueItem mqi = null;
						try
						{
							var body = ea.Body;
							var message = Encoding.UTF8.GetString(body.ToArray());
							mqi = ObjectSerialization.Deserialize<MessageQueueItem>(message);
						}
						catch (Exception ex)
						{
							_channel.BasicNack(ea.DeliveryTag, false, false);
							Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
						}

						try
						{
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
							if (RetryQueueItem(ea, ex))
								_channel.BasicAck(ea.DeliveryTag, false);
							else
								_channel.BasicNack(ea.DeliveryTag, false, true);
						}
					}
				};

				var distributionListQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				distributionListQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						DistributionListQueueItem dlqi = null;
						try
						{
							var body = ea.Body;
							var message = Encoding.UTF8.GetString(body.ToArray());
							dlqi = ObjectSerialization.Deserialize<DistributionListQueueItem>(message);
						}
						catch (Exception ex)
						{
							_channel.BasicNack(ea.DeliveryTag, false, false);
							Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
						}

						try
						{
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
							if (RetryQueueItem(ea, ex))
								_channel.BasicAck(ea.DeliveryTag, false);
							else
								_channel.BasicNack(ea.DeliveryTag, false, true);
						}
					}
				};

				var notificationQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				notificationQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						NotificationItem ni = null;
						try
						{
							var body = ea.Body;
							var message = Encoding.UTF8.GetString(body.ToArray());
							ni = ObjectSerialization.Deserialize<NotificationItem>(message);
						}
						catch (Exception ex)
						{
							_channel.BasicNack(ea.DeliveryTag, false, false);
							Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
						}

						try
						{
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
							if (RetryQueueItem(ea, ex))
								_channel.BasicAck(ea.DeliveryTag, false);
							else
								_channel.BasicNack(ea.DeliveryTag, false, true);
						}
					}
				};

				var shiftNotificationQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				shiftNotificationQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						ShiftQueueItem sqi = null;
						try
						{
							var body = ea.Body;
							var message = Encoding.UTF8.GetString(body.ToArray());
							sqi = ObjectSerialization.Deserialize<ShiftQueueItem>(message);
						}
						catch (Exception ex)
						{
							_channel.BasicNack(ea.DeliveryTag, false, false);
							Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
						}

						try
						{

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
							if (RetryQueueItem(ea, ex))
								_channel.BasicAck(ea.DeliveryTag, false);
							else
								_channel.BasicNack(ea.DeliveryTag, false, true);
						}
					}
				};

				var cqrsEventQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				cqrsEventQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						CqrsEvent cqrs = null;
						try
						{
							var body = ea.Body;
							var message = Encoding.UTF8.GetString(body.ToArray());
							cqrs = ObjectSerialization.Deserialize<CqrsEvent>(message);
						}
						catch (Exception ex)
						{
							_channel.BasicNack(ea.DeliveryTag, false, false);
							Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
						}

						try
						{
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
							if (RetryQueueItem(ea, ex))
								_channel.BasicAck(ea.DeliveryTag, false);
							else
								_channel.BasicNack(ea.DeliveryTag, false, true);
						}
					}
				};

				var paymentEventQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				paymentEventQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						CqrsEvent cqrs = null;
						try
						{
							var body = ea.Body;
							var message = Encoding.UTF8.GetString(body.ToArray());
							cqrs = ObjectSerialization.Deserialize<CqrsEvent>(message);
						}
						catch (Exception ex)
						{
							_channel.BasicNack(ea.DeliveryTag, false, false);
							Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
						}

						try
						{
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
							if (RetryQueueItem(ea, ex))
								_channel.BasicAck(ea.DeliveryTag, false);
							else
								_channel.BasicNack(ea.DeliveryTag, false, true);
						}
					}
				};

				var auditEventQueueReceivedConsumer = new EventingBasicConsumer(_channel);
				auditEventQueueReceivedConsumer.Received += async (model, ea) =>
				{
					if (ea != null && ea.Body.Length > 0)
					{
						AuditEvent audit = null;
						try
						{
							var body = ea.Body;
							var message = Encoding.UTF8.GetString(body.ToArray());
							audit = ObjectSerialization.Deserialize<AuditEvent>(message);
						}
						catch (Exception ex)
						{
							_channel.BasicNack(ea.DeliveryTag, false, false);
							Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
						}

						try
						{
							if (audit != null)
							{
								if (PaymentEventQueueReceived != null)
								{
									await AuditEventQueueReceived.Invoke(audit);
									_channel.BasicAck(ea.DeliveryTag, false);
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
							if (RetryQueueItem(ea, ex))
								_channel.BasicAck(ea.DeliveryTag, false);
							else
								_channel.BasicNack(ea.DeliveryTag, false, true);
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

				String auditEventQueueReceivedConsumerTag = _channel.BasicConsume(
					queue: SetQueueNameForEnv(ServiceBusConfig.AuditQueueName),
					autoAck: false,
					consumer: auditEventQueueReceivedConsumer);
			}
		}

		public bool IsConnected()
		{
			if (_channel == null)
				return false;

			return _channel.IsOpen;
		}

		private bool RetryQueueItem(BasicDeliverEventArgs ea, Exception mex)
		{
			try
			{
				int currentDeliveryCount = 0;

				if (ea.BasicProperties != null && ea.BasicProperties.Headers != null && ea.BasicProperties.Headers.Count > 0 &&
					ea.BasicProperties.Headers.ContainsKey("x-redelivered-count"))
					currentDeliveryCount = int.Parse(ea.BasicProperties.Headers["x-redelivered-count"].ToString());

				if (currentDeliveryCount >= 3)
					return true;

				//var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				using (var connection = CreateConnection())
				{
					using (var channel = connection.CreateModel())
					{
						IBasicProperties props = channel.CreateBasicProperties();
						props.DeliveryMode = 2;
						props.Expiration = "36000000";
						props.Headers = new Dictionary<string, object>();
						props.Headers.Add("x-redelivered-count", currentDeliveryCount++);
						props.Headers.Add("x-previous-error", mex.Message);

						// https://github.com/rabbitmq/rabbitmq-delayed-message-exchange
						props.Headers.Add("x-delay", 5000);

						channel.BasicPublish(exchange: ea.Exchange,
									 routingKey: ea.RoutingKey,
									 basicProperties: props,
									 body: ea.Body);
					}

					return true;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
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
