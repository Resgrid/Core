using System;

namespace Resgrid.Web.Areas.User.Models.Profile
{
	public class ScheduledTasksForJson
	{
		public int ScheduleId { get; set; }

		public string ScheduleType { get; set; }

		public bool IsActive { get; set; }

		public DateTime? SpecifcDate { get; set; }

		public string DaysOfWeek { get; set; }

		public string Data { get; set; }
	}
}