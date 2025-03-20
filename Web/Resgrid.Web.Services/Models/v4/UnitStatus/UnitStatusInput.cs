using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.UnitStatus
{
	/// <summary>
	/// Object inputs for setting a users Status/Action. If this object is used in an operation that sets
	/// a status for the current user the UserId value in this object will be ignored.
	/// </summary>
	public class UnitStatusInput
	{
		/// <summary>
		/// UnitId of the apparatus that the state is being set for
		/// </summary>
		[Required]
		public string Id { get; set; }

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

		/// <summary>
		/// The accountability roles filed for this event
		/// </summary>
		public List<UnitStatusRoleInput> Roles { get; set; }
	}

	/// <summary>
	/// Role filled by a User on a Unit for an event
	/// </summary>
	public class UnitStatusRoleInput
	{
		/// <summary>
		/// Id of the locally stored event
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Local Event Id
		/// </summary>
		public string EventId { get; set; }

		/// <summary>
		/// UserId of the user filling the role
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// RoleId of the role being filled
		/// </summary>
		public string RoleId { get; set; }

		/// <summary>
		///  The name of the Role
		/// </summary>
		public string Name { get; set; }
	}
}
