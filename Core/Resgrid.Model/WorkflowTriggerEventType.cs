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
		IncidentClosed = 37,
		PublicIncidentNoteAdded = 38,
		InternalIncidentNoteAdded = 39,
		PublicIncidentDocumentAdded = 40,
		InternalIncidentDocumentAdded = 41,
		IncidentNoteRemoved = 42,
		IncidentDocumentRemoved = 43,
		IncidentActionPlanUpdated = 44,
		IncidentCommandPostUpdated = 45,
		IncidentPublicSharingEnabled = 46,
		IncidentPublicSharingDisabled = 47,

		// Run card dispatch system
		RunCardActivated = 48,
		CallAlarmEscalated = 49,
		DispatchShortfallDetected = 50,
		StationCoverageGapDetected = 51,

		// -- Records (RMS) block 100-115 -- Identifier Allocation Registry section 3.2. Values 52-99 are
		// reserved by other pending plans and must not be taken here. Workflow definitions persist the
		// integer, so these are append-only and never renumbered. 103 (RecordApproved) and 113-114 are
		// RMS-1B, 108-111 RMS-2, 112 RMS-3; they are appended when their package lands.
		// Emitted after commit through the DomainEventOutbox, never on a draft autosave.

		/// <summary>A new non-legacy Record is first persisted (once, not per autosave).</summary>
		RecordCreated = 100,

		/// <summary>An author explicitly sent a Record to review.</summary>
		RecordSubmittedForReview = 101,

		/// <summary>A reviewer returned a Record with a reason code.</summary>
		RecordReturnedForCorrection = 102,

		/// <summary>A Record revision was finalized/attested. One-step Records emit Created then Finalized.</summary>
		RecordFinalized = 104,

		/// <summary>An amendment revision was finalized, referencing the prior revision.</summary>
		RecordAmended = 105,

		/// <summary>An authorized void completed with a reason code.</summary>
		RecordVoided = 106,

		/// <summary>A non-finalized Record was abandoned; carries the reserved-number disposition.</summary>
		RecordCancelled = 107
	}

	public static class WorkflowTriggerEventTypes
	{
		/// <summary>The Records (RMS) trigger block, 100-115, assigned by the Identifier Allocation Registry section 3.2.</summary>
		public const int RecordsBlockFirst = 100;
		public const int RecordsBlockLast = 115;

		public static bool IsRecordsTrigger(WorkflowTriggerEventType type)
		{
			var value = (int)type;
			return value >= RecordsBlockFirst && value <= RecordsBlockLast;
		}
	}
}

