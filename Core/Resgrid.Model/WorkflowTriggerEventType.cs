namespace Resgrid.Model
{
	public enum WorkflowTriggerEventType
	{
		CallAdded = 0,
		CallUpdated = 1,
		CallClosed = 2,
		UnitStatusChanged = 3,
		PersonnelStaffingChanged = 4,
		PersonnelStatusChanged = 5,
		UserCreated = 6,
		UserAssignedToGroup = 7,
		DocumentAdded = 8,
		NoteAdded = 9,
		UnitAdded = 10,
		LogAdded = 11,
		CalendarEventAdded = 12,
		CalendarEventUpdated = 13,
		ShiftCreated = 14,
		ShiftUpdated = 15,
		ResourceOrderAdded = 16,
		ShiftTradeRequested = 17,
		ShiftTradeFilled = 18,
		MessageSent = 19,
		TrainingAdded = 20,
		TrainingUpdated = 21,
		InventoryAdjusted = 22,
		CertificationExpiring = 23,
		FormSubmitted = 24,
		PersonnelRoleChanged = 25,
		GroupAdded = 26,
		GroupUpdated = 27,

		// Incident Command (§3.12)
		CommandEstablished = 28,
		ResourceAssigned = 29,
		ResourceReleased = 30,
		ObjectiveCompleted = 31,
		CriticalParDetected = 32,
		CommandTransferred = 33,
		IncidentRoleAssigned = 34,
		AdHocResourceCreated = 35,
		IncidentChannelOpened = 36,
		IncidentClosed = 37
	}
}

