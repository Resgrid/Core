using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Units;

namespace Resgrid.Web.Areas.User.Models.Reports.Units
{
	public class UnitEventsReportView
	{
		public Department Department { get; set; }
		public DateTime RunOn { get; set; }
		public List<UnitEventJson> Rows { get; set; }
	}
}