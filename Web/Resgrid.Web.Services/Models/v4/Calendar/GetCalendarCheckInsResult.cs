using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calendar
{
	/// <summary>
	/// Result containing calendar check-in records
	/// </summary>
	public class GetCalendarCheckInsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CalendarCheckInResultData> Data { get; set; }
	}
}
