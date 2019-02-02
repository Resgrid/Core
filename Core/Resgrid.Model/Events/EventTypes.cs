using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model.Events
{
	public enum EventTypes
	{
		[Display(Name = "Unit Status Changed")]
		UnitStatusChanged			= 0,

		[Display(Name = "Personnel Staffing Changed")]
		PersonnelStaffingChanged	= 1,

		[Display(Name = "Personnel Status Changed")]
		PersonnelStatusChanged		= 2,

		[Display(Name = "User Created")]
		UserCreated					= 3,

		[Display(Name = "User Assigned To Group")]
		UserAssignedToGroup			= 4,

		[Display(Name = "Calendar Event Upcoming")]
		CalendarEventUpcoming		= 5,

		[Display(Name = "Document Added")]
		DocumentAdded				= 6,

		[Display(Name = "Note Added")]
		NoteAdded					= 7,

		[Display(Name = "Unit Added")]
		UnitAdded					= 8,

		[Display(Name = "Log Added")]
		LogAdded					= 9,

		[Display(Name = "Department Settings Changed")]
		DepartmentSettingsChanged	= 10,

		[Display(Name = "Roles for Group Availability Alert")]
		RolesInGroupAvailabilityAlert = 11,

		[Display(Name = "Unit Types for Group Availability Alert")]
		UnitTypesInGroupAvailabilityAlert = 12,

		[Display(Name = "Roles for Department Availability Alert")]
		RolesInDepartmentAvailabilityAlert = 13,

		[Display(Name = "Unit Types for Department Availability Alert")]
		UnitTypesInDepartmentAvailabilityAlert = 14,

		[Display(Name = "Calendar Event Added")]
		CalendarEventAdded = 15,

		[Display(Name = "Calendar Event Updated")]
		CalendarEventUpdated = 16,

		[Display(Name = "Shift Created")]
		ShiftCreated = 17,

		[Display(Name = "Shift Updated")]
		ShiftUpdated = 18,

		[Display(Name = "Shift Days Added")]
		ShiftDaysAdded = 19,

		[Display(Name = "Linked Department Call Added")]
		LinkedDepartmentCallAdded = 20,

		[NotMapped]
		ResourceOrderAdded = 21
	}

	public static class EventOptions
	{
		public static HashSet<EventTypes> GroupEvents = new HashSet<EventTypes>()
		{
			EventTypes.UnitStatusChanged,
			EventTypes.PersonnelStatusChanged,
			EventTypes.PersonnelStaffingChanged,
			EventTypes.UserAssignedToGroup,
			EventTypes.RolesInGroupAvailabilityAlert,
			EventTypes.UnitTypesInGroupAvailabilityAlert
		};

		public static HashSet<EventTypes> PreviousDataEnabled = new HashSet<EventTypes>()
		{
			EventTypes.UnitStatusChanged,
			EventTypes.PersonnelStatusChanged,
			EventTypes.PersonnelStaffingChanged
		};

		public static HashSet<EventTypes> PreviousDataMultiple = new HashSet<EventTypes>()
		{
			
		};

		public static HashSet<EventTypes> CurrentDataEnabled = new HashSet<EventTypes>()
		{
			EventTypes.UnitStatusChanged,
			EventTypes.PersonnelStatusChanged,
			EventTypes.PersonnelStaffingChanged
		};

		public static HashSet<EventTypes> CurrentDataMultiple = new HashSet<EventTypes>()
		{
			EventTypes.RolesInGroupAvailabilityAlert,
			EventTypes.UnitTypesInGroupAvailabilityAlert,
			EventTypes.RolesInDepartmentAvailabilityAlert,
			EventTypes.UnitTypesInDepartmentAvailabilityAlert
		};

		public static HashSet<EventTypes> UpperLimitEnabled = new HashSet<EventTypes>()
		{
			
		};

		public static HashSet<EventTypes> LowerEnabled = new HashSet<EventTypes>()
		{
			EventTypes.RolesInGroupAvailabilityAlert,
			EventTypes.UnitTypesInGroupAvailabilityAlert,
			EventTypes.RolesInDepartmentAvailabilityAlert,
			EventTypes.UnitTypesInDepartmentAvailabilityAlert
		};

		public static HashSet<EventTypes> AlertableEvents = new HashSet<EventTypes>()
		{
			EventTypes.RolesInGroupAvailabilityAlert,
			EventTypes.UnitTypesInGroupAvailabilityAlert,
			EventTypes.RolesInDepartmentAvailabilityAlert,
			EventTypes.UnitTypesInDepartmentAvailabilityAlert
		};

		public static HashSet<EventTypes> HiddenEventTypes = new HashSet<EventTypes>()
		{
			EventTypes.CalendarEventAdded,
			EventTypes.CalendarEventUpcoming,
			EventTypes.CalendarEventUpdated,
			EventTypes.ShiftCreated,
			EventTypes.ShiftUpdated,
			EventTypes.ShiftDaysAdded
		};
	}
}