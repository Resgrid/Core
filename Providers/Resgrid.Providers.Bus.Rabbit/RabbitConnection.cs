using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus.Rabbit
{
	internal class RabbitConnection
	{
		private static IConnection _connection { get; set; }
		private static ConnectionFactory _factory { get; set; }
		private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

		/// <summary>
		/// Raised when the connection is reset so callers can clear cached declaration state.
		/// </summary>
		public static event Action ConnectionReset;

		public static async Task<bool> VerifyAndCreateClients(string clientName)
		{
			if (_connection != null && !_connection.IsOpen)
			{
				await _connection.DisposeAsync();
				_connection = null;
				_factory = null;
				RaiseConnectionReset();
			}

			if (_connection == null)
			{
				await _semaphore.WaitAsync();

				try
				{
					_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
					_connection = await _factory.CreateConnectionAsync(clientName);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);

					if (!String.IsNullOrWhiteSpace(ServiceBusConfig.RabbitHostname2))
					{
						try
						{
							_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname2, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
							_connection = await _factory.CreateConnectionAsync(clientName);
						}
						catch (Exception ex2)
						{
							Logging.LogException(ex2);

							if (!String.IsNullOrWhiteSpace(ServiceBusConfig.RabbitHostname3))
							{
								try
								{
									_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname3, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
									_connection = await _factory.CreateConnectionAsync(clientName);
								}
								catch (Exception ex3)
								{
									Logging.LogException(ex3);
									throw;
								}
							}
						}
					}
				}
				finally
				{
					// Single release: the semaphore is acquired once above, so release it exactly once here.
					// The outer finally covers every path (primary success, host2/host3 fallback, and rethrow);
					// a second release in the fallback branch previously threw SemaphoreFullException.
					_semaphore.Release();
				}


				if (_connection != null)
				{
					try
					{
						// await using to close the channel via DisposeAsync and release its channel number; a
						// synchronous Dispose() on a v7 IChannel skips the async close handshake and leaks channels.
						await using var channel = await _connection.CreateChannelAsync();

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.SystemQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.CallBroadcastQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.MessageBroadcastQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.EmailBroadcastQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.NotificaitonBroadcastQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.ShiftNotificationsQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.PaymentQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.AuditQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

					await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.UnitLoactionQueueName),
								 durable: false,
								 exclusive: false,
								 autoDelete: false,
								 arguments: null);

					await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.PersonnelLoactionQueueName),
									 durable: false,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(Topics.EventingTopic),
									 durable: false,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.SecurityRefreshQueueName),
									 durable: false,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.WorkflowQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

					await channel.QueueDeclareAsync(queue: SetQueueNameForEnv(ServiceBusConfig.ChatbotProcessingQueueName),
								 durable: true,
								 exclusive: false,
								 autoDelete: false,
								 arguments: null);

					await DeclareUnitLocationV2QueuesAsync(channel);

					return true;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
						return false;
					}
				}
			}

			return false;
		}

		private static async Task DeclareUnitLocationV2QueuesAsync(IChannel channel)
		{
			try
			{
				var unitLocationQueueV2Name = SetQueueNameForEnv(ServiceBusConfig.UnitLocationQueueV2Name);
				var unitLocationRetryQueueV2Name = SetQueueNameForEnv(ServiceBusConfig.UnitLocationRetryQueueV2Name);
				var unitLocationDeadQueueV2Name = SetQueueNameForEnv(ServiceBusConfig.UnitLocationDeadQueueV2Name);
				var queueExchange = ServiceBusConfig.RabbbitExchange ?? string.Empty;
				var queueTtlMilliseconds = (long)Math.Max(1, UnitTrackingConfig.QueueMessageTtlSeconds) * 1000L;
				var retryTtlMilliseconds = (long)Math.Max(1, UnitTrackingConfig.UnitLocationRetryDelaySeconds) * 1000L;

				await channel.QueueDeclareAsync(
					queue: unitLocationDeadQueueV2Name,
					durable: true,
					exclusive: false,
					autoDelete: false,
					arguments: null);

				await channel.QueueDeclareAsync(
					queue: unitLocationRetryQueueV2Name,
					durable: true,
					exclusive: false,
					autoDelete: false,
					arguments: new System.Collections.Generic.Dictionary<string, object>
					{
						["x-message-ttl"] = retryTtlMilliseconds,
						["x-dead-letter-exchange"] = queueExchange,
						["x-dead-letter-routing-key"] = unitLocationQueueV2Name
					});

				await channel.QueueDeclareAsync(
					queue: unitLocationQueueV2Name,
					durable: true,
					exclusive: false,
					autoDelete: false,
					arguments: new System.Collections.Generic.Dictionary<string, object>
					{
						["x-message-ttl"] = queueTtlMilliseconds,
						["x-dead-letter-exchange"] = queueExchange,
						["x-dead-letter-routing-key"] = unitLocationDeadQueueV2Name
					});

				if (!string.IsNullOrWhiteSpace(queueExchange))
				{
					await channel.QueueBindAsync(unitLocationQueueV2Name, queueExchange, unitLocationQueueV2Name);
					await channel.QueueBindAsync(unitLocationRetryQueueV2Name, queueExchange, unitLocationRetryQueueV2Name);
					await channel.QueueBindAsync(unitLocationDeadQueueV2Name, queueExchange, unitLocationDeadQueueV2Name);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public static async Task<IConnection> CreateConnection(string clientName)
		{
			if (_connection == null)
				await VerifyAndCreateClients(clientName);

			// _connection can still be null here if VerifyAndCreateClients failed to connect (e.g. primary
			// host down and no fallback host configured), so guard before accessing IsOpen to avoid an NRE.
			if (_connection == null || !_connection.IsOpen)
			{
				if (_connection != null)
					await _connection.DisposeAsync();

				_connection = null;
				_factory = null;
				RaiseConnectionReset();

				await VerifyAndCreateClients(clientName);
			}

			return _connection;
		}

		private static void RaiseConnectionReset()
		{
			var handler = ConnectionReset;
			if (handler == null)
				return;

			foreach (var subscriber in handler.GetInvocationList())
			{
				try
				{
					((Action)subscriber).Invoke();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}

		public static string SetQueueNameForEnv(string cacheKey)
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
