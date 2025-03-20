using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Workshifts
{
	public class ViewWorkshiftDayView
	{
		public Workshift Shift { get; set; }
		public WorkshiftDay Day { get; set; }
		public List<Unit> Units { get; set; }
		public List<UserGroupRole> Personnel { get; set; }
	}
}
