using System;

namespace Resgrid.Web.Areas.User.Models
{
	public class UserJson
	{
		public string UserId { get; set; }
		public string UserName { get; set; }
		public string Name { get; set; }
		public string Email { get; set; }
		public int DepartmentId { get; set; }
		public string DepartmentName { get; set; }
		public string Role { get; set; }
		public DateTime CreateDate { get; set; }
		public bool IsLockedOut { get; set; }
	}
}
