using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.PersonnelStaffing
{
	/// <summary>
	/// Saves (sets) and Personnel Status in the system, for a single user
	/// </summary>
	public class SavePersonnelStaffingsInput
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
		/// The event id used for queuing on mobile applications
		/// </summary>
		public string EventId { get; set; }
	}
}
