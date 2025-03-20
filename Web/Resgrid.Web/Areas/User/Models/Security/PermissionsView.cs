using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Security
{
	public class PermissionsView
	{
		public int AddUsers { get; set; }
		public SelectList AddUserPermissions { get; set; }
		public int RemoveUsers { get; set; }
		public SelectList RemoveUserPermissions { get; set; }
		public int CreateCall { get; set; }
		public SelectList CreateCallPermissions { get; set; }

		public int CreateTraining { get; set; }
		public SelectList CreateTrainingPermissions { get; set; }

		public int CreateDocument { get; set; }
		public SelectList CreateDocumentPermissions { get; set; }

		public int CreateCalendarEntry { get; set; }
		public SelectList CreateCalendarEntryPermissions { get; set; }

		public int CreateNote { get; set; }
		public SelectList CreateNotePermissions { get; set; }

		public int CreateLog { get; set; }
		public SelectList CreateLogPermissions { get; set; }

		public int CreateShift { get; set; }
		public SelectList CreateShiftPermissions { get; set; }

		public int ViewPersonalInfo { get; set; }
		public SelectList ViewPersonalInfoPermissions { get; set; }

		public int AdjustInventory { get; set; }
		public SelectList AdjustInventoryPermissions { get; set; }

		public int ViewPersonnelLocation { get; set; }
		public bool LockViewPersonneLocationToGroup { get; set; }
		public SelectList ViewPersonnelLocationPermissions { get; set; }

		public int ViewUnitLocation { get; set; }
		public bool LockViewUnitLocationToGroup { get; set; }
		public SelectList ViewUnitLocationPermissions { get; set; }

		public int CreateMessage { get; set; }
		public SelectList CreateMessagePermissions { get; set; }

		public int ViewGroupsUsers { get; set; }
		public bool LockViewGroupsUsersToGroup { get; set; }
		public SelectList ViewGroupUsersPermissions { get; set; }

		public int DeleteCall { get; set; }
		public bool LockDeleteCallToGroup { get; set; }
		public SelectList DeleteCallPermissions { get; set; }

		public int CloseCall { get; set; }
		public bool LockCloseCallToGroup { get; set; }
		public SelectList CloseCallPermissions { get; set; }

		public int FlagCallData { get; set; }
		public bool LockFlagCallDataToGroup { get; set; }
		public SelectList FlagCallDataPermissions { get; set; }

		public int AddCallData { get; set; }
		public bool LockAddCallDataToGroup { get; set; }
		public SelectList AddCallDataPermissions { get; set; }

		public int ViewGroupsUnits { get; set; }
		public bool LockViewGroupsUnitsToGroup { get; set; }
		public SelectList ViewGrouUnitsPermissions { get; set; }

		public int ViewContacts { get; set; }
		public SelectList ViewContactsPermissions { get; set; }

		public int EditContacts { get; set; }
		public SelectList EditContactsPermissions { get; set; }

		public int DeleteContacts { get; set; }
		public SelectList DeleteContactsPermissions { get; set; }
	}
}
