using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Message = Microsoft.Azure.ServiceBus.Message;

namespace Resgrid.Providers.Bus
{
	public class InboundEventProvider : IInboundEventProvider
	{
		private readonly IEventAggregator _eventAggregator;
		private static SubscriptionClient _topicClient;

		public Func<int, int, Task> ProcessPersonnelStatusChanged;
		public Func<int, int, Task> ProcessUnitStatusChanged;
		public Func<int, int, Task> ProcessCallStatusChanged;
		public Func<int, int, Task> ProcessPersonnelStaffingChanged;

		public InboundEventProvider(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;

			if (SystemBehaviorConfig.ServiceBusType != ServiceBusTypes.Rabbit)
			{
				_topicClient = new SubscriptionClient(ServiceBusConfig.AzureEventingTopicConnectionString,
					Topics.EventingTopic, ServiceBusConfig.EventingTopicQueueName);

				var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
				{

					MaxConcurrentCalls = 1,
					AutoComplete = true,
					MaxAutoRenewDuration = TimeSpan.FromMinutes(1)
				};

				// Register the function that processes messages.
				_topicClient.RegisterMessageHandler(ProcessMessagesAsync, messageHandlerOptions);
			}
		}

		private async Task ProcessMessagesAsync(Message message, CancellationToken token)
		{
			try
			{
				if (message.UserProperties["Type"] != null && message.UserProperties["DepartmentId"] != null && message.UserProperties["ItemId"] != null)
				{
					switch ((EventingTypes)int.Parse(message.UserProperties["Type"].ToString()))
					{
						case EventingTypes.PersonnelStatusUpdated:
							if (ProcessPersonnelStatusChanged != null)
								await ProcessPersonnelStatusChanged(int.Parse(message.UserProperties["DepartmentId"].ToString()), int.Parse(message.UserProperties["ItemId"].ToString()));
							break;
						case EventingTypes.UnitStatusUpdated:
							if (ProcessUnitStatusChanged != null)
								await ProcessUnitStatusChanged.Invoke(int.Parse(message.UserProperties["DepartmentId"].ToString()), int.Parse(message.UserProperties["ItemId"].ToString()));
							break;
						case EventingTypes.CallsUpdated:
							if (ProcessCallStatusChanged != null)
								await ProcessCallStatusChanged.Invoke(int.Parse(message.UserProperties["DepartmentId"].ToString()), int.Parse(message.UserProperties["ItemId"].ToString()));
							break;
						case EventingTypes.PersonnelStaffingUpdated:
							if (ProcessPersonnelStaffingChanged != null)
								await ProcessPersonnelStaffingChanged.Invoke(int.Parse(message.UserProperties["DepartmentId"].ToString()), int.Parse(message.UserProperties["ItemId"].ToString()));
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}
				}

				await _topicClient.CompleteAsync(message.SystemProperties.LockToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		private static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
		{
			Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
			var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
			Console.WriteLine("Exception context for troubleshooting:");
			Console.WriteLine($"- Endpoint: {context.Endpoint}");
			Console.WriteLine($"- Entity Path: {context.EntityPath}");
			Console.WriteLine($"- Executing Action: {context.Action}");
			return Task.CompletedTask;
		}

		public void RegisterForEvents(Func<int, int, Task> personnelStatusChanged, Func<int, int, Task> unitStatusChanged, Func<int, int, Task> callStatusChanged, Func<int, int, Task> personnelStaffingChanged)
		{
			ProcessPersonnelStatusChanged = personnelStatusChanged;
			ProcessUnitStatusChanged = unitStatusChanged;
			ProcessCallStatusChanged = callStatusChanged;
			ProcessPersonnelStaffingChanged = personnelStaffingChanged;
		}
	}
}
