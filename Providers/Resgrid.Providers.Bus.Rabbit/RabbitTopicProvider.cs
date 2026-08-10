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
		private static volatile bool _exchangeDeclared;

		static RabbitTopicProvider()
		{
			RabbitConnection.ConnectionReset += () => _exchangeDeclared = false;
		}

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

		public async Task<bool> IncidentCommandUpdated(IncidentCommandUpdatedEvent message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.IncidentCommandUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.CallId.ToString()
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

		public async Task<bool> ChatEventOccurred(ChatEventRaised message)
		{
			return await SendMessage(Topics.EventingTopic, new EventingMessage
			{
				Id = Guid.NewGuid(),
				Type = (int)EventingTypes.ChatEvent,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.ChatChannelId,
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
			}.SerializeJson(), true);
		}

		private static async Task<bool> VerifyAndCreateClients(string clientName)
		{
			try
			{
				// Validate/create connection first so a reconnect clears _exchangeDeclared
				var connection = await RabbitConnection.CreateConnection(clientName);

				if (_exchangeDeclared)
					return true;

				if (connection != null)
				{
					// await using to close the channel via DisposeAsync and release its channel number (see SendMessage).
					await using (var channel = await connection.CreateChannelAsync())
					{
						await channel.ExchangeDeclareAsync(RabbitConnection.SetQueueNameForEnv(Topics.EventingTopic), "fanout");
					}

					_exchangeDeclared = true;
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}

			return true;
		}

		private async Task<bool> SendMessage(string topicName, string message, bool requirePublisherConfirmation = false)
		{
			if (!await VerifyAndCreateClients(_clientName))
				return false;

			try
			{
				return await PublishAsync(topicName, message, requirePublisherConfirmation);
			}
			catch (RabbitMQ.Client.Exceptions.ChannelAllocationException ex)
			{
				// The shared connection still reports IsOpen when its channel numbers are exhausted,
				// so the normal reconnect guards never fire and every send fails until the process
				// restarts. Hard-reset the connection and retry the publish once on a fresh one.
				Framework.Logging.LogException(ex);

				try
				{
					await RabbitConnection.ForceResetAsync();

					if (!await VerifyAndCreateClients(_clientName))
						return false;

					return await PublishAsync(topicName, message, requirePublisherConfirmation);
				}
				catch (Exception retryEx)
				{
					Framework.Logging.LogException(retryEx);
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return false;
		}

		private async Task<bool> PublishAsync(string topicName, string message, bool requirePublisherConfirmation)
		{
			var connection = await RabbitConnection.CreateConnection(_clientName);
			if (connection == null)
				return false;

			var channelOptions = requirePublisherConfirmation
				? new CreateChannelOptions(true, true)
				: null;
			await using (var channel = channelOptions == null
				? await connection.CreateChannelAsync()
				: await connection.CreateChannelAsync(channelOptions))
			{
				using var publishTimeout = requirePublisherConfirmation
					? new System.Threading.CancellationTokenSource(
						TimeSpan.FromSeconds(Math.Max(1, UnitTrackingConfig.QueuePublishTimeoutSeconds)))
					: null;
				await channel.BasicPublishAsync(
					exchange: RabbitConnection.SetQueueNameForEnv(topicName),
					routingKey: "",
					mandatory: false,
					basicProperties: new BasicProperties
					{
						DeliveryMode = requirePublisherConfirmation
							? DeliveryModes.Persistent
							: DeliveryModes.Transient
					},
					// UTF8: chat payloads carry emoji/unicode; superset of the ASCII previously used and
					// the inbound consumer already decodes UTF8.
					body: Encoding.UTF8.GetBytes(message),
					cancellationToken: publishTimeout?.Token ?? default);
			}

			return true;
		}
	}
}
