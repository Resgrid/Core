using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Groups
{
	public class DepartmentGroupsModel: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public bool CanAddNewGroup { get; set; }
		public List<DepartmentGroup> Groups { get; set; }

		public string Message { get; set; }
		public string ModalCssClass { get; set; }
		public DepartmentGroup NewGroup { get; set; }
		public DepartmentGroup EditGroup { get; set; }
		public string EditModalCssClass { get; set; }
		public string EditModalStyle { get; set; }

		public List<IdentityUser> Users { get; set; } 

		public DepartmentGroupsModel()
		{
			NewGroup = new DepartmentGroup();
			NewGroup.Type = 1;
			ModalCssClass = "hide";
		}
	}
}
