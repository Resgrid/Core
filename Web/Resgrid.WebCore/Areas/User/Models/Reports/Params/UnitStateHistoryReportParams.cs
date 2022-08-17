using System;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Reports.Params
{
	public class UnitStateHistoryReportParams
	{
		public int UnitId { get; set; }
		public SelectList Units { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public bool GroupSelect { get; set; }
		public int GroupId { get; set; }
		public SelectList Groups { get; set; }
	}
}
