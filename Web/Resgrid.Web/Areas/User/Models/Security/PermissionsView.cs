using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Security
{
	public class PermissionsView
	{
		public int AddUsers { get; set; }
		public IEnumerable<SelectListItem> AddUserPermissions { get; set; }
		public int RemoveUsers { get; set; }
		public IEnumerable<SelectListItem> RemoveUserPermissions { get; set; }
		public int CreateCall { get; set; }
		public IEnumerable<SelectListItem> CreateCallPermissions { get; set; }

		public int CreateTraining { get; set; }
		public IEnumerable<SelectListItem> CreateTrainingPermissions { get; set; }

		public int CreateDocument { get; set; }
		public IEnumerable<SelectListItem> CreateDocumentPermissions { get; set; }

		public int CreateCalendarEntry { get; set; }
		public IEnumerable<SelectListItem> CreateCalendarEntryPermissions { get; set; }

		public int CreateNote { get; set; }
		public IEnumerable<SelectListItem> CreateNotePermissions { get; set; }

		public int CreateLog { get; set; }
		public IEnumerable<SelectListItem> CreateLogPermissions { get; set; }

		public int DeleteLog { get; set; }
		public IEnumerable<SelectListItem> DeleteLogPermissions { get; set; }

		public int CreateShift { get; set; }
		public IEnumerable<SelectListItem> CreateShiftPermissions { get; set; }

		public int ViewPersonalInfo { get; set; }
		public IEnumerable<SelectListItem> ViewPersonalInfoPermissions { get; set; }

		public int AdjustInventory { get; set; }
		public IEnumerable<SelectListItem> AdjustInventoryPermissions { get; set; }

		public int ViewPersonnelLocation { get; set; }
		public bool LockViewPersonneLocationToGroup { get; set; }
		public IEnumerable<SelectListItem> ViewPersonnelLocationPermissions { get; set; }

		public int ViewUnitLocation { get; set; }
		public bool LockViewUnitLocationToGroup { get; set; }
		public IEnumerable<SelectListItem> ViewUnitLocationPermissions { get; set; }

		public int CreateMessage { get; set; }
		public IEnumerable<SelectListItem> CreateMessagePermissions { get; set; }

		public int ViewGroupsUsers { get; set; }
		public bool LockViewGroupsUsersToGroup { get; set; }
		public IEnumerable<SelectListItem> ViewGroupUsersPermissions { get; set; }

		public int DeleteCall { get; set; }
		public bool LockDeleteCallToGroup { get; set; }
		public IEnumerable<SelectListItem> DeleteCallPermissions { get; set; }

		public int CloseCall { get; set; }
		public bool LockCloseCallToGroup { get; set; }
		public IEnumerable<SelectListItem> CloseCallPermissions { get; set; }

		public int FlagCallData { get; set; }
		public bool LockFlagCallDataToGroup { get; set; }
		public IEnumerable<SelectListItem> FlagCallDataPermissions { get; set; }

		public int AddCallData { get; set; }
		public bool LockAddCallDataToGroup { get; set; }
		public IEnumerable<SelectListItem> AddCallDataPermissions { get; set; }

		public int ViewGroupsUnits { get; set; }
		public bool LockViewGroupsUnitsToGroup { get; set; }
		public IEnumerable<SelectListItem> ViewGrouUnitsPermissions { get; set; }

		public int ViewContacts { get; set; }
		public IEnumerable<SelectListItem> ViewContactsPermissions { get; set; }

		public int EditContacts { get; set; }
		public IEnumerable<SelectListItem> EditContactsPermissions { get; set; }

		public int DeleteContacts { get; set; }
		public IEnumerable<SelectListItem> DeleteContactsPermissions { get; set; }

		public int CreateWorkflow { get; set; }
		public IEnumerable<SelectListItem> CreateWorkflowPermissions { get; set; }

		public int ManageWorkflowCredentials { get; set; }
		public IEnumerable<SelectListItem> ManageWorkflowCredentialsPermissions { get; set; }

		public int ViewWorkflowRuns { get; set; }
		public IEnumerable<SelectListItem> ViewWorkflowRunsPermissions { get; set; }

		public int UseCalendarSync { get; set; }
		public IEnumerable<SelectListItem> UseCalendarSyncPermissions { get; set; }

		public int DispatchAppLogin { get; set; }
		public IEnumerable<SelectListItem> DispatchAppLoginPermissions { get; set; }

		public int CommandAppLogin { get; set; }
		public IEnumerable<SelectListItem> CommandAppLoginPermissions { get; set; }

		// Advanced Data Protection (ADP) permissions. Defaults come from
		// Resgrid.Model.AdpPermissionDefaults — deliberately NOT the wide-open no-row convention. Only the values a
		// runtime check reads are on the screen (PermissionScreenCatalog).
		public int ViewProtectedCallData { get; set; }
		public IEnumerable<SelectListItem> ViewProtectedCallDataPermissions { get; set; }

		public int ConfigureProtectedDataEgress { get; set; }
		public IEnumerable<SelectListItem> ConfigureProtectedDataEgressPermissions { get; set; }

		// Records (RMS) permissions, PermissionTypes 50-67. Rows are generated from
		// Resgrid.Model.RecordPermissionCatalog; a missing row preselects the catalog no-row default,
		// which equals the pre-activation Logs behavior for the parity types.
		public List<RecordsPermissionRow> RecordsPermissions { get; set; } = new List<RecordsPermissionRow>();
		public bool RecordsFlagEnabled { get; set; }
		public bool RecordsActivated { get; set; }

		// Two-Factor Authentication enforcement
		public int Require2FAForAdmins { get; set; }
		public SelectList Require2FAForAdminsOptions { get; set; }
		public bool IsManagingUser { get; set; }

		// Guard: 2FA must be enabled on both the managing user and the current admin before enforcement can be turned on
		public bool ManagingUserHas2FAEnabled { get; set; }
		public bool CurrentUserHas2FAEnabled { get; set; }
		public bool Can2FAEnforcementBeChanged => ManagingUserHas2FAEnabled && CurrentUserHas2FAEnabled;
	}
}
