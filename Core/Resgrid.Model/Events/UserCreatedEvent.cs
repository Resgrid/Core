using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model.Events
{
	public class UserCreatedEvent
	{
		public string Name { get; set; }
		public int DepartmentId { get; set; }
		public IdentityUser User { get; set; }
	}
}