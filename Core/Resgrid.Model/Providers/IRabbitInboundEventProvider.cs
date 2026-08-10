using Resgrid.Model.Events;
using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IRabbitInboundEventProvider
	{
		Task Start(string clientName, string queueName);

		/// <summary>
		/// True when the consumer channel exists and is open. Hosts poll this to detect a dead
		/// consumer (e.g. after the shared connection was force-reset) and re-call Start.
		/// </summary>
		bool IsConnected();
		void RegisterForEvents(Func<int, string, Task> personnelStatusChanged,
							   Func<int, string, Task> unitStatusChanged,
							   Func<int, string, Task> callStatusChanged,
							   Func<int, string, Task> personnelStaffingChanged,
							   Func<int, string, Task> callAdded,
							   Func<int, string, Task> callClosed,
							   Func<int, PersonnelLocationUpdatedEvent, Task> personnelLocationUpdated,
							   Func<int, UnitLocationUpdatedEvent, Task> unitLocationUpdated,
							   Func<int, string, Task> incidentCommandUpdated);

		/// <summary>
		/// Registers the chat event callback separately so hosts that don't relay chat (workers, TTS)
		/// need no changes. The callback receives (departmentId, ChatEventRaised JSON payload).
		/// </summary>
		void RegisterForChatEvents(Func<int, string, Task> chatEvent);
	}
}
