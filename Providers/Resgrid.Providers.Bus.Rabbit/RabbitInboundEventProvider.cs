using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitInboundEventProvider: IRabbitInboundEventProvider
	{
		private ConnectionFactory _factory;
		private IConnection _connection;
		private IModel _channel;

		public Func<int, int, Task> ProcessPersonnelStatusChanged;
		public Func<int, int, Task> ProcessUnitStatusChanged;
		public Func<int, int, Task> ProcessCallStatusChanged;
		public Func<int, int, Task> ProcessPersonnelStaffingChanged;

		public async Task Start()
		{
			VerifyAndCreateClients();
			await StartMonitoring();
		}

		private void VerifyAndCreateClients()
		{
			_factory = new ConnectionFactory() { HostName = ServiceBusConfig.RabbitHostname, UserName = ServiceBusConfig.RabbitUsername, Password = ServiceBusConfig.RabbbitPassword };
			_connection = _factory.CreateConnection();
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
							case EventingTypes.PersonnelStaffingUpdated:
								if (ProcessPersonnelStaffingChanged != null)
									await ProcessPersonnelStaffingChanged.Invoke(eventingMessage.DepartmentId, eventingMessage.ItemId);
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

		public void RegisterForEvents(Func<int, int, Task> personnelStatusChanged, Func<int, int, Task> unitStatusChanged, Func<int, int, Task> callStatusChanged, Func<int, int, Task> personnelStaffingChanged)
		{
			ProcessPersonnelStatusChanged = personnelStatusChanged;
			ProcessUnitStatusChanged = unitStatusChanged;
			ProcessCallStatusChanged = callStatusChanged;
			ProcessPersonnelStaffingChanged = personnelStaffingChanged;
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
