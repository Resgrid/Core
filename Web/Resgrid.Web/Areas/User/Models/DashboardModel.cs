using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class DashboardModel : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		//public List<ActionLog> LastUserActionlogs { get; set; }
		//public List<Model.User> Users { get; set; } 
		public string StateNote { get; set; }
		public int UserState { get; set; }
		public SelectList UserStateTypes { get; set; }
		//public List<UserState> UserStates { get; set; }
		public List<DepartmentGroup> Stations { get; set; }
		//public UserStatusTableModel UserStatusTable { get; set; }
		public List<Call> Calls { get; set; }
		public bool FirstRun { get; set; }
		public string Number { get; set; }
		public UserStateTypes UserStateEnum { get; set; }
		public bool CustomStaffingActive { get; set; }
		public CustomState States { get; set; }
		public DashboardModel()
		{
			//UserStatusTable = new UserStatusTableModel();
			Stations = new List<DepartmentGroup>();
		}
	}
}
