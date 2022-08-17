using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.PersonnelStatuses
{
	/// <summary>
	/// Saves (sets) and Personnel Status in the system, for a single user
	/// </summary>
	public class SavePersonsStatusesInput
	{
		/// <summary>
		/// UnitId of the apparatus that the state is being set for
		/// </summary>
		[Required]
		public List<string> UserIds { get; set; }

		/// <summary>
		/// The UnitStateType of the Unit
		/// </summary>
		[Required]
		public string Type { get; set; }

		/// <summary>
		/// The Call/Station the unit is responding to
		/// </summary>
		public string RespondingTo { get; set; }

		/// <summary>
		/// The timestamp of the status event in UTC
		/// </summary>
		public DateTime? TimestampUtc { get; set; }

		/// <summary>
		/// The timestamp of the status event in the local time of the device
		/// </summary>
		public DateTime? Timestamp { get; set; }

		/// <summary>
		/// User provided note for this event
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// GPS Latitude of the Unit
		/// </summary>
		public string Latitude { get; set; }

		/// <summary>
		/// GPS Longitude of the Unit
		/// </summary>
		public string Longitude { get; set; }

		/// <summary>
		/// GPS Latitude\Longitude Accuracy of the Unit
		/// </summary>
		public string Accuracy { get; set; }

		/// <summary>
		/// GPS Altitude of the Unit
		/// </summary>
		public string Altitude { get; set; }

		/// <summary>
		/// GPS Altitude Accuracy of the Unit
		/// </summary>
		public string AltitudeAccuracy { get; set; }

		/// <summary>
		/// GPS Speed of the Unit
		/// </summary>
		public string Speed { get; set; }

		/// <summary>
		/// GPS Heading of the Unit
		/// </summary>
		public string Heading { get; set; }

		/// <summary>
		/// The event id used for queuing on mobile applications
		/// </summary>
		public string EventId { get; set; }
	}
}
