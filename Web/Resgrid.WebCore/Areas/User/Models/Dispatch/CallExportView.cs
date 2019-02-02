using System.Collections.Generic;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models.Dispatch
{
	public class CallExportView : BaseUserModel
	{
		public string Message { get; set; }
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Call Call { get; set; }
		public List<CallLog> CallLogs { get; set; }
		public List<UnitState> UnitStates { get; set; } 
		public List<ActionLog> ActionLogs { get; set; } 
		public List<DepartmentGroup> Groups { get; set; }
	}
}