using System.Collections.Generic;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models.Logs
{
	public class LogsReportsView : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Dictionary<string, UserReport> Report { get; set; }
	}

	public class UserReport
	{
		public string Name { get; set; }
		public int Hours { get; set; }
	}
}