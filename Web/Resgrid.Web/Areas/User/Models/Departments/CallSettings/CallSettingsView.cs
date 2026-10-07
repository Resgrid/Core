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

		// Call numbering (setting 115). The next-number row is the sequence a call logged now falls in.
		public string CallNumberPattern { get; set; }
		public int CallNumberSequenceWidth { get; set; }
		public string CallNumberScopeKey { get; set; }
		public string CallNumberNextNumber { get; set; }
		public int CallNumberCurrentNextSequence { get; set; }
		public CallNumberResetPeriod CallNumberResetPeriod { get; set; }
		public int? CallNumberNextSequence { get; set; }
		/// <summary>Department-local now, for the pattern preview.</summary>
		public System.DateTime CallNumberPreviewDate { get; set; }
		public string ErrorMessage { get; set; }
	}
}
