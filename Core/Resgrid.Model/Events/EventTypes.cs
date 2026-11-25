using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model.Events
{
	public enum EventTypes
	{
		[Display(Name = "Unit Status Changed")]
		UnitStatusChanged			= 0,

		[Display(Name = "Personnel Staffing Changed")]
		PersonnelStaffingChanged	= 1,

		[Display(Name = "Personnel Status Changed")]
		PersonnelStatusChanged		= 2,

		[Display(Name = "User Created")]
		UserCreated					= 3,

		[Display(Name = "User Assigned To Group")]
		UserAssignedToGroup			= 4,

		[Display(Name = "Calendar Event Upcoming")]
		CalendarEventUpcoming		= 5,

		[Display(Name = "Document Added")]
		DocumentAdded				= 6,

		[Display(Name = "Note Added")]
		NoteAdded					= 7,

		[Display(Name = "Unit Added")]
		UnitAdded					= 8,

		[Display(Name = "Log Added")]
		LogAdded					= 9,

		[Display(Name = "Department Settings Changed")]
		DepartmentSettingsChanged	= 10,

		[Display(Name = "Roles for Group Availability Alert")]
		RolesInGroupAvailabilityAlert = 11,

		[Display(Name = "Unit Types for Group Availability Alert")]
		UnitTypesInGroupAvailabilityAlert = 12,

		[Display(Name = "Roles for Department Availability Alert")]
		RolesInDepartmentAvailabilityAlert = 13,

		[Display(Name = "Unit Types for Department Availability Alert")]
		UnitTypesInDepartmentAvailabilityAlert = 14,

		[Display(Name = "Calendar Event Added")]
		CalendarEventAdded = 15,

		[Display(Name = "Calendar Event Updated")]
		CalendarEventUpdated = 16,

		[Display(Name = "Shift Created")]
		ShiftCreated = 17,

		[Display(Name = "Shift Updated")]
		ShiftUpdated = 18,

		[Display(Name = "Shift Days Added")]
		ShiftDaysAdded = 19,

		[Display(Name = "Linked Department Call Added")]
		LinkedDepartmentCallAdded = 20,

		[NotMapped]
		ResourceOrderAdded = 21,

		[NotMapped]
		WeatherAlertReceived = 22,

		[NotMapped]
		WeatherAlertExpired = 23,

		[NotMapped]
		ModerationRequestCompleted = 24,

		// Workforce & Business Operations plan Phase D (registry 25-29; plan D6). Department broadcasts raised by the
		// certification expiry worker (34); 25-27 resolve the holder's group, 28-29 the unit's station group.
		[Display(Name = "Certification Expiring")]
		CertificationExpiring = 25,

		[Display(Name = "Certification Expired")]
		CertificationExpired = 26,

		[Display(Name = "Role Removed for Certification")]
		CertificationRoleRemoved = 27,

		[Display(Name = "Unit Certification Expiring")]
		UnitCertificationExpiring = 28,

		[Display(Name = "Unit Certification Expired")]
		UnitCertificationExpired = 29,

		// -- Records (RMS) block 31-33 -- Identifier Allocation Registry section 3.5. 25-29 belong to
		// Certifications and 30 is the buffer; neither may be taken here. 31 is raised by
		// RecordsService.ReturnForCorrectionAsync and delivered to the author by IRecordsNotificationService
		// (NotificationBroadcastLogic); it stays NotMapped because it is author-targeted, not a department
		// broadcast the settings UI should offer. 32 and 33 remain NotMapped until their emitters land.

		/// <summary>A reviewer returned a Record to its author for correction. On by default once shipped; individually disableable.</summary>
		[NotMapped]
		RecordReturnedForCorrection = 31,

		/// <summary>A Record review obligation is overdue (worker 42, RMS-3).</summary>
		[NotMapped]
		RecordReviewOverdue = 32,

		/// <summary>A reporting destination rejected a submitted Record revision (RMS-2).</summary>
		[NotMapped]
		RecordSubmissionRejected = 33,

		// -- Department Import block 50-51 -- Identifier Allocation Registry section 4A. Both are targeted at the
		// workspace owner and delivered by IDepartmentImportNotificationService (NotificationBroadcastLogic), so they
		// stay NotMapped: they are not department broadcasts the notification settings UI should offer.

		/// <summary>An import run finished, failed, paused or awaits action; also the abandoned-workspace reminder.</summary>
		[NotMapped]
		DepartmentImportRunFinished = 50,

		/// <summary>Resgrid support posted a message, suggestion or support-request status change on an import workspace.</summary>
		[NotMapped]
		DepartmentImportSupportMessage = 51,

		// -- Incident Back Office block 35-44 and 49 -- Identifier Allocation Registry section 4C (plan §13.8). Built-in
		// operational notices addressed to whoever currently holds an incident position, resolved at send time by
		// IIncidentNotificationRoutingService and delivered through the shipped stack. NotMapped like 31-33: they are
		// position-targeted, not department broadcasts the notification settings UI offers. 45 is AI Dispatch's, 46-48
		// Enhanced AI's. Payloads carry counts, kinds, ETAs, numbers, property names and dates, never a protected field.

		/// <summary>A position assignment was created, changed or relieved (LI-A). To the holder and their supervisor position.</summary>
		[NotMapped]
		IncidentPositionNotification = 35,

		/// <summary>A subscribed scheduled situation report was generated (LI-A).</summary>
		[NotMapped]
		IncidentSituationReport = 36,

		/// <summary>A fill was accepted: ordered resources assigned with counts and ETA (LI-B). To LSC, OSC, FDUL, FCUL by default.</summary>
		[NotMapped]
		IncidentResourcesAssigned = 37,

		/// <summary>An inbound fill's ETA changed materially, arrival confirmed, or the resource is late (LI-B).</summary>
		[NotMapped]
		IncidentArrivalUpdate = 38,

		/// <summary>A person's own lodging assignment was confirmed or changed (LI-B). To that person only; never other occupants.</summary>
		[NotMapped]
		IncidentLodgingAssignment = 39,

		/// <summary>A release was approved or travel arranged (LI-B). To the released person and their supervisor position.</summary>
		[NotMapped]
		IncidentDemobNotification = 40,

		/// <summary>An item entered an approval queue (LI-C). To holders of the approving position or permission.</summary>
		[NotMapped]
		IncidentApprovalRequired = 41,

		/// <summary>An unapproved-spend, invoice or match exception posted (LI-C). To the Finance Section Chief and Procurement Unit Leader.</summary>
		[NotMapped]
		IncidentFinanceException = 42,

		/// <summary>A claim case was opened or its deadline entered the lead window (LI-C). To the Compensation/Claims Unit Leader.</summary>
		[NotMapped]
		IncidentClaimNotice = 43,

		/// <summary>A closeout milestone blocked or the final package generated (LI-C). To Command, DOCL and the Finance Section Chief.</summary>
		[NotMapped]
		IncidentCloseoutNotice = 44,

		/// <summary>The aggregate outbound projection changed materially (LI-B, rebased from 45). To LSC, OSC, RESL and DMOB.</summary>
		[NotMapped]
		IncidentReleaseProjection = 49,

		// -- Protocols P2 block 57-59 (protocols-training-knowledge-plan.md §5 P2). Member-targeted (the assignee, the overdue
		// member and their supervisor or group admin) and delivered directly by IProtocolAcknowledgmentService with push + email,
		// so NotMapped like the import block: not department broadcasts the notification settings UI should offer. The value is
		// also the push event code prefix the apps route on ("PA:" acknowledgment, "PB:" bulletin).

		/// <summary>A protocol version that requires acknowledgment was assigned to the member (publish, or entering its audience).</summary>
		[NotMapped]
		ProtocolAcknowledgmentAssigned = 57,

		/// <summary>A priority bulletin was issued to the member's audience (must-read).</summary>
		[NotMapped]
		ProtocolBulletinIssued = 58,

		/// <summary>An acknowledgment is due or overdue (to the member), or escalated (to their supervisor or group admin).</summary>
		[NotMapped]
		ProtocolAcknowledgmentOverdue = 59,

		// -- Member wellness WL1 block 68-69 (protocols-training-knowledge-plan.md §13b). Restricted tier: delivered directly by
		// IMemberHealthService to the designated or wellness officers (and the member), never a department broadcast, so NotMapped. The
		// text names no health detail. Push event code prefixes: "EI:" exposure case, "DS:" injury and duty status.

		/// <summary>A BBP / infectious-disease exposure was reported, or its Ryan White answer is overdue (to the designated officers).</summary>
		[NotMapped]
		ExposureIncidentNotice = 68,

		/// <summary>An injury was reported or a return-to-duty review is due (to the health officers).</summary>
		[NotMapped]
		DutyStatusNotice = 69,
		// -- e-Signature S3 block 66-67 (protocols-training-knowledge-plan.md §21.3, §23): multi-signer requests. Member-targeted and
		// delivered directly by ISignatureNotificationService with push + email, so NotMapped like the P2 block. The push event code
		// is "SG:{requestId}".

		/// <summary>It is the member's turn to sign a step of a multi-signer request (also the worker 90 reminder).</summary>
		[NotMapped]
		SignatureStepAssigned = 66,

		/// <summary>A multi-signer request the member requested or signs in was completed, declined, expired or withdrawn.</summary>
		[NotMapped]
		SignatureRequestClosed = 67,
		// -- Training T3 block 61-63 (protocols-training-knowledge-plan.md §13 T3, LMS). Member-, supervisor- and admin-targeted and
		// delivered directly by the LMS requirement engine with push + email (push code prefix "LR:"), so NotMapped like 57-59.

		/// <summary>A learning requirement (or an onboarding path) now applies to the member, with its due date.</summary>
		[NotMapped]
		LearningRequirementAssigned = 61,

		/// <summary>A requirement is at risk or overdue (to the member), escalated (to supervisors and named people), or completed (alerts).</summary>
		[NotMapped]
		LearningRequirementDue = 62,

		/// <summary>Training-backed enforcement acted or was undone (to the member), or a sweep refused to act (to the admins, R-L11).</summary>
		[NotMapped]
		LearningRequirementEnforcement = 63,
		// -- e-Signature S4 block 73-74 (protocols-training-knowledge-plan.md §23 S4): approval chains. Member-targeted and delivered
		// directly by IApprovalNotificationService with push + email, so NotMapped like 66-67. The push event code is "AP:{approvalRequestId}".

		/// <summary>An approval step is waiting for the member (its turn started, it was delegated to them, or it escalated to them).</summary>
		[NotMapped]
		ApprovalAwaiting = 73,

		/// <summary>The member's submission was approved, rejected (with the reason) or expired.</summary>
		[NotMapped]
		ApprovalDecided = 74,
		// -- Training T4 block 64-65 (protocols-training-knowledge-plan.md §13 T4, LMS): class sessions. Member-targeted and delivered
		// directly by the class service and worker 95 with push + email (push code prefix "CL:"), so NotMapped like 57-59.

		/// <summary>A class the member is enrolled in starts soon (worker 95 reminder).</summary>
		[NotMapped]
		ClassSessionReminder = 64,

		/// <summary>The member's place on a class roster changed: approved, declined, promoted from the waitlist, removed or cancelled.</summary>
		[NotMapped]
		ClassEnrollmentChanged = 65,
		// -- Training T4 skills block 70-71 (protocols-training-knowledge-plan.md §13 T4, LMS). Member- and officer-targeted, queued into the
		// skill notification digest (immediate, daily or weekly) and sent by worker 96 with push + email (push code prefix "SK:"), so
		// NotMapped like 61-63.

		/// <summary>A skill evaluation of the member was completed, failed or is waiting for a signature (to the member and the officers).</summary>
		[NotMapped]
		SkillEvaluationNotice = 70,

		/// <summary>A position task book was initiated, a task signed, the book recommended, certified or expired (to the trainee and the officers).</summary>
		[NotMapped]
		TaskBookNotice = 71
	}

	public static class EventOptions
	{
		public static HashSet<EventTypes> GroupEvents = new HashSet<EventTypes>()
		{
			EventTypes.UnitStatusChanged,
			EventTypes.PersonnelStatusChanged,
			EventTypes.PersonnelStaffingChanged,
			EventTypes.UserAssignedToGroup,
			EventTypes.RolesInGroupAvailabilityAlert,
			EventTypes.UnitTypesInGroupAvailabilityAlert,
			// Phase D: LockToGroup resolves the holder's group (25-27) or the unit's station group (28-29).
			EventTypes.CertificationExpiring,
			EventTypes.CertificationExpired,
			EventTypes.CertificationRoleRemoved,
			EventTypes.UnitCertificationExpiring,
			EventTypes.UnitCertificationExpired
		};

		public static HashSet<EventTypes> PreviousDataEnabled = new HashSet<EventTypes>()
		{
			EventTypes.UnitStatusChanged,
			EventTypes.PersonnelStatusChanged,
			EventTypes.PersonnelStaffingChanged
		};

		public static HashSet<EventTypes> PreviousDataMultiple = new HashSet<EventTypes>()
		{
			
		};

		public static HashSet<EventTypes> CurrentDataEnabled = new HashSet<EventTypes>()
		{
			EventTypes.UnitStatusChanged,
			EventTypes.PersonnelStatusChanged,
			EventTypes.PersonnelStaffingChanged
		};

		public static HashSet<EventTypes> CurrentDataMultiple = new HashSet<EventTypes>()
		{
			EventTypes.RolesInGroupAvailabilityAlert,
			EventTypes.UnitTypesInGroupAvailabilityAlert,
			EventTypes.RolesInDepartmentAvailabilityAlert,
			EventTypes.UnitTypesInDepartmentAvailabilityAlert
		};

		public static HashSet<EventTypes> UpperLimitEnabled = new HashSet<EventTypes>()
		{
			
		};

		public static HashSet<EventTypes> LowerEnabled = new HashSet<EventTypes>()
		{
			EventTypes.RolesInGroupAvailabilityAlert,
			EventTypes.UnitTypesInGroupAvailabilityAlert,
			EventTypes.RolesInDepartmentAvailabilityAlert,
			EventTypes.UnitTypesInDepartmentAvailabilityAlert
		};

		public static HashSet<EventTypes> AlertableEvents = new HashSet<EventTypes>()
		{
			EventTypes.RolesInGroupAvailabilityAlert,
			EventTypes.UnitTypesInGroupAvailabilityAlert,
			EventTypes.RolesInDepartmentAvailabilityAlert,
			EventTypes.UnitTypesInDepartmentAvailabilityAlert
		};

		public static HashSet<EventTypes> HiddenEventTypes = new HashSet<EventTypes>()
		{
			EventTypes.CalendarEventAdded,
			EventTypes.CalendarEventUpcoming,
			EventTypes.CalendarEventUpdated,
			EventTypes.ShiftCreated,
			EventTypes.ShiftUpdated,
			EventTypes.ShiftDaysAdded,
			EventTypes.WeatherAlertReceived,
			EventTypes.WeatherAlertExpired,
			EventTypes.ModerationRequestCompleted
		};
	}
}
