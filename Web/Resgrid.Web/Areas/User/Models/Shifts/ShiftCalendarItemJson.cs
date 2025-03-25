using System;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ShiftCalendarItemJson
	{
		public int CalendarItemId { get; set; }
		public string Title { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string Color { get; set; }
		public string StartTimezone { get; set; }
		public string EndTimezone { get; set; }
		public string Description { get; set; }
		public string RecurrenceId { get; set; }
		public string RecurrenceRule { get; set; }
		public string RecurrenceException { get; set; }
		public int? ItemType { get; set; }
		public int SignupType { get; set; }
		public bool IsAllDay { get; set; }
		public int ShiftId { get; set; }
		public string WorkshiftId { get; set; }
		public string WorkshiftDayId { get; set; }
		public bool Filled { get; set; }
		public bool UserSignedUp { get; set; }
		public List<ShiftGroupNeeds> Groups { get; set; }
		public List<ShiftUser> Users { get; set; }

		public ShiftCalendarItemJson()
		{
			Groups = new List<ShiftGroupNeeds>();
			Users = new List<ShiftUser>();
		}
	}

	public class ShiftGroupNeeds
	{
		public int ShiftGroupId { get; set; }
		public string Name { get; set; }
		public List<ShiftGroupNeedRole> Needs { get; set; }

		public ShiftGroupNeeds()
		{
			Needs = new List<ShiftGroupNeedRole>();
		}
	}

	public class ShiftGroupNeedRole
	{
		public int RoleId { get; set; }
		public string Name { get; set; }
		public int Needed { get; set; }
	}

	public class ShiftUser
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public bool IsYouOnShift { get; set; }
	}
}
