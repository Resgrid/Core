using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Personnel
{
	/// <summary>
	/// Information about a User
	/// </summary>
	public class PersonnelInfoResult
	{
		/// <summary>
		/// The UserId GUID/UUID for the user
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// DepartmentId of the deparment the user belongs to
		/// </summary>
		public int Did { get; set; }

		/// <summary>
		/// Department specificed ID number for this user
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// The Users First Name
		/// </summary>
		public string Fnm { get; set; }

		/// <summary>
		/// The Users Last Name
		/// </summary>
		public string Lnm { get; set; }

		/// <summary>
		/// The Users Email Address
		/// </summary>
		public string Eml { get; set; }

		/// <summary>
		/// The Users Mobile Telephone Number
		/// </summary>
		public string Mnu { get; set; }

		/// <summary>
		/// GroupId the user is assigned to (0 for no group)
		/// </summary>
		public int Gid { get; set; }

		/// <summary>
		/// Name of the group the user is assigned to
		/// </summary>
		public string Gnm { get; set; }

		/// <summary>
		/// Enumeration/List of roles the user currently holds
		/// </summary>
		public List<string> Roles { get; set; }

		/// <summary>
		/// The current action/status type for the user
		/// </summary>
		public int Ats { get; set; }

		/// <summary>
		/// The timestamp of the last action. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Atm { get; set; }

		/// <summary>
		/// The current action/status destination id for the user
		/// </summary>
		public string Dsi { get; set; }

		/// <summary>
		/// The current action/status destination name for the user
		/// </summary>
		public string Dsn { get; set; }

		/// <summary>
		/// The current staffing level (state) type for the user
		/// </summary>
		public int Stf { get; set; }

		/// <summary>
		/// The timestamp of the last state/staffing level. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Stm { get; set; }
	}
}
