using Resgrid.Model.Identity;

namespace Resgrid.Model.Events
{
	public class UserAssignedToGroupEvent
	{
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public IdentityUser User { get; set; }
		public DepartmentGroup PreviousGroup { get; set; }
		public DepartmentGroup Group { get; set; }
		public string UserId { get; set; }
	}
}
