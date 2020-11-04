using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class UserStatusTableModel
	{
		public Department Department { get; set; }
		public List<DepartmentGroup> DepartmentGroups { get; set; }
		public List<string> UnGroupedUsers { get; set; }
		public List<UserState> UserStates { get; set; }
		public IDictionary<string, UserState> DepartmentUserStates { get; set; }
		public IDictionary<string, string> Names { get; set; }
		public List<ActionLog> LastUserActionlogs { get; set; }
		public List<DepartmentGroup> Stations { get; set; }
		public List<string> ExcludedUsers { get; set; }
		public IDictionary<string, string> Roles { get; set; }
		public DepartmentGroup UsersGroup { get; set; }
		public CustomState States { get; set; }
		public CustomState StaffingLevels { get; set; }


		public List<UserStatusGroup> UserStatusGroups {get;set;}


		public UserStatusTableModel()
		{
			DepartmentGroups = new List<DepartmentGroup>();
			UnGroupedUsers = new List<string>();
			DepartmentUserStates = new Dictionary<string, UserState>();
			LastUserActionlogs = new List<ActionLog>();
			Names = new Dictionary<string, string>();
			Roles = new Dictionary<string, string>();

			UserStatusGroups = new List<UserStatusGroup>();
		}
	}

	public class UserStatusGroup
	{
		public DepartmentGroup Group { get; set; }

		public List<UserStatusGroup> ChildGroups { get; set; }

		public List<UserStatus> UserStatuses { get; set; }

		public UserStatusGroup()
		{
			ChildGroups = new List<UserStatusGroup>();
			UserStatuses = new List<UserStatus>();
		}
	}

	public class UserStatus
	{
		public int Weight { get; set; }
		public UserGroupRole UserInfo {get; set; }
		public ActionLog CurrentStatus { get; set; }
		public UserState CurrentStaffing { get; set; }
		public string Eta { get; set; }
	}
}
