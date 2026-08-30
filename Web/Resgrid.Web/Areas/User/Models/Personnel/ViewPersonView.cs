using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class ViewPersonView
	{
		public string UdfReadOnlyHtml { get; set; }

		/// <summary>True when this page is showing ADP placeholders that a grant could reveal.</summary>
		public bool IsProtectedRecord { get; set; }
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
