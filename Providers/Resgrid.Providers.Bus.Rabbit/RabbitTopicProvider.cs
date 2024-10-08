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
		private readonly string _clientName = "Resgrid-Topic";

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

		private static void VerifyAndCreateClients(string clientName)
		{
			try
			{
				var connection = RabbitConnection.CreateConnection(clientName);

				if (connection != null)
				{
					using (var channel = connection.CreateModel())
					{
						channel.ExchangeDeclare(RabbitConnection.SetQueueNameForEnv(Topics.EventingTopic), "fanout");
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
			VerifyAndCreateClients(_clientName);

			try
			{
				var connection = RabbitConnection.CreateConnection(_clientName);
				if (connection != null)
				{
					using (var channel = connection.CreateModel())
					{
						channel.BasicPublish(exchange: RabbitConnection.SetQueueNameForEnv(topicName),
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
	}
}
