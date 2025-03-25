using System;

namespace Resgrid.Web.Services.Models.v4.PersonnelStaffing
{
	/// <summary>
	/// The result of getting the current staffing for a user
	/// </summary>
	public class GetCurrentStaffingResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetCurrentStaffingResultData Data { get; set; }
	}

	/// <summary>
	/// Information about a User staffing
	/// </summary>
	public class GetCurrentStaffingResultData
	{
		/// <summary>
		/// The UserId GUID/UUID for the user status being return
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// DepartmentId of the deparment the user belongs to
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// The current staffing type for the user
		/// </summary>
		public int StaffingType { get; set; }

		/// <summary>
		/// The timestamp of the last staffing. This is converted UTC version of the timestamp.
		/// </summary>
		public DateTime TimestampUtc { get; set; }

		/// <summary>
		/// The timestamp of the last staffing. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Timestamp { get; set; }

		/// <summary>
		/// Note for this staffing
		/// </summary>
		public string Note { get; set; }
	}
}
