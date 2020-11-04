using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Stations
{
	/// <summary>
	/// A resrouce in the system this could be a user or unit
	/// </summary>
	public class StationResult
	{
		/// <summary>
		/// The Id value of the resource, it will be a Guid value for Users and an Int for all others
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// The type of the Resource (User or Unit)
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// The resource's display name
		/// </summary>
		//public string Nme { get; set; }

		/// <summary>
		/// The current action/status type for the resrouce
		/// </summary>
		public int Sts { get; set; }

		/// <summary>
		/// The timestamp of the last status. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Stm { get; set; }

		/// <summary>
		/// The current action/status destination id for the resrouce
		/// </summary>
		public int Did { get; set; }

		/// <summary>
		/// The current action/status destination name for the resrouce
		/// </summary>
		public string Dnm { get; set; }

		/// <summary>
		/// The current staffing level (state) type for the user (if the resrouce is a user)
		/// </summary>
		public int Ste { get; set; }

		/// <summary>
		/// The timestamp of the last state/staffing level. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Stt { get; set; }
	}
}
