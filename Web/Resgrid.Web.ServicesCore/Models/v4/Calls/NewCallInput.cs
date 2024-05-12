using System;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Data needed to create a new call
	/// </summary>
	public class NewCallInput
	{
		/// <summary>
		/// Priority of the call
		/// </summary>
		[Required]
		public int Priority { get; set; }

		/// <summary>
		/// Name of the call
		/// </summary>
		[Required]
		public string Name { get; set; }

		/// <summary>
		/// Nature of the call
		/// </summary>
		[Required]
		public string Nature { get; set; }

		/// <summary>
		/// Dispatch note
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// Address
		/// </summary>
		public string Address { get; set; }

		/// <summary>
		/// Geolocation data "lat,lon" in decimal format
		/// </summary>
		public string Geolocation { get; set; }

		/// <summary>
		/// Type of the call
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// What 3 Words location
		/// </summary>
		public string What3Words { get; set; }

		/// <summary>
		/// Pipe separated string of users,units,roles and groups to dispatch. Users and Roles are prefixed with "P:" and "R:" respectively.
		/// Groups are prefixed with "G:" and Units are prefixed with "U:".
		/// </summary>
		/// <example>
		/// Say you want to dispatch 2 users, 1 unit, 1 group and 1 role. The string would look like this:
		/// P:c0a0f71a-0758-434c-868b-c77d245db78a|P:c0a0f71a-0758-434c-868b-c77d245db78a|U:1995012|G:991031|R:893139
		/// </example>
		public string DispatchList { get; set; }

		/// <summary>
		/// Contact Name
		/// </summary>
		public string ContactName { get; set; }

		/// <summary>
		/// Contact Info (phone number, email, etc)
		/// </summary>
		public string ContactInfo { get; set; }

		/// <summary>
		/// External Call Id
		/// </summary>
		public string ExternalId { get; set; }

		/// <summary>
		/// Incident Id
		/// </summary>
		public string IncidentId { get; set; }

		/// <summary>
		/// Reference Id
		/// </summary>
		public string ReferenceId { get; set; }

		/// <summary>
		/// Time in the future, in the departments local time, to dispatch the call. Leave NULL to dispatch now.
		/// </summary>
		public DateTime? DispatchOn { get; set; }

		/// <summary>
		/// Call Intake form JSON
		/// </summary>
		public string CallFormData { get; set; }
	}
}
