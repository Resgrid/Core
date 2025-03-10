namespace Resgrid.Providers.Claims
{
	public static class ResgridClaimTypes
	{
		public static class Actions
		{
			// Basic Actions
			public const string View = "View";
			public const string Create = "Create";
			public const string Update = "Update";
			public const string Delete = "Delete";
		}

		public static class Memberships
		{
			// Memberships
			public const string Departments = "DepartmentMemberships";
			public const string Groups = "GroupMemberships";
		}

		public static class Data
		{
			// Memberships
			public const string TimeZone = "TimeZone";
			public const string DisplayName = "DisplayName";
			public const string UserId = "UserId";
		}

		public static class Resources
		{
			// Resources
			public const string Department = "Department";
			public const string Personnel = "Personnel";
			public const string Call = "Call";
			public const string Log = "Log";
			public const string Action = "Action";
			public const string Staffing = "Staffing";
			public const string Unit = "Unit";
			public const string Group = "Group";
			public const string UnitLog = "UnitLog";
			public const string Messages = "Messages";
			public const string Role = "Role";
			public const string Profile = "Profile";
			public const string Reports = "Reports";
			public const string GenericGroup = "GenericGroup";
			public const string Documents = "Documents";
			public const string Notes = "Notes";
			public const string Schedule = "Schedule";
			public const string Shift = "Shift";
			public const string Training = "Training";
			public const string PersonalInfo = "PII";
			public const string Inventory = "Inventory";
			public const string Command = "Command";
			public const string Connect = "Connect";
			public const string Protocols = "Protocols";
			public const string Forms = "Forms";
			public const string Voice = "Voice";
			public const string CustomStates = "CustomStates";
			public const string Contacts = "Contacts";
		}

		public static string CreateDepartmentClaimTypeString(int departmentId)
		{
			//return string.Format("{0}/{1}", Resources.Department, departmentId);
			return Resources.Department;
		}

		public static string CreateGroupClaimTypeString(int groupId)
		{
			return string.Format("{0}/{1}", Resources.Group, groupId);
		}
	}
}
