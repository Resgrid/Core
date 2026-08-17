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
		private IChannel _channel;
		private IChannel _callChannel;
		private IChannel _unitLocationChannel;
		private IChannel _personnelLocationChannel;
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
		public Func<Resgrid.Model.Queue.WorkflowQueueItem, Task> WorkflowQueueReceived;
		public Func<ChatbotMessageQueueItem, Task> ChatbotMessageQueueReceived;
		public Func<CommunicationTestQueueItem, Task> CommunicationTestQueueReceived;

		public RabbitInboundQueueProvider()
		{
			RabbitOutboundQueueProvider provider = new RabbitOutboundQueueProvider();
		}

		public async Task Start(string clientName)
		{
			_clientName = clientName;

			// Dispose any channels from a previous Start (the watchdog re-calls Start after a
			// disconnect). Disposal also removes them from automatic-recovery tracking, so a late
			// connection recovery can't resurrect the old consumers alongside the new ones and
			// double-process dispatches.
			await DisposeChannelsAsync();

			var connection = await RabbitConnection.CreateConnection(clientName);

			if (connection != null)
			{
				try
				{
					_channel = await connection.CreateChannelAsync();

					if (CallQueueReceived != null)
					{
						// Call dispatch has its own channel so no unrelated queue callback can delay an
						// emergency notification, regardless of which backing store that callback uses.
						_callChannel = await connection.CreateChannelAsync();
						await _callChannel.BasicQosAsync(0, 1, false);
					}

					if (UnitLocationEventQueueReceived != null)
					{
						_unitLocationChannel = await connection.CreateChannelAsync();
						var prefetchCount = (ushort)Math.Min(
							ushort.MaxValue,
							Math.Max(1, UnitTrackingConfig.UnitLocationQueuePrefetchCount));
						await _unitLocationChannel.BasicQosAsync(0, prefetchCount, false);
					}

					if (PersonnelLocationEventQueueReceived != null)
					{
						// Personnel location storage must never serialize dispatch callbacks behind a slow
						// Mongo/DocumentDB operation. Rabbit dispatches callbacks sequentially per channel.
						_personnelLocationChannel = await connection.CreateChannelAsync();
						await _personnelLocationChannel.BasicQosAsync(0, 1, false);
					}

					await StartMonitoring();
				}
				catch (RabbitMQ.Client.Exceptions.ChannelAllocationException)
				{
					// The shared connection is open but out of channel numbers, so the watchdog's
					// retry would get the same exhausted connection back from CreateConnection forever
					// (the IsOpen guards can't see exhaustion). Dispose our channels first (clean close
					// releases their numbers while the connection is still alive), then force-reset the
					// connection so the retry builds a fresh one.
					await DisposeChannelsAsync();
					await RabbitConnection.ForceResetAsync();
					throw;
				}
				catch
				{
					// A partial startup must not linger: if StartMonitoring fails after the channels
					// were created, every channel is open but consumers are incomplete, so IsConnected()
					// would report healthy while nothing (or only some queues) is being consumed and the
					// host watchdog would never rebuild. Tear everything down — nulled fields make
					// IsConnected() false — and let the caller's retry path handle the failure.
					await DisposeChannelsAsync();
					throw;
				}
			}
		}

		private async Task StartMonitoring()
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				if (CallQueueReceived != null)
				{
					var callQueueReceivedConsumer = new AsyncEventingBasicConsumer(_callChannel);
					callQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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
								await _callChannel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (cqi != null)
								{
									if (CallQueueReceived != null)
									{
										await CallQueueReceived.Invoke(cqi);
										await _callChannel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _callChannel.BasicNackAsync(ea.DeliveryTag, false, false);
								else
									await _callChannel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String callQueueReceivedConsumerTag = await _callChannel.BasicConsumeAsync(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.CallBroadcastQueueName),
							autoAck: false,
							consumer: callQueueReceivedConsumer);
				}

				if (MessageQueueReceived != null)
				{
					var messageQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					messageQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (mqi != null)
								{
									if (MessageQueueReceived != null)
									{
										await MessageQueueReceived.Invoke(mqi);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicAckAsync(ea.DeliveryTag, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String messageQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.MessageBroadcastQueueName),
						autoAck: false,
						consumer: messageQueueReceivedConsumer);
				}

				if (DistributionListQueueReceived != null)
				{
					var distributionListQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					distributionListQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (dlqi != null)
								{
									if (DistributionListQueueReceived != null)
									{
										await DistributionListQueueReceived.Invoke(dlqi);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicAckAsync(ea.DeliveryTag, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String distributionListQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.EmailBroadcastQueueName),
							autoAck: false,
							consumer: distributionListQueueReceivedConsumer);
				}

				if (NotificationQueueReceived != null)
				{
					var notificationQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					notificationQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (ni != null)
								{
									if (NotificationQueueReceived != null)
									{
										await NotificationQueueReceived.Invoke(ni);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicAckAsync(ea.DeliveryTag, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String notificationQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.NotificaitonBroadcastQueueName),
							autoAck: false,
							consumer: notificationQueueReceivedConsumer);
				}

				if (ShiftNotificationQueueReceived != null)
				{
					var shiftNotificationQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					shiftNotificationQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{

								if (sqi != null)
								{
									if (ShiftNotificationQueueReceived != null)
									{
										await ShiftNotificationQueueReceived.Invoke(sqi);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicAckAsync(ea.DeliveryTag, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String shiftNotificationQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.ShiftNotificationsQueueName),
						autoAck: false,
						consumer: shiftNotificationQueueReceivedConsumer);
				}

				if (CqrsEventQueueReceived != null)
				{
					var cqrsEventQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					cqrsEventQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (cqrs != null)
								{
									if (CqrsEventQueueReceived != null)
									{
										await CqrsEventQueueReceived.Invoke(cqrs);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicAckAsync(ea.DeliveryTag, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String cqrsEventQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.SystemQueueName),
							autoAck: false,
							consumer: cqrsEventQueueReceivedConsumer);
				}

				if (PaymentEventQueueReceived != null)
				{
					var paymentEventQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					paymentEventQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (cqrs != null)
								{
									if (PaymentEventQueueReceived != null)
									{
										await PaymentEventQueueReceived.Invoke(cqrs);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicAckAsync(ea.DeliveryTag, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String paymentEventQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.PaymentQueueName),
							autoAck: false,
							consumer: paymentEventQueueReceivedConsumer);
				}

				if (AuditEventQueueReceived != null)
				{
					var auditEventQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					auditEventQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (audit != null)
								{
									if (AuditEventQueueReceived != null)
									{
										await AuditEventQueueReceived.Invoke(audit);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicAckAsync(ea.DeliveryTag, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String auditEventQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.AuditQueueName),
							autoAck: false,
							consumer: auditEventQueueReceivedConsumer);
				}

				if (UnitLocationEventQueueReceived != null)
				{
					await StartUnitLocationConsumer(ServiceBusConfig.UnitLoactionQueueName);
					await StartUnitLocationConsumer(ServiceBusConfig.UnitLocationQueueV2Name);
				}

				if (PersonnelLocationEventQueueReceived != null)
				{
					var personnelLocationQueueReceivedConsumer = new AsyncEventingBasicConsumer(_personnelLocationChannel);
					personnelLocationQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
					{
						if (ea == null)
							return;

						try
						{
							if (ea.Body.Length == 0)
								throw new InvalidOperationException("Personnel location queue message body is empty.");

							var message = Encoding.UTF8.GetString(ea.Body.ToArray());
							var personnelLocation = ObjectSerialization.Deserialize<PersonnelLocationEvent>(message)
								?? throw new InvalidOperationException("Personnel location queue message could not be deserialized.");

							await PersonnelLocationEventQueueReceived.Invoke(personnelLocation);
							await _personnelLocationChannel.BasicAckAsync(ea.DeliveryTag, false);
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
							await _personnelLocationChannel.BasicNackAsync(ea.DeliveryTag, false, false);
						}
					};

					String personnelLocationEventQueueReceivedConsumerTag = await _personnelLocationChannel.BasicConsumeAsync(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.PersonnelLoactionQueueName),
						autoAck: false,
						consumer: personnelLocationQueueReceivedConsumer);
				}

				if (SecurityRefreshEventQueueReceived != null)
				{
					var securityRefreshEventQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					securityRefreshEventQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
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

					String securityRefreshEventQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.SecurityRefreshQueueName),
						autoAck: true,
						consumer: securityRefreshEventQueueReceivedConsumer);
				}

				if (WorkflowQueueReceived != null)
				{
					var workflowQueueConsumer = new AsyncEventingBasicConsumer(_channel);
					workflowQueueConsumer.ReceivedAsync += async (model, ea) =>
					{
						if (ea != null && ea.Body.Length > 0)
						{
							Resgrid.Model.Queue.WorkflowQueueItem workflowItem = null;
							try
							{
								var body = ea.Body;
								var message = Encoding.UTF8.GetString(body.ToArray());
								workflowItem = ObjectSerialization.Deserialize<Resgrid.Model.Queue.WorkflowQueueItem>(message);
							}
							catch (Exception ex)
							{
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (workflowItem != null && WorkflowQueueReceived != null)
								{
									await WorkflowQueueReceived.Invoke(workflowItem);
									await _channel.BasicAckAsync(ea.DeliveryTag, false);
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					await _channel.BasicConsumeAsync(
						queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.WorkflowQueueName),
						autoAck: false,
						consumer: workflowQueueConsumer);
				}

				if (ChatbotMessageQueueReceived != null)
				{
					var chatbotMessageQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					chatbotMessageQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
					{
						if (ea != null && ea.Body.Length > 0)
						{
							ChatbotMessageQueueItem cmqi = null;
							try
							{
								var body = ea.Body;
								var message = Encoding.UTF8.GetString(body.ToArray());
								cmqi = ObjectSerialization.Deserialize<ChatbotMessageQueueItem>(message);
							}
							catch (Exception ex)
							{
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (cmqi != null)
								{
									if (ChatbotMessageQueueReceived != null)
									{
										await ChatbotMessageQueueReceived.Invoke(cmqi);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String chatbotMessageQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.ChatbotProcessingQueueName),
							autoAck: false,
							consumer: chatbotMessageQueueReceivedConsumer);
				}

				if (CommunicationTestQueueReceived != null)
				{
					var communicationTestQueueReceivedConsumer = new AsyncEventingBasicConsumer(_channel);
					communicationTestQueueReceivedConsumer.ReceivedAsync += async (model, ea) =>
					{
						if (ea != null && ea.Body.Length > 0)
						{
							CommunicationTestQueueItem ctqi = null;
							try
							{
								var body = ea.Body;
								var message = Encoding.UTF8.GetString(body.ToArray());
								ctqi = ObjectSerialization.Deserialize<CommunicationTestQueueItem>(message);
							}
							catch (Exception ex)
							{
								await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								Logging.LogException(ex, Encoding.UTF8.GetString(ea.Body.ToArray()));
							}

							try
							{
								if (ctqi != null)
								{
									if (CommunicationTestQueueReceived != null)
									{
										await CommunicationTestQueueReceived.Invoke(ctqi);
										await _channel.BasicAckAsync(ea.DeliveryTag, false);
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								if (await RetryQueueItem(ea, ex))
									await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
								else
									await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
							}
						}
					};

					String communicationTestQueueReceivedConsumerTag = await _channel.BasicConsumeAsync(
							queue: RabbitConnection.SetQueueNameForEnv(ServiceBusConfig.CommunicationTestQueueName),
							autoAck: false,
							consumer: communicationTestQueueReceivedConsumer);
				}
			}
		}

		private async Task DisposeChannelsAsync()
		{
			var channels = new[] { _channel, _callChannel, _unitLocationChannel, _personnelLocationChannel };
			_channel = null;
			_callChannel = null;
			_unitLocationChannel = null;
			_personnelLocationChannel = null;

			foreach (var channel in channels)
			{
				if (channel == null)
					continue;

				try
				{
					await channel.DisposeAsync();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}

		public bool IsConnected()
		{
			if (_channel == null ||
				(CallQueueReceived != null && _callChannel == null) ||
				(UnitLocationEventQueueReceived != null && _unitLocationChannel == null) ||
				(PersonnelLocationEventQueueReceived != null && _personnelLocationChannel == null))
				return false;

			return _channel.IsOpen &&
				(_callChannel?.IsOpen ?? true) &&
				(_unitLocationChannel?.IsOpen ?? true) &&
				(_personnelLocationChannel?.IsOpen ?? true);
		}

		private async Task StartUnitLocationConsumer(string queueName)
		{
			var consumer = new AsyncEventingBasicConsumer(_unitLocationChannel);
			consumer.ReceivedAsync += async (model, ea) =>
			{
				if (ea == null)
					return;

				try
				{
					if (ea.Body.Length == 0)
						throw new InvalidOperationException("Unit location queue message body is empty.");

					var message = Encoding.UTF8.GetString(ea.Body.ToArray());
					var unitLocation = ObjectSerialization.Deserialize<UnitLocationEvent>(message)
						?? throw new InvalidOperationException("Unit location queue message could not be deserialized.");

					await UnitLocationEventQueueReceived.Invoke(unitLocation);
					await _unitLocationChannel.BasicAckAsync(ea.DeliveryTag, false);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);

					if (await RetryOrDeadLetterUnitLocationAsync(ea, ex))
						await _unitLocationChannel.BasicAckAsync(ea.DeliveryTag, false);
					else
						await _unitLocationChannel.BasicNackAsync(ea.DeliveryTag, false, true);
				}
			};

			await _unitLocationChannel.BasicConsumeAsync(
				queue: RabbitConnection.SetQueueNameForEnv(queueName),
				autoAck: false,
				consumer: consumer);
		}

		private async Task<bool> RetryOrDeadLetterUnitLocationAsync(BasicDeliverEventArgs ea, Exception exception)
		{
			var retryCount = GetRetryCount(ea.BasicProperties?.Headers);
			var maxRetryAttempts = Math.Max(0, UnitTrackingConfig.UnitLocationMaxRetryAttempts);
			var targetQueue = retryCount >= maxRetryAttempts
				? ServiceBusConfig.UnitLocationDeadQueueV2Name
				: ServiceBusConfig.UnitLocationRetryQueueV2Name;

			try
			{
				var connection = await RabbitConnection.CreateConnection(_clientName);
				if (connection == null)
					return false;

				var channelOptions = new CreateChannelOptions(true, true);
				await using var channel = await connection.CreateChannelAsync(channelOptions);
				var properties = new BasicProperties
				{
					DeliveryMode = DeliveryModes.Persistent,
					MessageId = ea.BasicProperties?.MessageId,
					Headers = new Dictionary<string, object>
					{
						["x-unitlocation-retry-count"] = retryCount + 1,
						["x-previous-error"] = exception.GetType().Name
					}
				};

				using var publishTimeout = new System.Threading.CancellationTokenSource(
					TimeSpan.FromSeconds(Math.Max(1, UnitTrackingConfig.QueuePublishTimeoutSeconds)));

				await channel.BasicPublishAsync(
					exchange: ServiceBusConfig.RabbbitExchange,
					routingKey: RabbitConnection.SetQueueNameForEnv(targetQueue),
					mandatory: true,
					basicProperties: properties,
					body: ea.Body,
					cancellationToken: publishTimeout.Token);

				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		private static int GetRetryCount(IDictionary<string, object> headers)
		{
			if (headers == null || !headers.TryGetValue("x-unitlocation-retry-count", out var value))
				return 0;

			switch (value)
			{
				case byte byteValue:
					return byteValue;
				case sbyte signedByteValue:
					return Math.Max(0, (int)signedByteValue);
				case short shortValue:
					return Math.Max(0, (int)shortValue);
				case int intValue:
					return Math.Max(0, intValue);
				case long longValue:
					return (int)Math.Max(0, Math.Min(int.MaxValue, longValue));
				case byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed):
					return Math.Max(0, parsed);
				default:
					return int.TryParse(value.ToString(), out var converted) ? Math.Max(0, converted) : 0;
			}
		}

		private async Task<bool> RetryQueueItem(BasicDeliverEventArgs ea, Exception mex)
		{
			try
			{
				int currentDeliveryCount = 0;
				if (ea.BasicProperties?.Headers != null &&
					ea.BasicProperties.Headers.TryGetValue("x-redelivered-count", out var hdrVal))
				{
					switch (hdrVal)
					{
						case byte b:
							currentDeliveryCount = b; break;
						case sbyte sb:
							currentDeliveryCount = sb; break;
						case short s:
							currentDeliveryCount = s; break;
						case int i:
							currentDeliveryCount = i; break;
						case long l:
							currentDeliveryCount = (int)l; break;
						case byte[] bytes:
							if (int.TryParse(Encoding.UTF8.GetString(bytes), out var parsedBytes))
								currentDeliveryCount = parsedBytes;
							break;
						default:
							int.TryParse(hdrVal?.ToString(), out currentDeliveryCount);
							break;
					}
				}

				if (currentDeliveryCount >= 3)
					return true;

				var connection = await RabbitConnection.CreateConnection(_clientName);
				if (connection != null)
				{
					// await using to close the channel via DisposeAsync and release its channel number; the
					// synchronous Dispose() on a v7 IChannel skips the async close handshake and leaks channels.
					await using (var channel = await connection.CreateChannelAsync())
					{
						var props = new BasicProperties();
						props.DeliveryMode = DeliveryModes.Persistent;
						props.Expiration = "36000000";

						props.Headers = new Dictionary<string, object>();
						props.Headers.Add("x-redelivered-count", currentDeliveryCount + 1);
						props.Headers.Add("x-previous-error", string.IsNullOrWhiteSpace(mex.Message) ? "UnhandledError" : mex.Message.Substring(0, Math.Min(256, mex.Message.Length)));

						await channel.BasicPublishAsync(exchange: ea.Exchange,
									 routingKey: ea.RoutingKey,
									 mandatory: true,
									 basicProperties: props,
									 body: ea.Body);
					}

					return true;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return true; // Somethings really wrong, just don't retry.
			}

			return false;
		}
	}
}
