using System;

namespace Resgrid.Web.Services.Models.v4.UnitLocation
{
	/// <summary>
	/// A GPS location for a point in time of a specificed unit
	/// </summary>
	public class UnitLocationInput
	{
		/// <summary>
		/// UnitId of the apparatus that the location is for
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// The timestamp of the location in UTC
		/// </summary>
		public DateTime? Timestamp { get; set; }

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
	}
}
