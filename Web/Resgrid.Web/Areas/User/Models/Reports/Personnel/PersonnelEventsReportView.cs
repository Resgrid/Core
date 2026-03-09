using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Personnel;

namespace Resgrid.Web.Areas.User.Models.Reports.Personnel
{
	public class PersonnelEventsReportView
	{
		public Department Department { get; set; }
		public DateTime RunOn { get; set; }
		public List<PersonnelEventJson> Rows { get; set; }
	}
}
