using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using System;
using System.Text;

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
			return SendMessage(Topics.EventingTopic, new
			{
				Id = Guid.NewGuid(),
				Type = EventingTypes.PersonnelStatusUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.Status.DepartmentId,
				ItemId = message.Status.ActionLogId
			}.SerializeJson());
		}

		public bool PersonnelStaffingChanged(UserStaffingEvent message)
		{
			return SendMessage(Topics.EventingTopic, new
			{
				Id = Guid.NewGuid(),
				Type = EventingTypes.PersonnelStaffingUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Staffing.UserStateId
			}.SerializeJson());
		}

		public bool UnitStatusChanged(UnitStatusEvent message)
		{
			return SendMessage(Topics.EventingTopic, new
			{
				Id = Guid.NewGuid(),
				Type = EventingTypes.UnitStatusUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Status.UnitStateId
			}.SerializeJson());
		}

		public bool CallAdded(CallAddedEvent message)
		{
			return SendMessage(Topics.EventingTopic, new
			{
				Id = Guid.NewGuid(),
				Type = EventingTypes.CallsUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Call.CallId
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
					var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
					using (var connection = factory.CreateConnection())
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
