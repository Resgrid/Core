using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Messages
{
	public class ViewMessageView: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Message Message { get; set; } 
		public int UnreadMessages { get; set; }

		/// <summary>True when this page is showing ADP placeholders that a grant could reveal.</summary>
		public bool IsProtectedMessage { get; set; }
		public List<UserGroupRole> UserGroupsAndRoles { get; set; }
		public bool CanRespondToCalendarRsvp { get; set; }
		public int? CalendarRsvpAttendeeType { get; set; }
		public bool HasModerationReport { get; set; }
		public int? ModerationStatus { get; set; }
		public string ModerationAdminNote { get; set; }
		public string ModerationMessage { get; set; }
	}
}
