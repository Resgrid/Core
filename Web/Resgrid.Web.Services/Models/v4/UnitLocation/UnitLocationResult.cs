using System;

namespace Resgrid.Web.Services.Models.v4.UnitLocation
{
	/// <summary>
	/// A unit location in the Resgrid system
	/// </summary>
	public class UnitLocationResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public UnitLocationResultData Data { get; set; }
	}

	/// <summary>
	/// The information about a specific unit's location
	/// </summary>
	public class UnitLocationResultData
	{
		/// <summary>
		/// Id of the Unit
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// The Timestamp for the location in UTC
		/// </summary>
		public DateTime Timestamp { get; set; }

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
