using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class ViewPersonView
	{
		public IdentityUser User { get; set; }
		public UserProfile Profile { get; set; }
		public DepartmentGroup Group { get; set; }
		public string Roles { get; set; }
		public UserState UserState { get; set; }
		public ActionLog ActionLog { get; set; }
		public Department Department { get; set; }
		public string State { get; set; }
	}
}
