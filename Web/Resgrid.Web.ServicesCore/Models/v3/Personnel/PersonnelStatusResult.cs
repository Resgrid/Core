using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Personnel
{
	/// <summary>
	/// A Personnel status result object
	/// </summary>
	public class PersonnelStatusResult
	{
		/// <summary>
		/// The UserId GUID/UUID for the user state/staffing level being return
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// The current action/status type for the user
		/// </summary>
		public int Atp { get; set; }

		/// <summary>
		/// The timestamp of the last action. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Atm { get; set; }

		/// <summary>
		/// The current action/status destination id for the user
		/// </summary>
		public string Did { get; set; }

		/// <summary>
		/// The current staffing level (state) type for the user
		/// </summary>
		public int Ste { get; set; }

		/// <summary>
		/// The timestamp of the last state/staffing level. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Stm { get; set; }

		/// <summary>
		/// The timestamp of the last state level in UTC.
		/// </summary>
		public DateTime AUtc { get; set; }

		/// <summary>
		/// The timestamp of the last staffing level in UTC.
		/// </summary>
		public DateTime SUtc { get; set; }

		/// <summary>
		/// Group Id for the user, 0 if not in a group
		/// </summary>
		public int Gid { get; set; }

		/// <summary>
		/// Sorting weight
		/// </summary>
		public int Weight { get; set; }
	}
}
