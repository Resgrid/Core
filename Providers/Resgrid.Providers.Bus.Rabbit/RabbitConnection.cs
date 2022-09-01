using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Framework;
using System;

namespace Resgrid.Providers.Bus.Rabbit
{
	internal class RabbitConnection
	{
		private static ConnectionFactory _factory;

		public static bool VerifyAndCreateClients()
		{
			IConnection connection = null;
			bool success = false;
			
			// I know....I know.....
			try
			{
				_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				connection = _factory.CreateConnection();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				if (!String.IsNullOrWhiteSpace(ServiceBusConfig.RabbitHostname2))
				{
					try
					{
						_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname2, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
						connection = _factory.CreateConnection();
					}
					catch (Exception ex2)
					{
						Logging.LogException(ex2);

						if (!String.IsNullOrWhiteSpace(ServiceBusConfig.RabbitHostname3))
						{
							try
							{
								_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname3, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
								connection = _factory.CreateConnection();
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

			if (connection != null)
			{
				success = true;
				var channel = connection.CreateModel();

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


			}

			return success;
		}

		public static IConnection CreateConnection()
		{
			if (_factory == null)
				VerifyAndCreateClients();

			return _factory.CreateConnection();
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
