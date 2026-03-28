using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.EventAttendance
{
	public class EventAttendanceView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string Name { get; set; }
		public List<PersonnelEventHours> EventHours { get; set; }
	}

	public class PersonnelEventHours
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int TotalEvents { get; set; }
		public int MissedEvents { get; set; }
		public double TotalSeconds { get; set; }

		public TimeSpan GetTimeSpan()
		{
			return TimeSpan.FromSeconds(TotalSeconds);
		}
	}
}
