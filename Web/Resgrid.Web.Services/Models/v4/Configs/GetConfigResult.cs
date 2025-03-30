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
		/// The key for a Directions API
		/// </summary>
		public string DirectionsMapKey { get; set; }

		/// <summary>
		/// The key for a Navigation API
		/// </summary>
		public string NavigationMapKey { get; set; }

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

		/// <summary>
		/// API url for Novu
		/// </summary>
		public string NovuBackendApiUrl { get; set; }

		/// <summary>
		/// Websocket url for Novu
		/// </summary>
		public string NovuSocketUrl { get; set; }

		/// <summary>
		/// Novu Application Id
		/// </summary>
		public string NovuApplicationId { get; set; }

		/// <summary>
		/// Novu Environment Id
		/// </summary>
		public string NovuEnvironmentId { get; set; }

		/// <summary>
		/// PostHog Api Key
		/// </summary>
		public string PostHogApiKey { get; set; }

		/// <summary>
		/// PostHog Host
		/// </summary>
		public string PostHogHost { get; set; }
	}
}
