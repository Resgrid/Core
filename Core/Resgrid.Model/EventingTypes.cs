namespace Resgrid.Model
{
	public enum EventingTypes
	{
		PersonnelStatusUpdated = 1,
		UnitStatusUpdated = 2,
		CallsUpdated = 3,
		PersonnelStaffingUpdated = 4,
		CallAdded = 5,
		CallClosed = 6,
		PersonnelLocationUpdated = 7,
		UnitLocationUpdated = 8,
		IncidentCommandUpdated = 9,
		ChatEvent = 10,
		ChecklistUpdated = 11,

		/// <summary>An event for one signed-in session only, such as an approval decision (passkey workbook section 7.4).</summary>
		SessionEvent = 12,

		/// <summary>A unit status timer acknowledgement changed; the item id is the unit id.</summary>
		UnitStatusAlertUpdated = 13,

		/// <summary>Calls awaiting resources changed, or a freed unit matches one; item id = call id (0 = several), payload = OutstandingRequirementsAlert.</summary>
		OutstandingRequirementsUpdated = 14,

		/// <summary>Who holds a unit's active roles (its current crew) changed; item id = unit id (0 = several units).</summary>
		UnitActiveRolesUpdated = 15,

		/// <summary>A navigating unit's live ETA changed or its navigation ended; item id = unit id, payload = ids and state (no ETA).</summary>
		UnitNavigationUpdated = 16,

		/// <summary>A department's recurring call alerts changed (hub event <c>recurringCallAlertsUpdated</c>: schedule id, payload).</summary>
		RecurringCallAlertsUpdated = 17,

		/// <summary>A call's Enhanced AI destination advice changed (hub event <c>aiDestinationAdviceUpdated</c>: call id, payload = advice id and state; M0323).</summary>
		AiDestinationAdviceUpdated = 18,

		/// <summary>
		/// A department's dispatcher desk changed: a dispatcher signed in or off, went on or came back from break, was set Away, or
		/// the dispatch roster or break plan changed (hub event <c>dispatcherDeskUpdated</c>: user id, payload).
		/// </summary>
		DispatcherDeskUpdated = 19,

		/// <summary>
		/// A department's dispatcher queue changed: work was added, offered, taken, released, transferred, completed or overflowed,
		/// or a conversation message arrived (hub event <c>dispatcherQueueUpdated</c>: item id, payload).
		/// </summary>
		DispatcherQueueUpdated = 20,

		/// <summary>An intake SMS conversation changed (hub event <c>intakeConversationUpdated</c>: call id, payload = conversation id, state, handler; M0325).</summary>
		IntakeConversationUpdated = 21
	}
}
