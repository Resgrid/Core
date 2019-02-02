using System;

namespace Resgrid.Web.Areas.User.Models.Departments.ActionLogs
{
	public class ActionLogForJson
	{
		public string Name { get; set; }
		public string Action { get; set; }
		public int ActionType { get; set; }
		public string Timestamp { get; set; }
	}
}