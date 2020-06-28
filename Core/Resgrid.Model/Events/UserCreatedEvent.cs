using Resgrid.Model.Identity;

namespace Resgrid.Model.Events
{
	public class UserCreatedEvent
	{
		public string Name { get; set; }
		public int DepartmentId { get; set; }
		public IdentityUser User { get; set; }
	}
}
