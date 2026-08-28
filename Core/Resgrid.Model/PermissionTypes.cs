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
		BreakGlassProtectedData = 39
	}

}
