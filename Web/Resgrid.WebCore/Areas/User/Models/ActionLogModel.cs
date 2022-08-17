using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class PersonnelStatusHistoryView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string Name { get; set; }

		public List<PersonnelStatusSummary> Personnel { get; set; }

		public PersonnelStatusHistoryView()
		{
			Personnel = new List<PersonnelStatusSummary>();
		}
	}

	public class PersonnelStatusSummary
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int TotalStaffingChanges { get; set; }
		public int TotalActiveScheduledChanges { get; set; }

		public List<PersonnelStatusDetail> Details { get; set; }

		public PersonnelStatusSummary()
		{
			Details = new List<PersonnelStatusDetail>();
		}
	}

	public class PersonnelStatusDetail
	{
		public string Timestamp { get; set; }
		public string Status { get; set; }
		public string StatusColor { get; set; }
		public string Note { get; set; }
	}
}
