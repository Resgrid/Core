using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	public class DepartmentCallDispatchGroupResult
	{
		public int CallDispatchGroupId { get; set; }
		public int CallId { get; set; }
		public int DepartmentGroupId { get; set; }
		public int DispatchCount { get; set; }
		public DateTime? LastDispatchedOn { get; set; }
	}
}
