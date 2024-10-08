using Resgrid.Model.Events;
using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IRabbitInboundEventProvider
	{
		Task Start(string clientName, string queueName);
		void RegisterForEvents(Func<int, string, Task> personnelStatusChanged,
							   Func<int, string, Task> unitStatusChanged,
							   Func<int, string, Task> callStatusChanged,
							   Func<int, string, Task> personnelStaffingChanged,
							   Func<int, string, Task> callAdded,
							   Func<int, string, Task> callClosed,
							   Func<int, PersonnelLocationUpdatedEvent, Task> personnelLocationUpdated,
							   Func<int, UnitLocationUpdatedEvent, Task> unitLocationUpdated);
	}
}
