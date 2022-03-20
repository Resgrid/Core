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
		/// Geolocation data "lat,lon"
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
		/// Comma seperated list of users,units,roles and groups to dipstach
		/// </summary>
		public string DispatchList { get; set; }

		/// <summary>
		/// Contact Name
		/// </summary>
		public string ContactName { get; set; }

		/// <summary>
		/// Contact Info
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
		/// Time in the future, in the departments local time, to dispatch the call
		/// </summary>
		public DateTime? DispatchOn { get; set; }

		/// <summary>
		/// Call Intake form JSON
		/// </summary>
		public string CallFormData { get; set; }
	}
}
