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
		/// Mapbox style url (mapbox://styles/...) the department's maps use in a light theme. Always
		/// populated: the built-in style the department chose, Streets when it has not chosen one, or the
		/// department's own custom style when its Mapbox override is in effect (see AppMapboxAccessToken).
		/// </summary>
		public string MapDayStyleUrl { get; set; }

		/// <summary>
		/// Mapbox style url (mapbox://styles/...) the department's maps use in a dark theme. Always
		/// populated: the built-in night style the department chose; when it has not chosen one, the pair
		/// of the day style (Dark for Streets, Light and Dark; Navigation Night for Navigation Day and
		/// Navigation Night; unchanged for Outdoors, Satellite and Satellite Streets). The department's own
		/// custom style when its Mapbox override is in effect.
		/// </summary>
		public string MapNightStyleUrl { get; set; }

		/// <summary>
		/// Public (pk.) Mapbox token the native apps should render maps and call Mapbox services with:
		/// the department's own token when its Mapbox override is in effect (switched on, with a public
		/// token and a valid style url), otherwise the server's token for the requesting app. An override
		/// missing either half is ignored and the department's built-in style choice applies. Empty means
		/// keep the token built into the app. Only signed-in callers get one.
		/// </summary>
		public string AppMapboxAccessToken { get; set; }

		/// <summary>
		/// "maplibre" when the server serves MapLibre styles (maplibre-mapping-routing-navigation-plan.md §5.4). The
		/// fields below are for MapLibre clients (Omni); the Mapbox fields above keep their meaning for older builds.
		/// </summary>
		public string MapRenderer { get; set; }

		/// <summary>Signed GetStyle URL of the department's base map for a light theme (MapLibre style JSON).</summary>
		public string MapLibreStyleUrlDay { get; set; }

		/// <summary>Signed GetStyle URL of the department's base map for a dark theme.</summary>
		public string MapLibreStyleUrlNight { get; set; }

		/// <summary>Raster XYZ tile template ({z}/{x}/{y}) usable as a fallback base map.</summary>
		public string MapRasterTileUrl { get; set; }

		/// <summary>Signed static map image template; placeholders {lat} {lon} {zoom} {width} {height} {scale} {scheme}.</summary>
		public string StaticMapUrlTemplate { get; set; }

		/// <summary>A live traffic overlay is available (TrafficTileUrl).</summary>
		public bool TrafficAvailable { get; set; }

		/// <summary>Mapbox Traffic v1 vector tile template (layer "traffic", congestion low/moderate/heavy/severe, closed).</summary>
		public string TrafficTileUrl { get; set; }

		/// <summary>How often to reload traffic tiles, in seconds.</summary>
		public int TrafficRefreshSeconds { get; set; }

		/// <summary>Routing provider: valhalla, mapbox, google, osrm or estimate.</summary>
		public string RoutingProvider { get; set; }

		/// <summary>Drive times and routes reflect live traffic.</summary>
		public bool RoutingSupportsTraffic { get; set; }

		/// <summary>Routes report incidents (accidents, closures, construction).</summary>
		public bool RoutingSupportsIncidents { get; set; }

		/// <summary>Routes can carry spoken and on-screen navigation instructions.</summary>
		public bool RoutingSupportsVoice { get; set; }

		/// <summary>Unit in-app navigation is available (Valhalla or Mapbox routing, enabled, Navigation.InApp flag on).</summary>
		public bool NavigationAvailable { get; set; }

		/// <summary>The department may download offline map packs (OfflineManifestUrl).</summary>
		public bool OfflineMapsAvailable { get; set; }

		/// <summary>Signed offline map manifest URL (same origin as the API).</summary>
		public string OfflineManifestUrl { get; set; }

		/// <summary>Ambient tile cache budget for MapLibre on the device, in bytes.</summary>
		public long AmbientCacheMaxBytes { get; set; }

		/// <summary>
		/// The department sets statuses in the Unit and Responder apps with a two-second press and hold
		/// instead of a tap followed by Next/Submit (Dispatch Settings). Only signed-in callers get it;
		/// false otherwise.
		/// </summary>
		public bool StatusHoldToConfirm { get; set; }

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
		/// When acknowledging or muting a status timer alert needs a reason: 0 optional, 1 required at the alert level,
		/// 2 required for warnings and alerts. The acknowledge endpoint refuses a blank note with unit_alert_note_required.
		/// </summary>
		public int UnitStatusAlertNoteRequirement { get; set; }

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
		/// Firebase web app for browser and desktop push (WebPushConfig). The web and Electron editions mint
		/// their FCM token with it and register that token as Platform 3. All empty while web push is off.
		/// </summary>
		public string WebPushApiKey { get; set; }

		/// <summary>
		/// Firebase web app auth domain for browser and desktop push
		/// </summary>
		public string WebPushAuthDomain { get; set; }

		/// <summary>
		/// Firebase project id for browser and desktop push
		/// </summary>
		public string WebPushProjectId { get; set; }

		/// <summary>
		/// Firebase messaging sender id for browser and desktop push
		/// </summary>
		public string WebPushMessagingSenderId { get; set; }

		/// <summary>
		/// Firebase web app id for browser and desktop push
		/// </summary>
		public string WebPushAppId { get; set; }

		/// <summary>
		/// Public VAPID key of the Firebase project's Web Push certificate
		/// </summary>
		public string WebPushVapidKey { get; set; }

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
