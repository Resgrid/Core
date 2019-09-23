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
		public SelectList ViewGroupUsersPermissions { get; set; }
	}
}
