using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Units
{
	/// <summary>
	/// Object inputs for setting a users Status/Action. If this object is used in an operation that sets
	/// a status for the current user the UserId value in this object will be ignored.
	/// </summary>
	public class UnitStateInput
	{
		/// <summary>
		/// UnitId of the apparatus that the state is being set for
		/// </summary>
		public int Uid { get; set; }

		/// <summary>
		/// The UnitStateType of the Unit
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// The Call/Station the unit is responding to
		/// </summary>
		public int Rto { get; set; }

		/// <summary>
		/// The timestamp of the status event in UTC
		/// </summary>
		public DateTime? Tms { get; set; }

		/// <summary>
		/// The timestamp of the status event in the local time of the device
		/// </summary>
		public DateTime? Lts { get; set; }

		/// <summary>
		/// User provided note for this event
		/// </summary>
		public string Not { get; set; }

		/// <summary>
		/// GPS Latitude of the Unit
		/// </summary>
		public string Lat { get; set; }

		/// <summary>
		/// GPS Longitude of the Unit
		/// </summary>
		public string Lon { get; set; }

		/// <summary>
		/// GPS Latitude\Longitude Accuracy of the Unit
		/// </summary>
		public string Acc { get; set; }

		/// <summary>
		/// GPS Altitude of the Unit
		/// </summary>
		public string Alt { get; set; }

		/// <summary>
		/// GPS Altitude Accuracy of the Unit
		/// </summary>
		public string Alc { get; set; }

		/// <summary>
		/// GPS Speed of the Unit
		/// </summary>
		public string Spd { get; set; }

		/// <summary>
		/// GPS Heading of the Unit
		/// </summary>
		public string Hdn { get; set; }

		/// <summary>
		/// The event id used for queuing on mobile applications
		/// </summary>
		public int Eid { get; set; }

		/// <summary>
		/// The accountability roles filed for this event
		/// </summary>
		public List<Role> Roles { get; set; }
	}

	/// <summary>
	/// Role filled by a User on a Unit for an event
	/// </summary>
	[ProtoContract]
	public class Role
	{
		/// <summary>
		/// Id of the locally stored event
		/// </summary>
		[ProtoMember(1)]
		public int Id { get; set; }

		/// <summary>
		/// Local Event Id
		/// </summary>
		[ProtoMember(2)]
		public int Eid { get; set; }

		/// <summary>
		/// UserId of the user filling the role
		/// </summary>
		[ProtoMember(3)]
		public string Uid { get; set; }

		/// <summary>
		/// RoleId of the role being filled
		/// </summary>
		[ProtoMember(4)]
		public int Rid { get; set; }

		/// <summary>
		///  The name of the Role
		/// </summary>
		[ProtoMember(5)]
		public string Nme { get; set; }
	}
}
