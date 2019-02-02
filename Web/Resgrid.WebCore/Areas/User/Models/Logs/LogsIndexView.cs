using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Logs
{
	public class LogsIndexView : BaseUserModel
	{
		public List<CallLog> CallLogs { get; set; }
		public List<Log> WorkLogs { get; set; }
		public Department Department { get; set; }
	}
}