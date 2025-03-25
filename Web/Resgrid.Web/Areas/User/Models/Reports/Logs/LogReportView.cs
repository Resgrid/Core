using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Logs
{
	public class LogReportView
	{
		public DateTime RunOn { get; set; }
		public Log Log { get; set; }
		public float Attendance { get; set; }
		public Dictionary<string, Tuple<string, UserProfile>> UserData { get; set; }

		public LogReportView()
		{
			UserData = new Dictionary<string, Tuple<string, UserProfile>>();
		}
	}
}
