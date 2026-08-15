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
		/// Url for the event hub
		/// </summary>
		public string EventingUrl { get; set; }

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
		/// The Url for the rendered map tiles
		/// </summary>
		public string MapUrl { get; set; }

		/// <summary>
		/// The map provider identifier
		/// </summary>
		public string MapProvider { get; set; }

		/// <summary>
		/// The resolved Mapbox style url for website and native clients
		/// </summary>
		public string MapStyleUrl { get; set; }

		/// <summary>
		/// The access token for the resolved map config
		/// </summary>
		public string MapAccessToken { get; set; }

		/// <summary>
		/// Indicates if a department-specific override is active
		/// </summary>
		public bool IsDepartmentMapOverride { get; set; }

		/// <summary>
		/// The attribution for the rendered map
		/// </summary>
		public string MapAttribution { get; set; }

		/// <summary>
		/// Latitude every map in every client should open on for this department. Resolved from the
		/// department's configured map center, falling back to its address and finally to a system
		/// default, so this is always populated.
		/// </summary>
		public double MapCenterLatitude { get; set; }

		/// <summary>
		/// Longitude every map in every client should open on for this department.
		/// </summary>
		public double MapCenterLongitude { get; set; }

		/// <summary>
		/// Zoom level to open department-wide maps at. Defaults to 9 when the department has not set one.
		/// </summary>
		public int MapCenterZoomLevel { get; set; }

		/// <summary>
		/// How long a unit may sit in a status before the board flags it, keyed by the status's canonical
		/// base type. Empty means the department has configured no thresholds and nothing is highlighted.
		/// </summary>
		public List<UnitStatusThresholdData> UnitStatusThresholds { get; set; } = new List<UnitStatusThresholdData>();

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
		/// True when the run card dispatch system is enabled for this department
		/// </summary>
		public bool DispatchRunCardsEnabled { get; set; }

		/// <summary>
		/// Department dispatch recommendation mode (0 = off, 1 = station based, 2 = closest unit)
		/// </summary>
		public int DispatchRecommendationMode { get; set; }

		/// <summary>
		/// True when matched run cards auto-dispatch; false = recommendations pre-populate for review
		/// </summary>
		public bool DispatchRecommendationAutoDispatch { get; set; }

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
		/// Analytics Api Key
		/// </summary>
		public string AnalyticsApiKey { get; set; }

		/// <summary>
		/// Analytics Host
		/// </summary>
		public string AnalyticsHost { get; set; }

		/// <summary>
		/// Whether the current user should use modern application notification sounds. This is
		/// enabled when either the department-wide setting or the user's profile setting is enabled.
		/// </summary>
		public bool EnableModernApplicationSounds { get; set; }
	}
	/// <summary>One time-in-status threshold for the board's unit highlighting.</summary>
	public class UnitStatusThresholdData
	{
		/// <summary>The ActionBaseTypes value this applies to.</summary>
		public int BaseType { get; set; }

		/// <summary>Seconds after which the unit is highlighted. 0 disables the warning.</summary>
		public int WarnSeconds { get; set; }

		/// <summary>Seconds after which the unit is escalated to a high-priority alert. 0 disables it.</summary>
		public int AlertSeconds { get; set; }
	}

}
