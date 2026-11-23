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
		void RegisterForChecklistEvents(Func<int, string, Task> checklistEvent);

		/// <summary>Events for one session (session id, serialized <c>SessionEventMessage</c>).</summary>
		void RegisterForSessionEvents(Func<string, string, Task> sessionEvent);

		/// <summary>Unit status timer acknowledgement changes (department id, unit id).</summary>
		void RegisterForUnitStatusAlertEvents(Func<int, string, Task> unitStatusAlertEvent);

		/// <summary>A unit's current crew (its active roles) changed (department id, unit id; 0 = several units).</summary>
		void RegisterForUnitActiveRolesEvents(Func<int, string, Task> unitActiveRolesEvent);

		/// <summary>Calls awaiting resources changed or a freed unit matches one (department id, call id, alert payload).</summary>
		void RegisterForOutstandingRequirementsEvents(Func<int, string, string, Task> outstandingRequirementsEvent);

		/// <summary>A navigating unit's live ETA changed or ended (department id, unit id, ids-and-state payload without the ETA).</summary>
		void RegisterForUnitNavigationEvents(Func<int, string, string, Task> unitNavigationEvent);

		/// <summary>The dispatcher desk board changed (department id, dispatcher user id or empty, payload { userId, state }).</summary>
		void RegisterForDispatcherDeskEvents(Func<int, string, string, Task> dispatcherDeskEvent);

		/// <summary>The dispatcher queue changed (department id, queue item id, payload { itemId, state, assignedUserId, kind }).</summary>
		void RegisterForDispatcherQueueEvents(Func<int, string, string, Task> dispatcherQueueEvent);

		/// <summary>Recurring call alerts changed (department id, schedule id, payload { alertId, kind, raised }).</summary>
		void RegisterForRecurringCallAlertsEvents(Func<int, string, string, Task> recurringCallAlertsEvent);

		/// <summary>A call's Enhanced AI destination advice changed (department id, call id, payload { adviceId, state, isChange }).</summary>
		void RegisterForAiDestinationAdviceEvents(Func<int, string, string, Task> aiDestinationAdviceEvent);

		/// <summary>An intake SMS conversation changed (department id, call id, payload { conversationId, state, handler }; no text).</summary>
		void RegisterForIntakeConversationEvents(Func<int, string, string, Task> intakeConversationEvent);
	}
}
