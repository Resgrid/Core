using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Configs
{
	/// <summary>
	/// Gets Configuration Information by a key
	/// </summary>
	public class GetConfigResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetConfigResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetConfigResult()
		{
			Data = new GetConfigResultData();
		}
	}

	/// <summary>
	/// All the data required to populate the New Call form
	/// </summary>
	public class GetConfigResultData
	{
		/// <summary>
		/// The key for What3Words
		/// </summary>
		public string W3WKey { get; set; }

		/// <summary>
		/// The key for Google Maps
		/// </summary>
		public string GoogleMapsKey { get; set; }

		/// <summary>
		/// The key for Logging
		/// </summary>
		public string LoggingKey { get; set; }

		/// <summary>
		/// The Url for the leaflet map api
		/// </summary>
		public string MapUrl { get; set; }

		/// <summary>
		/// The Attribution for the leaflet map
		/// </summary>
		public string MapAttribution { get; set; }

		/// <summary>
		/// How many seconds to prevent a duplicate gps location from being logged for personnel
		/// </summary>
		public int PersonnelLocationStaleSeconds { get; set; }

		/// <summary>
		/// How many seconds to prevent a duplicate gps location from being logged for units
		/// </summary>
		public int UnitLocationStaleSeconds { get; set; }

		/// <summary>
		/// How many meters between subsuquent gps locations to allow the position update to go through for personnel
		/// </summary>
		public int PersonnelLocationMinMeters { get; set; }

		/// <summary>
		/// How many meters between subsuquent gps locations to allow the position update to go through for units
		/// </summary>
		public int UnitLocationMinMeters { get; set; }

		/// <summary>
		/// API Key for the OpenWeatherAPI
		/// </summary>
		public string OpenWeatherApiKey { get; set; }
	}
}
