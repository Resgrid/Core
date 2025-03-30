using Newtonsoft.Json;
using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitTopicProvider
	{
		private readonly string _clientName = "Resgrid-Topic";

		public async Task<bool> PersonnelStatusChanged(UserStatusEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.PersonnelStatusUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.Status.DepartmentId,
				ItemId = message.Status.ActionLogId.ToString()
			}.SerializeJson());
		}

		public async Task<bool> PersonnelStaffingChanged(UserStaffingEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.PersonnelStaffingUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Staffing.UserStateId.ToString()
			}.SerializeJson());
		}

		public async Task<bool> UnitStatusChanged(UnitStatusEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.UnitStatusUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Status.UnitStateId.ToString()
			}.SerializeJson());
		}

		public async Task<bool> CallAdded(CallAddedEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.CallAdded,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Call.CallId.ToString()
			}.SerializeJson());
		}

		public async Task<bool> CallUpdated(CallUpdatedEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.CallsUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Call.CallId.ToString()
			}.SerializeJson());
		}

		public async Task<bool> CallClosed(CallClosedEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.CallClosed,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Call.CallId.ToString()
			}.SerializeJson());
		}

		public async Task<bool> PersonnelLocationUnidatedChanged(PersonnelLocationUpdatedEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.PersonnelLocationUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.RecordId,
				Payload = JsonConvert.SerializeObject(message)
			}.SerializeJson());
		}

		public async Task<bool> UnitLocationUpdatedChanged(UnitLocationUpdatedEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.UnitLocationUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.RecordId,
				Payload = JsonConvert.SerializeObject(message)
			}.SerializeJson());
		}

		private static async Task<bool> VerifyAndCreateClients(string clientName)
		{
			try
			{
				var connection = await RabbitConnection.CreateConnection(clientName);

				if (connection != null)
				{
					using (var channel = await connection.CreateChannelAsync())
					{
						await channel.ExchangeDeclareAsync(RabbitConnection.SetQueueNameForEnv(Topics.EventingTopic), "fanout");
					}
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}

			return true;
		}

		private async Task<bool> SendMessage(string topicName, string message)
		{
			await VerifyAndCreateClients(_clientName);

			try
			{
				var connection = await RabbitConnection.CreateConnection(_clientName);
				if (connection != null)
				{
					using (var channel = await connection.CreateChannelAsync())
					{
						await channel.BasicPublishAsync(exchange: RabbitConnection.SetQueueNameForEnv(topicName),
									 routingKey: "",
									 //mandatory: false, //TODO: Not sure here. -SJ
									 //basicProperties: null,
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
