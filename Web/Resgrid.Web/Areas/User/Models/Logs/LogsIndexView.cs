using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Logs
{
	public class LogsIndexView : BaseUserModel
	{
		public List<CallLog> CallLogs { get; set; }
		public List<Log> WorkLogs { get; set; }
		public Department Department { get; set; }
		public string Year { get; set; }
		public List<SelectListItem> Years { get; set; }

		/// <summary>True after Records activation: the module lists, prints and exports, but every create/edit/delete path is denied.</summary>
		public bool LegacyReadOnly { get; set; }
	}
}
