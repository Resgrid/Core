using System;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Reports.Params
{
	public class CallSummaryReportParams
	{
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		/// <summary>0 for every group (the whole department).</summary>
		public int GroupId { get; set; }
		public SelectList Groups { get; set; }
	}
}
