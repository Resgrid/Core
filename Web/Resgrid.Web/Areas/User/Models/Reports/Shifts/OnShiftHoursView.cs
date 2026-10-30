using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Shifts
{
	/// <summary>What the on-shift hours report covers.</summary>
	public enum OnShiftHoursScopes
	{
		Department = 0,
		Group = 1,
		Shift = 2
	}

	public class OnShiftHoursReportParams
	{
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public OnShiftHoursScopes Scope { get; set; }

		public int GroupId { get; set; }

		/// <summary>"s:{shiftId}" for an assigned or signup shift, "w:{workshiftId}" for a static shift.</summary>
		public string Shift { get; set; }

		public SelectList Groups { get; set; }

		public List<SelectListItem> Shifts { get; set; } = new List<SelectListItem>();
	}

	public class OnShiftHoursView
	{
		public Department Department { get; set; }
		public DateTime RunOn { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public OnShiftHoursScopes Scope { get; set; }

		/// <summary>The department, group or shift the report covers.</summary>
		public string ScopeName { get; set; }

		/// <summary>Query values that reproduce this report (for the CSV link).</summary>
		public int GroupId { get; set; }
		public string Shift { get; set; }

		public List<OnShiftHoursPersonRow> People { get; set; } = new List<OnShiftHoursPersonRow>();

		public List<OnShiftHoursOccurrenceRow> Occurrences { get; set; } = new List<OnShiftHoursOccurrenceRow>();

		public double TotalHours => People.Sum(x => x.TotalHours);
		public double ManualHours => People.Sum(x => x.ManualHours);
		public double AutomaticHours => People.Sum(x => x.AutomaticHours);
	}

	public class OnShiftHoursPersonRow
	{
		public string UserId { get; set; }
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int ShiftCount { get; set; }
		public int ManualShiftCount { get; set; }
		public double TotalHours { get; set; }
		public double ManualHours { get; set; }
		public double AutomaticHours { get; set; }
		public double AssignedHours { get; set; }
		public double SignupHours { get; set; }
		public double StaticHours { get; set; }

		public List<OnShiftHoursEntryRow> Entries { get; set; } = new List<OnShiftHoursEntryRow>();

		/// <summary>Every counted hour comes from approved staffing.</summary>
		public bool AllManual => ShiftCount > 0 && ManualShiftCount == ShiftCount;

		/// <summary>Some, but not all, counted hours come from approved staffing.</summary>
		public bool Mixed => ManualShiftCount > 0 && ManualShiftCount < ShiftCount;
	}

	public class OnShiftHoursEntryRow
	{
		public string ShiftName { get; set; }
		public StaffedShiftTypes ShiftType { get; set; }
		public string TypeName { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public double Hours { get; set; }
		public bool IsPartial { get; set; }
		public ShiftStaffingSources Source { get; set; }

		/// <summary>The group slot, or for a static shift the unit, the person filled.</summary>
		public string Assignment { get; set; }
	}

	public class OnShiftHoursOccurrenceRow
	{
		public DateTime Day { get; set; }
		public string Name { get; set; }
		public StaffedShiftTypes ShiftType { get; set; }
		public string TypeName { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public double Hours { get; set; }
		public int People { get; set; }
		public ShiftStaffingSources Source { get; set; }
		public string SetBy { get; set; }
		public DateTime? SetOn { get; set; }
		public int Added { get; set; }
		public int Removed { get; set; }
		public bool IsOver { get; set; }

		/// <summary>Opens the day on the Shift Staffing page.</summary>
		public int? ShiftId { get; set; }
		public string WorkshiftId { get; set; }
	}
}
