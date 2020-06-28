using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Departments.CallSettings
{
	public class CallSettingsView : BaseUserModel
	{
		public string Message { get; set; }
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public List<CallType> CallTypes { get; set; }
		public DepartmentCallEmail EmailSettings { get; set; }
		public string NewCallType { get; set; }
		public int CallType { get; set; }
		public bool PruneEmailCalls { get; set; }
		public int MinutesTillPrune { get; set; }
		public bool EnableTextToCall { get; set; }
		public string DepartmentTextToCallNumber { get; set; }
		public bool CanProvisionNumber { get; set; }
		public int TextCallType { get; set; }
		public string DepartmentTextToCallSourceNumbers { get; set; }
		public string InternalDispatchEmail { get; set; }
	}
}
