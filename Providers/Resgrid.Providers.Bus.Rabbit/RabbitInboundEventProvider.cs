using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitInboundEventProvider : IRabbitInboundEventProvider
	{
		private IConnection _connection;
		private IChannel _channel;

		public Func<int, string, Task> ProcessPersonnelStatusChanged;
		public Func<int, string, Task> ProcessUnitStatusChanged;
		public Func<int, string, Task> ProcessCallStatusChanged;
		public Func<int, string, Task> ProcessCallAdded;
		public Func<int, string, Task> ProcessCallClosed;
		public Func<int, string, Task> ProcessPersonnelStaffingChanged;
		public Func<int, PersonnelLocationUpdatedEvent, Task> PersonnelLocationUpdated;
		public Func<int, UnitLocationUpdatedEvent, Task> UnitLocationUpdated;
		public Func<int, string, Task> ProcessIncidentCommandUpdated;
		public Func<int, string, Task> ProcessChatEvent;

		public async Task Start(string clientName, string queueName)
		{
			// Dispose any channel from a previous Start (the host watchdog re-calls Start after a
			// disconnect). Disposal also removes it from automatic-recovery tracking, so a late
			// connection recovery can't resurrect the old consumer alongside the new one and
			// double-deliver events.
			await DisposeChannelAsync();

			if (!await VerifyAndCreateClients(clientName))
				return;

			// _channel stays null when the connection couldn't be created; skip monitoring so the
			// caller sees IsConnected() == false and can retry instead of an NRE killing the task.
			if (_channel == null)
				return;

			try
			{
				await StartMonitoring(queueName);
			}
			catch
			{
				// If consumer registration fails the channel is open but consumes nothing, so
				// IsConnected() would report healthy and the host watchdog would never rebuild.
				// Tear the channel down (nulled field makes IsConnected() false) and rethrow for
				// the caller's retry path.
				await DisposeChannelAsync();
				throw;
			}
		}

		private async Task DisposeChannelAsync()
		{
			var channel = _channel;
			_channel = null;

			if (channel == null)
				return;

			try
			{
				await channel.DisposeAsync();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		private async Task<bool> VerifyAndCreateClients(string clientName)
		{
			try
			{
				_connection = await RabbitConnection.CreateConnection(clientName);

				if (_connection != null)
				{
					_channel = await _connection.CreateChannelAsync();

					if (_channel != null)
					{
						await _channel.ExchangeDeclareAsync(RabbitConnection.SetQueueNameForEnv(Topics.EventingTopic), "fanout");
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

		private async Task StartMonitoring(string queueName)
		{
			//var queueName = _channel.QueueDeclare().QueueName;

			var queue = await _channel.QueueDeclareAsync(RabbitConnection.SetQueueNameForEnv(queueName), durable: true,
							autoDelete: false, exclusive: false);

			await _channel.QueueBindAsync(queue: queue.QueueName,
				exchange: RabbitConnection.SetQueueNameForEnv(Topics.EventingTopic),
				routingKey: "");

			var consumer = new AsyncEventingBasicConsumer(_channel);
			consumer.ReceivedAsync += async (model, ea) =>
			{
				var body = ea.Body.ToArray();
				var message = Encoding.UTF8.GetString(body);

				try
				{
					var eventingMessage = JsonConvert.DeserializeObject<EventingMessage>(message);

					if (eventingMessage != null)
					{
						switch ((EventingTypes)eventingMessage.Type)
						{
							case EventingTypes.PersonnelStatusUpdated:
								if (ProcessPersonnelStatusChanged != null)
									await ProcessPersonnelStatusChanged(eventingMessage.DepartmentId, eventingMessage.ItemId);
								break;
							case EventingTypes.UnitStatusUpdated:
								if (ProcessUnitStatusChanged != null)
									await ProcessUnitStatusChanged.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
								break;
							case EventingTypes.CallsUpdated:
								if (ProcessCallStatusChanged != null)
									await ProcessCallStatusChanged.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
								break;
							case EventingTypes.CallAdded:
								if (ProcessCallAdded != null)
									await ProcessCallAdded.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
								break;
							case EventingTypes.CallClosed:
								if (ProcessCallClosed != null)
									await ProcessCallClosed.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
								break;
							case EventingTypes.PersonnelStaffingUpdated:
								if (ProcessPersonnelStaffingChanged != null)
									await ProcessPersonnelStaffingChanged.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
								break;
							case EventingTypes.PersonnelLocationUpdated:
								if (PersonnelLocationUpdated != null)
									await PersonnelLocationUpdated.Invoke(eventingMessage.DepartmentId, JsonConvert.DeserializeObject<PersonnelLocationUpdatedEvent>(eventingMessage.Payload));
								break;
							case EventingTypes.UnitLocationUpdated:
								if (UnitLocationUpdated != null)
									await UnitLocationUpdated.Invoke(eventingMessage.DepartmentId, JsonConvert.DeserializeObject<UnitLocationUpdatedEvent>(eventingMessage.Payload));
								break;
							case EventingTypes.IncidentCommandUpdated:
								if (ProcessIncidentCommandUpdated != null)
									await ProcessIncidentCommandUpdated.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
								break;
							case EventingTypes.ChatEvent:
								if (ProcessChatEvent != null)
									await ProcessChatEvent.Invoke(eventingMessage.DepartmentId, eventingMessage.Payload);
								break;
							default:
								Logging.LogError($"RabbitInboundEventProvider received unknown eventing message type {eventingMessage.Type}; acking and dropping it.");
								break;
						}
					}

					await _channel.BasicAckAsync(ea.DeliveryTag, false);
				}
				catch (Exception ex)
				{
					// One guard for every handler in the switch (chat included): a handler exception is
					// logged with the offending message and the delivery is nacked, so a bad message can
					// never propagate out and destabilize the consumer loop.
					var context = message != null && message.Length > 500 ? message.Substring(0, 500) : message;
					Logging.LogException(ex, $"RabbitInboundEventProvider failed processing an eventing message; nacking. Raw: {context}");

					try
					{
						await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
					}
					catch (Exception nackEx)
					{
						Logging.LogException(nackEx);
					}
				}
			};
			await _channel.BasicConsumeAsync(queue: queue.QueueName,
				autoAck: false,
				consumer: consumer);
		}

		public bool IsConnected()
		{
			if (_channel == null)
				return false;

			return _channel.IsOpen;
		}

		public void RegisterForEvents(Func<int, string, Task> personnelStatusChanged,
									  Func<int, string, Task> unitStatusChanged,
									  Func<int, string, Task> callStatusChanged,
									  Func<int, string, Task> personnelStaffingChanged,
									  Func<int, string, Task> callAdded,
									  Func<int, string, Task> callClosed,
									  Func<int, PersonnelLocationUpdatedEvent, Task> personnelLocationUpdated,
									  Func<int, UnitLocationUpdatedEvent, Task> unitLocationUpdated,
									  Func<int, string, Task> incidentCommandUpdated)
		{
			ProcessPersonnelStatusChanged = personnelStatusChanged;
			ProcessUnitStatusChanged = unitStatusChanged;
			ProcessCallStatusChanged = callStatusChanged;
			ProcessPersonnelStaffingChanged = personnelStaffingChanged;
			ProcessCallAdded = callAdded;
			ProcessCallClosed = callClosed;
			PersonnelLocationUpdated = personnelLocationUpdated;
			UnitLocationUpdated = unitLocationUpdated;
			ProcessIncidentCommandUpdated = incidentCommandUpdated;
		}

		public void RegisterForChatEvents(Func<int, string, Task> chatEvent)
		{
			ProcessChatEvent = chatEvent;
		}
	}
}
