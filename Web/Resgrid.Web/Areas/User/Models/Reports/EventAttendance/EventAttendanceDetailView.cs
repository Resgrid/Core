using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.EventAttendance
{
	public class EventAttendanceDetailView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string Name { get; set; }
		public string PersonnelName { get; set; }
		public List<EventAttendanceDetail> Details { get; set; }
		public double TotalSeconds { get; set; }

		public TimeSpan GetTotalTimeSpan()
		{
			return TimeSpan.FromSeconds(TotalSeconds);
		}
	}

	public class EventAttendanceDetail
	{
		public string EventTitle { get; set; }
		public DateTime CheckInTime { get; set; }
		public DateTime? CheckOutTime { get; set; }
		public double DurationSeconds { get; set; }
		public bool IsManualOverride { get; set; }
		public string CheckInNote { get; set; }
		public string CheckOutNote { get; set; }
		public string CheckInByName { get; set; }
		public string CheckOutByName { get; set; }
		public string CheckInLatitude { get; set; }
		public string CheckInLongitude { get; set; }
		public string CheckOutLatitude { get; set; }
		public string CheckOutLongitude { get; set; }

		public TimeSpan GetTimeSpan()
		{
			return TimeSpan.FromSeconds(DurationSeconds);
		}
	}
}
