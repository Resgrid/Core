using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

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
		public List<Unit> Units { get; set; }
		public DepartmentGroup Station { get; set; }
		public string StartLat { get; set; }
		public string StartLon { get; set; }
		public string EndLat { get; set; }
		public string EndLon { get; set; }
		public List<PersonName> Names { get; set; }
		public List<CallReference> ChildCalls { get; set; }
		public List<Contact> Contacts { get; set; }
	}
}
