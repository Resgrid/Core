using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Personnel
{
	public class PersonnelStaffingHistoryView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string Name { get; set; }

		public List<PersonnelStaffingSummary> Personnel { get; set; }

		public PersonnelStaffingHistoryView()
		{
			Personnel = new List<PersonnelStaffingSummary>();
		}
	}

	public class PersonnelStaffingSummary
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int TotalStaffingChanges { get; set; }
		public int TotalActiveScheduledChanges { get; set; }

		public List<PersonnelStaffingDetail> Details { get; set; }

		public PersonnelStaffingSummary()
		{
			Details = new List<PersonnelStaffingDetail>();
		}
	}

	public class PersonnelStaffingDetail
	{
		public string Timestamp { get; set; }
		public string State { get; set; }
		public string StateColor { get; set; }
		public string Note { get; set; }
	}
}