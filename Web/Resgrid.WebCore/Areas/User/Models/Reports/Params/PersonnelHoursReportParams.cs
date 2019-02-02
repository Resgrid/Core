using System;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Reports.Params
{
	public class PersonnelHoursReportParams
	{
		public string UserId { get; set; }
		public SelectList Users { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
	}
}