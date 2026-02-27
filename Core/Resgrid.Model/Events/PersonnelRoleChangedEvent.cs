namespace Resgrid.Model.Events
{
	public class PersonnelRoleChangedEvent
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public int PersonnelRoleId { get; set; }
		public string RoleName { get; set; }
		public string RoleDescription { get; set; }
		/// <summary>"Added" or "Removed"</summary>
		public string Action { get; set; }
	}
}

