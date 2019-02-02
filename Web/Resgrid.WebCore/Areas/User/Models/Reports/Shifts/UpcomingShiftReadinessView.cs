using System;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Reports.Shifts
{
	public class UpcomingShiftReadinessView
	{
		public DateTime RunOn { get; set; }
		public List<UpcomingShiftReadinessReportRow> Rows { get; set; }
	}

	public class UpcomingShiftReadinessReportRow
	{
		public string ShiftName { get; set; }
		public string ShiftDate { get; set; }
		public string Type { get; set; }
		public bool Ready { get; set; }
		public List<UpcomingShiftReadinessReportSubRow> SubRows { get; set; }

		public UpcomingShiftReadinessReportRow()
		{
			SubRows = new List<UpcomingShiftReadinessReportSubRow>();
		}
	}

	public class UpcomingShiftReadinessReportSubRow
	{
		public string GroupName { get; set; }
		public List<UpcomingShiftReadinessPersonnel> Personnel { get; set; }
		public List<UpcomingShiftReadinessGroupRole> Roles { get; set; }

		public UpcomingShiftReadinessReportSubRow()
		{
			Personnel = new List<UpcomingShiftReadinessPersonnel>();
			Roles = new List<UpcomingShiftReadinessGroupRole>();
		}
	}

	public class UpcomingShiftReadinessPersonnel
	{
		public string Name { get; set; }
		public List<string> Roles { get; set; }

		public UpcomingShiftReadinessPersonnel()
		{
			Roles = new List<string>();
		}
	}

	public class UpcomingShiftReadinessGroupRole
	{
		public string Name { get; set; }
		public int Required { get; set; }
		public int Optional { get; set; }
		public int Delta { get; set; }
	}
}