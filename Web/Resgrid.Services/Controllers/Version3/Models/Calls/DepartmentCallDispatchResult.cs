using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	public class DepartmentCallDispatchResult
	{
		public int CallDispatchId { get; set; }
		public int CallId { get; set; }
		public string UserId { get; set; }
		public int? GroupId { get; set; }
		public int DispatchCount { get; set; }
		public DateTime? LastDispatchedOn { get; set; }
		public int? ActionLogId { get; set; }
	}
}
