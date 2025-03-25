using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.StaffingSchedules
{
	/// <summary>
	/// Describes a staffing schedule (automated staffing change) for a user
	/// </summary>
	public class StaffingScheduleResult
	{
		/// <summary>
		/// Id value of the staffing schedule
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Type of the Staffing Schedule (Date = 1, Weekly = 2)
		/// </summary>
		public string Typ { get; set; }

		/// <summary>
		/// Is the staffing schedule currently active
		/// </summary>
		public bool Act { get; set; }

		/// <summary>
		/// Specific date time the schedule triggers (if Typ = 1)
		/// </summary>
		public DateTime? Spc { get; set; }

		/// <summary>
		/// Days of the Week the Staffing Schedule triggers (if Type = 2)
		/// </summary>
		public string Dow { get; set; }

		/// <summary>
		/// What the staffing schedule will change to
		/// </summary>
		public string Data { get; set; }

		/// <summary>
		/// Note for this staffing schedule
		/// </summary>
		public string Not { get; set; }
	}
}