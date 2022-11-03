using Newtonsoft.Json;
using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using System;
using System.Text;
using System.Threading;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitTopicProvider
	{
		public RabbitTopicProvider()
		{
			VerifyAndCreateClients();
		}

		public bool PersonnelStatusChanged(UserStatusEvent message)
		{
			return SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.PersonnelStatusUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.Status.DepartmentId,
				ItemId = message.Status.ActionLogId.ToString()
			}.SerializeJson());
		}

		public bool PersonnelStaffingChanged(UserStaffingEvent message)
		{
			return SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.PersonnelStaffingUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Staffing.UserStateId.ToString()
			}.SerializeJson());
		}

		public bool UnitStatusChanged(UnitStatusEvent message)
		{
			return SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.UnitStatusUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Status.UnitStateId.ToString()
			}.SerializeJson());
		}

		public bool CallAdded(CallAddedEvent message)
		{
			return SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.CallAdded,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Call.CallId.ToString()
			}.SerializeJson());
		}

		public bool CallUpdated(CallUpdatedEvent message)
		{
			return SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.CallsUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Call.CallId.ToString()
			}.SerializeJson());
		}

		public bool CallClosed(CallClosedEvent message)
		{
			return SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.CallClosed,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Call.CallId.ToString()
			}.SerializeJson());
		}

		public bool PersonnelLocationUnidatedChanged(PersonnelLocationUpdatedEvent message)
		{
			return SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.PersonnelLocationUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.RecordId,
				Payload = JsonConvert.SerializeObject(message)
			}.SerializeJson());
		}

		public bool UnitLocationUpdatedChanged(UnitLocationUpdatedEvent message)
		{
			return SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.UnitLocationUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.RecordId,
				Payload = JsonConvert.SerializeObject(message)
			}.SerializeJson());
		}

		private static void VerifyAndCreateClients()
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				try
				{
					var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
					using (var connection = factory.CreateConnection())
					{
						using (var channel = connection.CreateModel())
						{
							channel.ExchangeDeclare(SetQueueNameForEnv(Topics.EventingTopic), "fanout");
						}
					}
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
				}
			}
		}

		private bool SendMessage(string topicName, string message)
		{
			try
			{
				if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				{
					//var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
					using (var connection = CreateConnection())
					{
						using (var channel = connection.CreateModel())
						{
							channel.BasicPublish(exchange: SetQueueNameForEnv(topicName),
										 routingKey: "",
										 basicProperties: null,
										 body: Encoding.ASCII.GetBytes(message));
						}
					}

					return true;
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return false;
		}

		private IConnection CreateConnection()
		{
			ConnectionFactory factory;
			IConnection connection = null;

			// I know....I know.....
			try
			{
				factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				connection = factory.CreateConnection();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				if (!String.IsNullOrWhiteSpace(ServiceBusConfig.RabbitHostname2))
				{
					try
					{
						factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname2, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
						connection = factory.CreateConnection();
					}
					catch (Exception ex2)
					{
						Logging.LogException(ex2);

						if (!String.IsNullOrWhiteSpace(ServiceBusConfig.RabbitHostname3))
						{
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
