using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	public class CloseCallView: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public int CallId { get; set; }
		public string ClosedCallNotes { get; set; }
		public ClosedOnlyCallStates CallState { get; set; }
		public SelectList CallStates { get; set; }
		public bool SendNotification { get; set; }

		/// <summary>The call has an active incident command, which has to be closed before the call can be.</summary>
		public bool IsBlockedByIncidentCommand { get; set; }
	}
}
