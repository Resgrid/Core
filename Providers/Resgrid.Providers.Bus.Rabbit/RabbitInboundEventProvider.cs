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

		public async Task Start(string clientName, string queueName)
		{
			await VerifyAndCreateClients(clientName);
			await StartMonitoring(queueName);
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
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			};
			await _channel.BasicConsumeAsync(queue: queue.QueueName,
				autoAck: true,
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
									  Func<int, UnitLocationUpdatedEvent, Task> unitLocationUpdated)
		{
			ProcessPersonnelStatusChanged = personnelStatusChanged;
			ProcessUnitStatusChanged = unitStatusChanged;
			ProcessCallStatusChanged = callStatusChanged;
			ProcessPersonnelStaffingChanged = personnelStaffingChanged;
			ProcessCallAdded = callAdded;
			ProcessCallClosed = callClosed;
			PersonnelLocationUpdated = personnelLocationUpdated;
			UnitLocationUpdated = unitLocationUpdated;
		}
	}
}
