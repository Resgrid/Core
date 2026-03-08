using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Logs
{
	public class ViewLogsView : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public CallLog CallLog { get; set; }
		public Log WorkLog { get; set; }
		public List<LogAttachment> Attachments { get; set; }
		public List<DepartmentGroup> Groups { get; set; }
		public List<Unit> Units { get; set; }
		public Dictionary<string, string> PersonnelNames { get; set; }
		public bool CanDelete { get; set; }

		public ViewLogsView()
		{
			Attachments = new List<LogAttachment>();
			Groups = new List<DepartmentGroup>();
			Units = new List<Unit>();
			PersonnelNames = new Dictionary<string, string>();
		}
	}
}
