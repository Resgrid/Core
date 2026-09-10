namespace Resgrid.Model
{
	public enum WorkflowTriggerEventType
	{
		ChecklistCompleted = 67,
		ChecklistFailed = 68,
		ChecklistMissed = 69,
		ChecklistScheduleChanged = 164,
		ChecklistOccurrenceSkipped = 165,
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

		/// <summary>An approver completed the Approval/Acknowledgement preset's approve step, before finalization.</summary>
		RecordApproved = 103,

		/// <summary>A Record revision was finalized/attested. One-step Records emit Created then Finalized.</summary>
		RecordFinalized = 104,

		/// <summary>An amendment revision was finalized, referencing the prior revision.</summary>
		RecordAmended = 105,

		/// <summary>An authorized void completed with a reason code.</summary>
		RecordVoided = 106,

		/// <summary>A non-finalized Record was abandoned; carries the reserved-number disposition.</summary>
		RecordCancelled = 107,

		// RMS-2 reporting-destination triggers (plan section 5.6). Emitted by the submission worker (41), never
		// from the Records transaction; payloads carry sanitized submission.* data and never the destination payload.

		/// <summary>A finalized revision was queued for NERIS or another configured reporting destination.</summary>
		RecordSubmissionQueued = 108,

		/// <summary>The destination accepted the immutable revision.</summary>
		RecordSubmissionAccepted = 109,

		/// <summary>The destination rejected it with normalized, non-sensitive error codes.</summary>
		RecordSubmissionRejected = 110,

		/// <summary>Delivery exhausted its retries or requires operator attention.</summary>
		RecordSubmissionFailed = 111,

		// RMS-3 obligation trigger (plan section 4.7). Emitted by the due-state evaluation worker (42) on the
		// transition into overdue only, from a persisted RmsRecordDueState row, so a missed or repeated worker run
		// can neither double-emit nor silently skip.

		/// <summary>A Record passed the due time of a review, correction or resubmission obligation.</summary>
		RecordOverdue = 112,

		// RMS-1B definition lifecycle (plan section 4.1). One trigger per lifecycle outcome, never a trigger per
		// department definition; the definition key/version travels in the definition.* block.

		/// <summary>A department Record definition version was published and new Records may start on it.</summary>
		RecordDefinitionPublished = 113,

		/// <summary>A department Record definition was retired; historical Records stay usable, new ones cannot start.</summary>
		RecordDefinitionRetired = 114,

		/// <summary>An attachment was added to a Record draft or amendment; carries safe metadata only, never bytes or names.</summary>
		RecordAttachmentAdded = 115,

		// -- Records (RMS) block 2, 152-163 -- Identifier Allocation Registry section 3.2 (allocated 2026-09-05). The
		// first block ran out with 113/114 reserved for RMS-1B definitions; 116-151 belong to Incident Back Office
		// and AI Dispatch. These cover the RMS-3 capabilities (disclosures, legal holds, evidence, retention) and the
		// department-authored export schedule so every RMS capability has a subscribable outcome.

		/// <summary>A public-records request was logged and its statutory clock started.</summary>
		RecordDisclosureRequested = 152,

		/// <summary>An immutable disclosure packet was produced for a request.</summary>
		RecordDisclosureProduced = 153,

		/// <summary>A produced packet was released to the requester.</summary>
		RecordDisclosureReleased = 154,

		/// <summary>A disclosure request was closed with a disposition.</summary>
		RecordDisclosureClosed = 155,

		/// <summary>A legal hold was placed on a record, a definition or a period.</summary>
		RecordLegalHoldPlaced = 156,

		/// <summary>A legal hold was released.</summary>
		RecordLegalHoldReleased = 157,

		/// <summary>An immutable evidence artifact was captured against a record.</summary>
		RecordEvidenceCaptured = 158,

		/// <summary>The retention sweep purged a record's content, leaving a tombstone.</summary>
		RecordPurged = 159,

		/// <summary>A department export template's schedule came due and its file was rendered for delivery.</summary>
		RecordExportScheduled = 160,

		/// <summary>A prevention inspection was completed (RMS-5); carries the inspection identity, result and violation counts, never the notes.</summary>
		RecordInspectionCompleted = 161,

		/// <summary>A code violation passed its correction due date without being corrected (RMS-5); raised once per violation by the daily sweep.</summary>
		RecordViolationOverdue = 162,

		/// <summary>An issued permit is inside its expiry notice window (RMS-5); raised once per permit by the daily sweep.</summary>
RecordPermitExpiring = 163,
		WorkOrderCreated = 70,
		WorkOrderStatusChanged = 71,
		WorkOrderAssigned = 72,
		WorkOrderOverdue = 73,
		WorkOrderSafetyHoldApplied = 167,
		WorkOrderSafetyHoldReleased = 168,
		WorkOrderRecurrenceChanged = 169,
		WorkOrderThresholdReached = 170,
		WorkOrderDeferred = 171,
		WorkOrderPartChanged = 172,
		WorkOrderApprovalChanged = 173,
		WorkOrderSlaBreached = 174,
		WorkOrderVendorChargeChanged = 175,
		WorkOrderPolicyChanged = 176,

		// Inventory modernization: persisted registry allocations; InventoryAdjusted retains value 22.
		InventoryTransferCompleted = 58,
		InventoryIssued = 59,
		InventoryReturned = 60,
		InventoryLowStock = 61,
		InventoryExpiring = 62,
		InventoryCountCompleted = 63,
		InventoryReturnOverdue = 166,
		InventoryAssetStatusChanged = 64,
		InventoryPurchaseOrderReceived = 65,
		ControlledSubstanceRecorded = 66
	}

	public static class WorkflowTriggerEventTypes
	{
		/// <summary>The Records (RMS) trigger block, 100-115, assigned by the Identifier Allocation Registry section 3.2.</summary>
		public const int RecordsBlockFirst = 100;
		public const int RecordsBlockLast = 115;

		/// <summary>The second Records block, 152-163 (registry section 3.2, allocated 2026-09-05); 161-163 were taken by RMS-5 on 2026-09-07.</summary>
		public const int RecordsBlock2First = 152;
		public const int RecordsBlock2Last = 163;

		public static bool IsRecordsTrigger(WorkflowTriggerEventType type)
		{
			var value = (int)type;
			return value >= RecordsBlockFirst && value <= RecordsBlockLast || value >= RecordsBlock2First && value <= RecordsBlock2Last;
		}
	}
}
