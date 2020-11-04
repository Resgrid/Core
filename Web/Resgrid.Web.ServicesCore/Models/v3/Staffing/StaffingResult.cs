using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Staffing
{
	/// <summary>
	/// The result object for a state/staffing level request.
	/// </summary>
	public class StaffingResult
	{
		/// <summary>
		/// The UserId GUID/UUID for the user state/staffing level being return
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// The full name of the user for the state/staffing level being returned
		/// </summary>
		public string Nme { get; set; }

		/// <summary>
		/// The current staffing level (state) type for the user
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// The timestamp of the last state/staffing level. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Tms { get; set; }

		/// <summary>
		/// Staffing note for the User's staffing
		/// </summary>
		public string Not { get; set; }
	}
}
