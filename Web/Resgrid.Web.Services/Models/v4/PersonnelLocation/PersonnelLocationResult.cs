using System;

namespace Resgrid.Web.Services.Models.v4.PersonnelLocation
{
	/// <summary>
	/// A unit location in the Resgrid system
	/// </summary>
	public class PersonnelLocationResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public PersonnelLocationResultData Data { get; set; }
	}

	/// <summary>
	/// The information about a specific unit's location
	/// </summary>
	public class PersonnelLocationResultData
	{
		/// <summary>
		/// Id of the Person
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// The Timestamp for the location in UTC
		/// </summary>
		public DateTime Timestamp { get; set; }

		/// <summary>
		/// GPS Latitude of the Person
		/// </summary>
		public string Latitude { get; set; }

		/// <summary>
		/// GPS Longitude of the Person
		/// </summary>
		public string Longitude { get; set; }

		/// <summary>
		/// GPS Latitude\Longitude Accuracy of the Person
		/// </summary>
		public string Accuracy { get; set; }

		/// <summary>
		/// GPS Altitude of the Person
		/// </summary>
		public string Altitude { get; set; }

		/// <summary>
		/// GPS Altitude Accuracy of the Person
		/// </summary>
		public string AltitudeAccuracy { get; set; }

		/// <summary>
		/// GPS Speed of the Person
		/// </summary>
		public string Speed { get; set; }

		/// <summary>
		/// GPS Heading of the Person
		/// </summary>
		public string Heading { get; set; }
	}
}
