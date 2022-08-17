using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Shifts
{
	/// <summary>
	/// Shifts for a department
	/// </summary>
	public class ShiftsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<ShiftsResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ShiftsResult()
		{
			Data = new List<ShiftsResultData>();
		}
	}

	/// <summary>
	/// Shift result data
	/// </summary>
	public class ShiftsResultData
	{
		/// <summary>
		/// Shift Identifier
		/// </summary>
		public string ShiftId { get; set; }

		/// <summary>
		/// Name of the Shift
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Shift Code
		/// </summary>
		public string Code { get; set; }

		/// <summary>
		/// Color of the Shift
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// Type of Schedule for this shift
		/// </summary>
		public int ScheduleType { get; set; }

		/// <summary>
		/// What type of shift assignment
		/// </summary>
		public int AssignmentType { get; set; }

		/// <summary>
		/// Is User in this Shift
		/// </summary>
		public bool InShift { get; set; }

		/// <summary>
		/// Personnel Count
		/// </summary>
		public int PersonnelCount { get; set; }

		/// <summary>
		/// Group Count
		/// </summary>
		public int GroupCount { get; set; }

		/// <summary>
		/// Next shift day
		/// </summary>
		public string NextDay { get; set; }

		/// <summary>
		/// Next shift day id
		/// </summary>
		public string NextDayId { get; set; }

		/// <summary>
		/// Days for the shift (this may be null)
		/// </summary>
		public List<ShiftDayResultData> Days { get; set; }
	}
}
