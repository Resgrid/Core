using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Framework;
using System;

namespace Resgrid.Providers.Bus.Rabbit
{
	internal class RabbitConnection
	{
		private static IConnection _connection { get; set; }
		private static ConnectionFactory _factory { get; set; }
		private static object LOCK = new object();


		public static bool VerifyAndCreateClients()
		{
			if (_connection != null && !_connection.IsOpen)
			{
				_connection?.Dispose();

				_connection = null;
				_factory = null;
			}
			
			if (_connection == null)
			{
				lock (LOCK)
				{
					try
					{
						_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
						_connection = _factory.CreateConnection();
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						if (!String.IsNullOrWhiteSpace(ServiceBusConfig.RabbitHostname2))
						{
							try
							{
								_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname2, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
								_connection = _factory.CreateConnection();
							}
							catch (Exception ex2)
							{
								Logging.LogException(ex2);

								if (!String.IsNullOrWhiteSpace(ServiceBusConfig.RabbitHostname3))
								{
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
						}
					}
				}

				if (_connection != null)
				{
					try
					{
						var channel = _connection.CreateModel();

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.SystemQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.CallBroadcastQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.MessageBroadcastQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.EmailBroadcastQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.NotificaitonBroadcastQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.ShiftNotificationsQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.PaymentQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.AuditQueueName),
									 durable: true,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.UnitLoactionQueueName),
									 durable: false,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.PersonnelLoactionQueueName),
									 durable: false,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(Topics.EventingTopic),
									 durable: false,
									 exclusive: false,
									 autoDelete: false,
									 arguments: null);

						channel.QueueDeclare(queue: SetQueueNameForEnv(ServiceBusConfig.SecurityRefreshQueueName),
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

		public static IConnection CreateConnection()
		{
			if (_connection == null)
				VerifyAndCreateClients();

			if (!_connection.IsOpen)
			{
				_connection?.Dispose();

				_connection = null;
				_factory = null;

				VerifyAndCreateClients();
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
