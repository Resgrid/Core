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
			try
			{
				//var factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
				//using (var connection = factory.CreateConnection())
				var connection = RabbitConnection.CreateConnection();

				if (connection != null)
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

		private bool SendMessage(string topicName, string message)
		{
			try
			{
				//using (var connection = RabbitConnection.CreateConnection())
				var connection = RabbitConnection.CreateConnection();
				if (connection != null)
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
