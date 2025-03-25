using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Status
{
	/// <summary>
	/// The result object for a status request for V3 requests
	/// </summary>
	public class StatusResult
	{
		/// <summary>
		/// The UserId GUID/UUID for the user status being return
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// The current action/status type for the user
		/// </summary>
		public int Act { get; set; }

		/// <summary>
		/// The timestamp of the last action. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Ats { get; set; }

		/// <summary>
		/// The current staffing level (state) type for the user
		/// </summary>
		public int Ste { get; set; }

		/// <summary>
		/// The timestamp of the last state/staffing level. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Sts { get; set; }

		/// <summary>
		/// The current action/status destination id for the user
		/// </summary>
		public int Did { get; set; }
	}
}
