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


		public static async Task<bool> VerifyAndCreateClients(string clientName)
		{
			if (_connection != null && !_connection.IsOpen)
			{
				_connection.Dispose();
				_connection = null;
				_factory = null;
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
						finally
						{
							_semaphore.Release();
						}
					}
				}
				finally
				{
					_semaphore.Release();
				}


				if (_connection != null)
				{
					try
					{
						var channel = await _connection.CreateChannelAsync();

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

		public static async Task<IConnection> CreateConnection(string clientName)
		{
			if (_connection == null)
				await VerifyAndCreateClients(clientName);

			if (!_connection.IsOpen)
			{
				_connection.Dispose();
				_connection = null;
				_factory = null;

				await VerifyAndCreateClients(clientName);
			}

			return _connection;
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
