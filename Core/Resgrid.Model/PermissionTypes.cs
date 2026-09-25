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
		/// Legacy stored permission identifier. Does not authorize staff support access; that flow
		/// is managed by BackOffice with customer consent, fresh MFA and independent approval.
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
		ReassignRecordDrafts = 67,

		/// <summary>
		/// Manage prevention data: occupancies, inspection programs and code sets, hydrants, permits and CRR
		/// (Record_PreventionAdmin). No-row default: department admins. RMS-5, registry value 69 (from the pool
		/// released on 2026-08-27; 68 is Unified Search's ManageSearchIndex). Reading prevention data needs only
		/// Record_View; investigations use ViewRestrictedRecords (59) plus case membership, never this value.
		/// </summary>
		RecordsPreventionAdmin = 69,

		ManageChecklists = 112,
ViewChecklistResults = 113,
		ManageWorkOrders = 114,
		ViewAllWorkOrders = 115,

		/// <summary>Transfer inventory between department locations. Defaults to the inventory adjustment permission.</summary>
		TransferInventory = 47,
		/// <summary>Issue and return department equipment. Defaults to the inventory adjustment permission.</summary>
		IssueInventory = 48,
		/// <summary>Record controlled-substance inventory transactions. Defaults to department administrators.</summary>
		ManageControlledSubstances = 49,

		/// <summary>Workforce &amp; Business Operations plan Phase B (registry 40): create/edit/send/void invoices, record payments, rate cards, billing profiles. Defaults to department administrators.</summary>
		ManageInvoicing = 40,
		/// <summary>Workforce &amp; Business Operations plan Phase B (registry 41): view invoices, aging and PDFs. Defaults to department administrators.</summary>
		ViewInvoicing = 41,

		/// <summary>Workforce &amp; Business Operations plan Phase D (registry 42): create/edit/delete/status/verify others' certification records and credits, and unit certification records. Defaults to department administrators.</summary>
		ManageCertifications = 42,
		/// <summary>Workforce &amp; Business Operations plan Phase D (registry 43): view others' certification records and the expiry dashboard; own records are always visible. Defaults to department administrators.</summary>
		ViewCertifications = 43,
		/// <summary>Workforce &amp; Business Operations plan Phase D (registry 44): certification types, role requirements and department certification settings. Defaults to department administrators.</summary>
		ManageCertificationSetup = 44,

		// -- Workforce & Business Operations Phase C (registry 116-119 + 79) ---------------------------------
		// 118-119 are wired (catalog, claims, policies, Security page) by the deployment core milestone; 116, 117
		// and 79 are declared so the values stay locked and gain their chain with the contractor-billing and
		// Cal OES MARS milestones.

		/// <summary>Phase C contractor billing (registry 116): create/edit/send/accept bids. Defaults to department administrators. Chain wired by the contractor milestone.</summary>
		ManageBids = 116,
		/// <summary>Phase C contractor billing (registry 117): service contracts, document requirements and compliance documents. Defaults to department administrators. Chain wired by the contractor milestone.</summary>
		ManageContracts = 117,
		/// <summary>Phase C deployment core (registry 118): create/edit deployments, roster, equipment, expenses, attachments and status transitions; view every deployment. Defaults to department administrators. Rostered members always see their own deployments and file time entries without it.</summary>
		ManageDeployments = 118,
		/// <summary>Phase C deployment core (registry 119): approve and void submitted daily time reports (TimeReports_Approve). Defaults to department administrators.</summary>
		ApproveTimeReports = 119,
		/// <summary>Phase C Cal OES MARS (registry 79): agency/rate/agreement management, portal handoff, external status observation and MARS invoice reconciliation. Defaults to department administrators. Chain wired by C-M3 (2026-09-19).</summary>
		ManageMutualAidReimbursement = 79,

		// -- Workforce & Business Operations Phase E (registry 74-78) ----------------------------------------
		// Protected workforce pay data, field costing and California pay data reporting (2026-09-19). All fall
		// back to department administrators; a member always sees and answers their own demographic response
		// and files their own resource usage without any of them.

		/// <summary>Phase E (registry 74): aggregate internal field-cost summaries and margins for bids, calls and deployments — categories and totals, never a line, rate or person. Defaults to department administrators.</summary>
		ViewInternalCosts = 74,
		/// <summary>Phase E (registry 75): employer identity, establishments, workers, employment periods, job assignments, compensation profiles, pay / employer-cost components and annual pay fact imports. Defaults to department administrators.</summary>
		ManageWorkforceCompensation = 75,
		/// <summary>Phase E (registry 76): read compensation profiles, work entries and cost-run lines (values still require a current Protected Data Grant). Defaults to department administrators.</summary>
		ViewWorkforceCompensation = 76,
		/// <summary>Phase E (registry 77): the California CRD report wizard — runs, employee snapshots, overrides, aggregation, validation, remarks, certification observation, corrections and the compliance officer's demographic records. Defaults to department administrators.</summary>
		ManagePayDataReporting = 77,
		/// <summary>Phase E (registry 78): freeze a validated run and download its export artifacts and portal worksheet. Defaults to department administrators.</summary>
		ExportPayDataReporting = 78
	}

}
