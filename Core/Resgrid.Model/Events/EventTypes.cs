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
		IncidentReleaseProjection = 49
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
