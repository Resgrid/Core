using System;
using System.Text;
using System.Threading;
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
		private ConnectionFactory _factory;
		private IConnection _connection;
		private IModel _channel;

		public Func<int, string, Task> ProcessPersonnelStatusChanged;
		public Func<int, string, Task> ProcessUnitStatusChanged;
		public Func<int, string, Task> ProcessCallStatusChanged;
		public Func<int, string, Task> ProcessCallAdded;
		public Func<int, string, Task> ProcessCallClosed;
		public Func<int, string, Task> ProcessPersonnelStaffingChanged;
		public Func<int, PersonnelLocationUpdatedEvent, Task> PersonnelLocationUpdated;
		public Func<int, UnitLocationUpdatedEvent, Task> UnitLocationUpdated;

		public async Task Start()
		{
			VerifyAndCreateClients();
			await StartMonitoring();
		}

		private void VerifyAndCreateClients()
		{
			// I know....I know.....
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

			_channel = _connection.CreateModel();
		}

		private async Task StartMonitoring()
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				var queueName = _channel.QueueDeclare().QueueName;

				_channel.QueueBind(queue: queueName,
					exchange: SetQueueNameForEnv(Topics.EventingTopic),
					routingKey: "");

				var consumer = new EventingBasicConsumer(_channel);
				consumer.Received += async (model, ea) =>
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
								if (ProcessCallStatusChanged != null)
									await ProcessCallStatusChanged.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
								break;
							case EventingTypes.CallClosed:
								if (ProcessCallStatusChanged != null)
									await ProcessCallStatusChanged.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
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
				_channel.BasicConsume(queue: queueName,
					autoAck: true,
					consumer: consumer);
			}
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
