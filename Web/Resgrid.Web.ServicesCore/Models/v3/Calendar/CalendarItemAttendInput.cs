using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calendar
{
	public class CalendarItemAttendInput
	{
		/// <summary>
		/// 
		/// </summary>
		public int CalId { get; set; }

		/// <summary>
		/// 
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// 1 = Attending, 4 = Not Attending
		/// </summary>
		public int Type { get; set; }
	}
}