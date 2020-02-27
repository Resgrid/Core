using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	public class DepartmentCallDispatchRoleResult
	{
		public int CallDispatchRoleId { get; set; }
		public int CallId { get; set; }
		public int RoleId { get; set; }
		public int DispatchCount { get; set; }
		public DateTime? LastDispatchedOn { get; set; }
	}
}
