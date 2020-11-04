using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.UnitLocation
{
	/// <summary>
	/// A GPS location for a point in time of a specificed unit
	/// </summary>
	public class UnitLocationInput
	{
		/// <summary>
		/// UnitId of the apparatus that the location is for
		/// </summary>
		public int Uid { get; set; }

		/// <summary>
		/// The timestamp of the location in UTC
		/// </summary>
		public DateTime? Tms { get; set; }

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
	}
}
