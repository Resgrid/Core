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
		private string _clientName;
		private IModel _channel;
		public Func<CallQueueItem, Task> CallQueueReceived;
		public Func<MessageQueueItem, Task> MessageQueueReceived;
		public Func<DistributionListQueueItem, Task> DistributionListQueueReceived;
		public Func<NotificationItem, Task> NotificationQueueReceived;
		public Func<ShiftQueueItem, Task> ShiftNotificationQueueReceived;
		public Func<CqrsEvent, Task> CqrsEventQueueReceived;
		public Func<CqrsEvent, Task> PaymentEventQueueReceived;
		public Func<AuditEvent, Task> AuditEventQueueReceived;
		public Func<UnitLocationEvent, Task> UnitLocationEventQueueReceived;
		public Func<PersonnelLocationEvent, Task> PersonnelLocationEventQueueReceived;
		public Func<SecurityRefreshEvent, Task> SecurityRefreshEventQueueReceived;

		public RabbitInboundQueueProvider()
		{
			RabbitOutboundQueueProvider provider = new RabbitOutboundQueueProvider();
		}

		public async Task Start(string clientName)
		{
			_clientName = clientName;
			var connection = RabbitConnection.CreateConnection(clientName);

			if (connection != null)
			{
				_channel = connection.CreateModel();
				await StartMonitoring();
			}
		}

		private async Task StartMonitoring()
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				if (CallQueueReceived != null)
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

					String callQueueReceivedConsumerTag = _channel.BasicConsume(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.CallBroadcastQueueName),
							autoAck: false,
							consumer: callQueueReceivedConsumer);
				}

				if (MessageQueueReceived != null)
				{
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

					String messageQueueReceivedConsumerTag = _channel.BasicConsume(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.MessageBroadcastQueueName),
						autoAck: false,
						consumer: messageQueueReceivedConsumer);
				}

				if (DistributionListQueueReceived != null)
				{
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

					String distributionListQueueReceivedConsumerTag = _channel.BasicConsume(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.EmailBroadcastQueueName),
							autoAck: false,
							consumer: distributionListQueueReceivedConsumer);
				}

				if (NotificationQueueReceived != null)
				{
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

					String notificationQueueReceivedConsumerTag = _channel.BasicConsume(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.NotificaitonBroadcastQueueName),
							autoAck: false,
							consumer: notificationQueueReceivedConsumer);
				}

				if (ShiftNotificationQueueReceived != null)
				{
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

					String shiftNotificationQueueReceivedConsumerTag = _channel.BasicConsume(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.ShiftNotificationsQueueName),
						autoAck: false,
						consumer: shiftNotificationQueueReceivedConsumer);
				}

				if (CqrsEventQueueReceived != null)
				{
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

					String cqrsEventQueueReceivedConsumerTag = _channel.BasicConsume(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.SystemQueueName),
							autoAck: false,
							consumer: cqrsEventQueueReceivedConsumer);
				}

				if (PaymentEventQueueReceived != null)
				{
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

					String paymentEventQueueReceivedConsumerTag = _channel.BasicConsume(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.PaymentQueueName),
							autoAck: false,
							consumer: paymentEventQueueReceivedConsumer);
				}

				if (AuditEventQueueReceived != null)
				{
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
									if (AuditEventQueueReceived != null)
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

					String auditEventQueueReceivedConsumerTag = _channel.BasicConsume(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.AuditQueueName),
							autoAck: false,
							consumer: auditEventQueueReceivedConsumer);
				}

				if (UnitLocationEventQueueReceived != null)
				{
					var unitLocationQueueReceivedConsumer = new EventingBasicConsumer(_channel);
					unitLocationQueueReceivedConsumer.Received += async (model, ea) =>
					{
						if (ea != null && ea.Body.Length > 0)
						{
							UnitLocationEvent unitLocation = null;
							try
							{
								var body = ea.Body;
								var message = Encoding.UTF8.GetString(body.ToArray());
								unitLocation = ObjectSerialization.Deserialize<UnitLocationEvent>(message);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (unitLocation != null)
								{
									if (UnitLocationEventQueueReceived != null)
									{
										await UnitLocationEventQueueReceived.Invoke(unitLocation);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
							}
						}
					};

					String unitLocationEventQueueReceivedConsumerTag = _channel.BasicConsume(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.UnitLoactionQueueName),
							autoAck: true,
							consumer: unitLocationQueueReceivedConsumer);
				}

				if (PersonnelLocationEventQueueReceived != null)
				{
					var personnelLocationQueueReceivedConsumer = new EventingBasicConsumer(_channel);
					personnelLocationQueueReceivedConsumer.Received += async (model, ea) =>
					{
						if (ea != null && ea.Body.Length > 0)
						{
							PersonnelLocationEvent personnelLocation = null;
							try
							{
								var body = ea.Body;
								var message = Encoding.UTF8.GetString(body.ToArray());
								personnelLocation = ObjectSerialization.Deserialize<PersonnelLocationEvent>(message);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (personnelLocation != null)
								{
									if (PersonnelLocationEventQueueReceived != null)
									{
										await PersonnelLocationEventQueueReceived.Invoke(personnelLocation);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
							}
						}
					};

					String personnelLocationEventQueueReceivedConsumerTag = _channel.BasicConsume(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.PersonnelLoactionQueueName),
						autoAck: true,
						consumer: personnelLocationQueueReceivedConsumer);
				}

				if (SecurityRefreshEventQueueReceived != null)
				{
					var securityRefreshEventQueueReceivedConsumer = new EventingBasicConsumer(_channel);
					securityRefreshEventQueueReceivedConsumer.Received += async (model, ea) =>
					{
						if (ea != null && ea.Body.Length > 0)
						{
							SecurityRefreshEvent securityRefresh = null;
							try
							{
								var body = ea.Body;
								var message = Encoding.UTF8.GetString(body.ToArray());
								securityRefresh = ObjectSerialization.Deserialize<SecurityRefreshEvent>(message);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (securityRefresh != null)
								{
									if (SecurityRefreshEventQueueReceived != null)
									{
										await SecurityRefreshEventQueueReceived.Invoke(securityRefresh);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
							}
						}
					};

					String securityRefreshEventQueueReceivedConsumerTag = _channel.BasicConsume(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.SecurityRefreshQueueName),
						autoAck: true,
						consumer: securityRefreshEventQueueReceivedConsumer);
				}
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
				//using (var connection = RabbitConnection.CreateConnection())
				//{
				var connection = RabbitConnection.CreateConnection(_clientName);
				if (connection != null)
				{
					using (var channel = connection.CreateModel())
					{
						IBasicProperties props = channel.CreateBasicProperties();
						props.DeliveryMode = 2;

						// I *THINK* these headers are appearing in the body when trying to deserialze and it's blowing up protobuf. -SJ
						//props.Expiration = "36000000";
						//props.Headers = new Dictionary<string, object>();
						//props.Headers.Add("x-redelivered-count", currentDeliveryCount++);
						//props.Headers.Add("x-previous-error", mex.Message);

						// https://github.com/rabbitmq/rabbitmq-delayed-message-exchange
						//props.Headers.Add("x-delay", 5000);

						channel.BasicPublish(exchange: ea.Exchange,
									 routingKey: ea.RoutingKey,
									 basicProperties: props,
									 body: ea.Body);
					}

					return true;
				}

				return false;
				//}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}
	}
}
