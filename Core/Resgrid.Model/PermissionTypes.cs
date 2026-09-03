namespace Resgrid.Model
{
	public enum PermissionTypes
	{
		AddPersonnel,
		RemovePersonnel,
		CreateCall,
		CreateTraining,
		CreateDocument,
		CreateCalendarEntry,
		CreateNote,
		CreateLog,
		CreateShift,
		ViewPersonalInfo,
		AdjustInventory,
		CanSeePersonnelLocations,
		CanSeeUnitLocations,
		CreateMessage,
		ViewGroupUsers,
		DeleteCall,
		CloseCall,
		AddCallData,
		ViewGroupUnits,
		ContactEdit,
		ContactView,
		ContactDelete,
		CreateWorkflow = 22,
		ManageWorkflowCredentials = 23,
		ViewWorkflowRuns = 24,
		ViewUdfFields = 25,
		ManageRoutes = 26,
		DeleteLog = 27,
		UseCalendarSync = 28,

		/// <summary>
		/// Who may sign in to the Dispatch app. Defaults to everyone in the department (no permission
		/// row = allowed, per IPermissionsService.IsUserAllowed), and can be narrowed to admins, group
		/// admins, or selected personnel roles. Dispatch surfaces private command, unit and responder
		/// traffic, so a department that isn't all-dispatchers should restrict this.
		/// </summary>
		DispatchAppLogin = 29,

		/// <summary>
		/// Who may act as a commander: sign in to the IC app, establish incident command on a call, and
		/// read command boards. Defaults to everyone in the department (no permission row = allowed) and
		/// can be narrowed to admins, group admins, or selected personnel roles — the same ladder as
		/// <see cref="DispatchAppLogin"/>.
		/// </summary>
		CommandAppLogin = 30,

		/// <summary>
		/// Manage post-enrollment Advanced Data Protection settings: step-up window and egress policy.
		/// Deliberately NOT sufficient for enrollment, offboarding, or ADP billing commands — those are
		/// restricted server-side to Department.ManagingUserId (ADP plan decision 15).
		/// </summary>
		ManageDepartmentDataProtection = 31,

		/// <summary>Reveal protected call/dispatch fields (with a current Protected Data Grant).</summary>
		ViewProtectedCallData = 32,

		/// <summary>Edit protected call/dispatch fields (with a current Protected Data Grant).</summary>
		EditProtectedCallData = 33,

		/// <summary>Reveal protected personnel/member fields (with a current Protected Data Grant).</summary>
		ViewProtectedPersonnelData = 34,

		/// <summary>Reveal protected contact fields (with a current Protected Data Grant).</summary>
		ViewProtectedContactData = 35,

		/// <summary>Reveal protected operational data — logs, forms/UDF, IC content, documents (with a grant).</summary>
		ViewProtectedOperationalData = 36,

		/// <summary>Export data containing protected fields; every export is separately audited.</summary>
		ExportProtectedData = 37,

		/// <summary>Configure per-channel protected-data egress (push/SMS/email/voice modes, PIN release).</summary>
		ConfigureProtectedDataEgress = 38,

		/// <summary>
		/// Emergency break-glass access to protected data. Off by default; every use requires a reason,
		/// produces notifications, and is subject to review (ADP plan section 12).
		/// </summary>
		BreakGlassProtectedData = 39,

		// -- Records (RMS) block 50-67 -- Identifier Allocation Registry section 4.1 ------------------
		// Values 40-49 are reserved for other pending plans (Contacts 42-43, Certifications 44-46,
		// Inventory 47-49) and must not be taken here. A missing Permission row means ALLOWED under the
		// documented no-row default below (ClaimsLogic.AddRecordClaims), so every default equals the
		// current Logs behavior and activation changes nobody's access by accident.

		/// <summary>Author Records (Record_View + Record_Create). No-row default: everyone (matches CreateLog). LockToGroup restricts authoring to the user's own group's subjects.</summary>
		CreateRecord = 50,

		/// <summary>Void a finalized Record or cancel a non-finalized one (Record_Void). No-row default: everyone. That is what AddDeleteLogClaims grants today when no DeleteLog row exists, so parity requires it even though the registry table says "department admins". A configured DeleteLog row is copied verbatim at activation.</summary>
		DeleteRecord = 51,

		/// <summary>Review a Record submitted for review (Record_Review). No-row default: department and group admins. LockToGroup: reviewers see only their group's queue.</summary>
		ReviewRecords = 52,

		/// <summary>Approve step of the Approval/Acknowledgement preset (Record_Approve). No-row default: department admins.</summary>
		ApproveRecords = 53,

		/// <summary>Finalize a Record (Record_Finalize). No-row default: everyone. A legacy Log is created already-final (Quick Entry parity).</summary>
		FinalizeRecords = 54,

		/// <summary>Open and finalize an amendment to a finalized Record (Record_Amend). No-row default: department and group admins.</summary>
		AmendRecords = 55,

		/// <summary>Submit a finalized revision to a reporting destination such as NERIS (Record_Submit). No-row default: department admins. LockToGroup is not meaningful (department-scoped).</summary>
		SubmitRecords = 56,

		/// <summary>Print/export Records (Record_Export). No-row default: everyone (per-record print/export is Logs parity). Bulk export honors the viewer's group scope regardless.</summary>
		ExportRecords = 57,

		/// <summary>Share one Record to another group or externally (Record_Share). No-row default: department admins.</summary>
		ShareRecordsExternally = 58,

		/// <summary>Read restricted sections: Coroner, casualty/exposure, investigation (RecordRestricted_View). No-row default: department admins. Never widened by group scope.</summary>
		ViewRestrictedRecords = 59,

		/// <summary>Read pre-cutover legacy Log/UnitLog history (RecordLegacy_View). No-row default: everyone (everyone holds Log:View today).</summary>
		ViewLegacyRecords = 60,

		/// <summary>Cross-group Record visibility control, mirroring ViewGroupUsers/ViewGroupUnits. Issues no claim; evaluated per Record at the service layer (AuthorizationService.CanUserViewRecordAsync). No-row default: everyone, not locked (department-wide, matching Logs today). RMS plan section 5.7.1.</summary>
		ViewGroupRecords = 61,

		/// <summary>Author department Record definitions (RecordDefinition_Update). No-row default: department admins. RMS-1B.</summary>
		ManageRecordDefinitions = 62,

		/// <summary>Publish/retire Record definition versions (RecordDefinition_Publish). No-row default: department admins. RMS-1B.</summary>
		PublishRecordDefinitions = 63,

		/// <summary>Manage saved Record reports (RecordReport_Update). No-row default: department admins. Report results still honor the runner's group scope. RMS-1B.</summary>
		ManageRecordReports = 64,

		/// <summary>Public-records / access-to-information disclosure workflow (RecordDisclosure_Update). No-row default: department admins, grantable onward to selected roles. RMS-3.</summary>
		ManageRecordDisclosures = 65,

		/// <summary>Place and release legal holds (RecordLegalHold_Update). No-row default: department admins, grantable onward. RMS-3.</summary>
		ManageRecordLegalHold = 66,

		/// <summary>Reassign an unfinalized draft to another author (Record_Reassign). No-row default: department and group admins.</summary>
		ReassignRecordDrafts = 67
	}

}
