using System;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus
{
	public class InboundEventProvider : IInboundEventProvider
	{
		private readonly IEventAggregator _eventAggregator;
		private static SubscriptionClient _topicClient;

		public Action<int, int> ProcessPersonnelStatusChanged;
		public Action<int, int> ProcessUnitStatusChanged;
		public Action<int, int> ProcessCallStatusChanged;
		public Action<int, int> ProcessPersonnelStaffingChanged;

		public InboundEventProvider(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;

			_topicClient = SubscriptionClient.CreateFromConnectionString(ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic, ServiceBusConfig.EventingTopicQueueName);

			// Configure the callback options.
			OnMessageOptions options = new OnMessageOptions();
			options.AutoComplete = true;
			options.AutoRenewTimeout = TimeSpan.FromMinutes(1);
			options.MaxConcurrentCalls = 1; // Indicates the maximum number of concurrent calls to the callback the pump should initiate 

			_topicClient.OnMessage((message) =>
			{
				try
				{
					if (message.Properties["Type"] != null && message.Properties["DepartmentId"] != null && message.Properties["ItemId"] != null)
					{
						switch ((EventingTypes) int.Parse(message.Properties["Type"].ToString()))
						{
							case EventingTypes.PersonnelStatusUpdated:
								ProcessPersonnelStatusChanged?.Invoke(int.Parse(message.Properties["DepartmentId"].ToString()), int.Parse(message.Properties["ItemId"].ToString()));
								break;
							case EventingTypes.UnitStatusUpdated:
								ProcessUnitStatusChanged?.Invoke(int.Parse(message.Properties["DepartmentId"].ToString()), int.Parse(message.Properties["ItemId"].ToString()));
								break;
							case EventingTypes.CallsUpdated:
								ProcessCallStatusChanged?.Invoke(int.Parse(message.Properties["DepartmentId"].ToString()), int.Parse(message.Properties["ItemId"].ToString()));
								break;
							case EventingTypes.PersonnelStaffingUpdated:
								ProcessPersonnelStaffingChanged?.Invoke(int.Parse(message.Properties["DepartmentId"].ToString()), int.Parse(message.Properties["ItemId"].ToString()));
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}, options);
		}

		public void RegisterForEvents(Action<int, int> personnelStatusChanged, Action<int, int> unitStatusChanged, Action<int, int> callStatusChanged, Action<int, int> personnelStaffingChanged)
		{
			ProcessPersonnelStatusChanged = personnelStatusChanged;
			ProcessUnitStatusChanged = unitStatusChanged;
			ProcessCallStatusChanged = callStatusChanged;
			ProcessPersonnelStaffingChanged = personnelStaffingChanged;
		}
	}
}
