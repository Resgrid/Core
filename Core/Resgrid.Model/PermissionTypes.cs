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
		CommandAppLogin = 30
	}

}
