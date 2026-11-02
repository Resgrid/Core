using System;

namespace Resgrid.Config
{
	public static class MappingConfig
	{
		public const string LeafletMapProvider = "leaflet";
		public const string MapboxMapProvider = "mapbox";
		public const string SelfHostedMapProvider = "selfhosted";

		public static int PersonnelLocationStaleSeconds = 30;
		public static int UnitLocationStaleSeconds = 30;
		public static int PersonnelLocationMinMeters = 20;
		public static int UnitLocationMinMeters = 20;


		public static string GoogleMapsApiKey = "";
		public static string GoogleMapsJSKey = "";
		public static string BingMapsApiKey = "";
		public static string What3WordsApiKey = "";
		public static string LocationIQApiKey = "";
		public static string OSMKey = "";

		public static string LoqateApiUrl = "https://saas.loqate.com";
		public static string LoqateApiKey = "";

		/***********************************
		 * GoogleMaps Api Keys
		 ***********************************/
		public static string UnitAppGoogleMapsKey = "";
		public static string ResponderAppGoogleMapsKey = "";
		public static string DispatchAppGoogleMapsKey = "";

		/***********************************
		 * OpenWeatherApi Keys
		 ***********************************/
		public static string BigBoardOpenWeatherApiKey = "";
		public static string DispatchOpenWeatherApiKey = "";

		/***********************************
		 * MapBox Api Keys used for Navigation and Routing, i.e. the Unit App
		 ***********************************/
		public static string UnitAppMapBoxKey = "";

		/***********************************
		 * what3words Api Keys
		 ***********************************/
		public static string UnitAppWhat3WordsKey = "";
		public static string ResponderAppWhat3WordsKey = "";
		public static string DispatchAppWhat3WordsKey = "";

		/***********************************
		 * Leaflet OSM Keys (used for Mapping and Map display)
		 ***********************************/

		public static string WebsiteOSMKey = "";
		public static string ApiOSMKey = "";
		public static string ResponderAppOSMKey = "";
		public static string UnitAppOSMKey = "";
		public static string DispatchAppOSMKey = "";
		public static string BigBoardOSMKey = "";

		public static string DispatchAppMapboxKey = "";

		/***********************************
		 * Mapbox public (pk.) tokens handed to the native apps in config so a token can be rotated
		 * without an app release. Empty = the app keeps the token built into it. Unit and Dispatch use
		 * UnitAppMapBoxKey / DispatchAppMapboxKey above.
		 ***********************************/
		public static string ResponderAppMapboxKey = "";
		public static string ICAppMapboxKey = "";
		public static string BigBoardMapboxKey = "";

		public static string WebsiteMapboxKey = "";
		public static string WebsiteMapboxAccessToken = "";
		public static string WebsiteMapMode = LeafletMapProvider;

		public static string LeafletTileUrl = "https://api.maptiler.com/maps/streets/{{z}}/{{x}}/{{y}}.png?key={0}";
		public static string MapBoxTileUrl = "";
		public static string MapBoxStyleUrl = "";

		public static string LeafletAttribution = "© OpenStreetMap contributors CC-BY-SA";
		public static string MapBoxAttribution = "© Mapbox © OpenStreetMap contributors";

		/***********************************
		 * Geocoding and Routing Service URLs
		 ***********************************/
		public static string NominatimUrl = "https://nominatim.openstreetmap.org";
		public static string OsrmUrl = "https://router.project-osrm.org";

		/***********************************
		 * Map providers (maplibre-mapping-routing-navigation-plan.md §5.1). Chosen per install; none of these change
		 * the fields the legacy store apps read (plan §21).
		 ***********************************/

		/// <summary>Base map tiles: "mapbox", "selfhosted", or "auto" (mapbox when a system Mapbox token exists, else selfhosted).</summary>
		public static string TileProvider = "auto";

		/// <summary>MapLibre style JSON for self-hosted tiles (e.g. a TileServer GL or Protomaps style). Empty: a raster style is synthesized from SelfHostedRasterTileUrl / LeafletTileUrl.</summary>
		public static string SelfHostedStyleUrlDay = "";
		public static string SelfHostedStyleUrlNight = "";

		/// <summary>Optional JSON object of preset class → style URL ("streets", "light", "dark", "outdoors", "satellite") so department style picks resolve on self-hosted tiles.</summary>
		public static string SelfHostedStyleCatalog = "";

		/// <summary>Raster XYZ tiles for the pixel-space editors and static maps on self-hosted installs; empty falls back to LeafletTileUrl.</summary>
		public static string SelfHostedRasterTileUrl = "";

		/// <summary>Forces the attribution text injected into every style (OpenStreetMap data needs visible attribution).</summary>
		public static string MapAttributionOverride = "";

		/// <summary>Routing provider: "valhalla", "mapbox", "google", "osrm" or "estimate" (straight line). Today's behaviour is "google".</summary>
		public static string RoutingProvider = "google";

		/// <summary>Provider used when the primary fails or times out; the straight-line estimate always remains the last resort.</summary>
		public static string RoutingFallbackProvider = "estimate";

		/// <summary>Base URL of a Valhalla server, e.g. http://valhalla:8002 (private network addresses are fine).</summary>
		public static string ValhallaUrl = "";

		/// <summary>Optional value sent as the X-Api-Key header to a Valhalla server behind a gateway.</summary>
		public static string ValhallaApiKey = "";

		/// <summary>Server-side Mapbox token for Directions, Matrix and Optimization; may be a secret (sk.) token and is never sent to clients.</summary>
		public static string MapboxRoutingAccessToken = "";

		/// <summary>Use live traffic where the provider has it (Mapbox driving-traffic, Google TRAFFIC_AWARE) for the DrivingTraffic profile and drive times.</summary>
		public static bool RoutingTrafficAware = true;

		/// <summary>"legacy" (Directions / Distance Matrix, today's calls) or "routes" (Routes API, required for Google Cloud projects created after March 2025).</summary>
		public static string GoogleRoutingApi = "legacy";

		/// <summary>Timeout per routing call in milliseconds; matrix calls get twice this.</summary>
		public static int RoutingTimeoutMs = 4000;

		/// <summary>Most origins one drive-time matrix may route on a provider without per-request cost (Valhalla, OSRM).</summary>
		public static int RoutingMatrixMaxOrigins = 100;

		/// <summary>Allows Unit in-app navigation; also needs the Navigation.InApp flag and a Valhalla or Mapbox routing provider.</summary>
		public static bool NavigationEnabled = true;

		/// <summary>Ordered geocoding chain: any of "google", "mapbox", "nominatim", "locationiq", "bing", comma separated.</summary>
		public static string GeocodingProviders = "google,locationiq,bing";

		/// <summary>Contact e-mail sent to Nominatim (its usage policy asks for one); production installs should self-host Nominatim.</summary>
		public static string NominatimUserAgentEmail = "";

		/// <summary>Mapbox token for the Geocoding API (v6).</summary>
		public static string MapboxGeocodingAccessToken = "";

		/// <summary>Live traffic overlay: "mapbox" or "none".</summary>
		public static string TrafficProvider = "none";

		/// <summary>Public (pk.) Mapbox token clients use for traffic tiles; a self-hosted install can enable traffic with only this.</summary>
		public static string MapboxTrafficAccessToken = "";

		/// <summary>How often clients reload traffic tiles (Mapbox updates traffic about every 8 minutes).</summary>
		public static int TrafficRefreshSeconds = 300;

		/// <summary>Lifetime of signed map resource URLs (styles, custom map tiles, indoor images, static maps).</summary>
		public static int SignedMapUrlTtlHours = 24;

		// ---- Offline map packs (maplibre-mapping-routing-navigation-plan.md section 19) ----------------------------------

		/// <summary>Install-wide switch for offline map packs (still gated per department by the Maps.Offline flag).</summary>
		public static bool OfflineMapsEnabled = true;

		/// <summary>
		/// Source PMTiles archive base-map packs are cut from: an https URL that honours Range requests (Resgrid's mirror of
		/// the Protomaps build, or object storage) or a local path (a self-host regional extract). Never the public Protomaps
		/// build URL. Empty disables base-map packs; department packs still build.
		/// </summary>
		public static string OfflineBasemapArchiveUrl = "";

		/// <summary>Build date of the archive (e.g. 20261028); part of every base-map pack key, so a new archive rebuilds packs.</summary>
		public static string OfflineBasemapVersion = "";

		/// <summary>Highest zoom in base-map packs (Protomaps builds stop at 15; MapLibre over-zooms beyond).</summary>
		public static int OfflineBasemapMaxZoom = 15;

		/// <summary>Largest base-map download a department's maximum response area may produce (500 MB; hard ceiling 1 GB).</summary>
		public static long ResponseAreaMaxBytes = 524288000;

		/// <summary>Radius of the response area used when a department has not drawn one.</summary>
		public static int ResponseAreaFallbackRadiusKm = 40;

		/// <summary>Hard cap per pack; the builder lowers the max zoom until a pack fits.</summary>
		public static long OfflinePackMaxBytes = 2147483648;

		/// <summary>Native ambient tile cache budget the apps set (256 MB).</summary>
		public static long AmbientCacheMaxBytes = 268435456;

		/// <summary>Where built packs are kept: "" (offline packs off), "file" (OfflinePackStoragePath) or "s3" (OfflinePackS3*).</summary>
		public static string OfflinePackStorage = "";

		/// <summary>Directory for OfflinePackStorage "file"; must be shared by the API and the worker.</summary>
		public static string OfflinePackStoragePath = "";

		/// <summary>S3-compatible endpoint (RustFS, MinIO, S3) for OfflinePackStorage "s3".</summary>
		public static string OfflinePackS3Endpoint = "";

		public static string OfflinePackS3AccessKey = "";

		public static string OfflinePackS3SecretKey = "";

		public static string OfflinePackS3Bucket = "resgrid-offline-maps";

		public static string OfflinePackS3Region = "us-east-1";

		public static bool OfflinePackS3UseSsl = true;

		/// <summary>Key prefix inside the bucket (an environment name keeps clusters apart in one bucket).</summary>
		public static string OfflinePackS3Prefix = "";

		/// <summary>
		/// Offline style templates (Protomaps v4 schema, generated from @protomaps/basemaps themes) with {basemap}, {glyphs}
		/// and {sprite} placeholders the apps fill with local paths. https URL or local path; mirrored into the pack store.
		/// </summary>
		public static string OfflineStyleUrlDay = "";

		public static string OfflineStyleUrlNight = "";

		/// <summary>Zip of the glyph PBF ranges the offline styles use; https URL or local path, mirrored into the pack store.</summary>
		public static string OfflineGlyphsArchiveUrl = "";

		/// <summary>Version of the style and glyph files above; part of their keys, so a new set is mirrored again.</summary>
		public static string OfflineStyleVersion = "1";

		/// <summary>
		/// "mapbox" or "selfhosted" for a surface that has (or lacks) a public Mapbox token. A department's own Mapbox
		/// account (override) always renders on Mapbox.
		/// </summary>
		public static string ResolveTileProvider(bool hasMapboxToken, bool isDepartmentOverride)
		{
			if (isDepartmentOverride && hasMapboxToken)
				return MapboxMapProvider;

			var configured = (TileProvider ?? "auto").Trim().ToLowerInvariant();

			if (configured == SelfHostedMapProvider)
				return SelfHostedMapProvider;

			return hasMapboxToken ? MapboxMapProvider : SelfHostedMapProvider;
		}

		/// <summary>Raster XYZ tiles for a self-hosted surface: SelfHostedRasterTileUrl, else the surface's Leaflet tile URL.</summary>
		public static string GetSelfHostedRasterTileUrl(string key)
		{
			if (!string.IsNullOrWhiteSpace(SelfHostedRasterTileUrl))
				return ReplaceTileKey(SelfHostedRasterTileUrl, null);

			return GetLegacyLeafletUrl(string.IsNullOrWhiteSpace(key) ? InfoConfig.WebsiteKey : key);
		}

		/// <summary>Attribution for self-hosted (OpenStreetMap-based) maps.</summary>
		public static string GetSelfHostedAttribution()
		{
			return string.IsNullOrWhiteSpace(MapAttributionOverride) ? "© OpenStreetMap contributors" : MapAttributionOverride;
		}

		public static ResolvedMapConfig GetMapConfig(string key)
		{
			var surfaceKey = string.IsNullOrWhiteSpace(key) ? InfoConfig.WebsiteKey : key;
			var mapProvider = GetPreferredMapProvider(surfaceKey);

			if (mapProvider == MapboxMapProvider)
			{
				var mapboxAccessToken = NormalizePublicMapboxAccessToken(GetSystemMapboxAccessToken(surfaceKey));

				if (TryCreateMapboxConfig(MapBoxStyleUrl, mapboxAccessToken, false, out var mapboxConfig))
					return mapboxConfig;

				if (!string.IsNullOrWhiteSpace(mapboxAccessToken) && !string.IsNullOrWhiteSpace(MapBoxTileUrl))
				{
					return new ResolvedMapConfig
					{
						MapProvider = MapboxMapProvider,
						TileUrl = ReplaceTileKey(MapBoxTileUrl, mapboxAccessToken),
						StyleUrl = MapBoxStyleUrl,
						AccessToken = mapboxAccessToken,
						Attribution = MapBoxAttribution,
						IsDepartmentOverride = false
					};
				}
			}

			return new ResolvedMapConfig
			{
				MapProvider = LeafletMapProvider,
				TileUrl = GetLegacyLeafletUrl(surfaceKey),
				StyleUrl = string.Empty,
				AccessToken = string.Empty,
				Attribution = LeafletAttribution,
				IsDepartmentOverride = false
			};
		}

		public static bool TryCreateMapboxConfig(string styleUrl, string accessToken, bool isDepartmentOverride, out ResolvedMapConfig mapConfig)
		{
			mapConfig = null;
			var publicAccessToken = NormalizePublicMapboxAccessToken(accessToken);

			if (string.IsNullOrWhiteSpace(styleUrl) || string.IsNullOrWhiteSpace(publicAccessToken))
				return false;

			var styleId = GetMapboxStyleId(styleUrl);

			if (string.IsNullOrWhiteSpace(styleId))
				return false;

			mapConfig = new ResolvedMapConfig
			{
				MapProvider = MapboxMapProvider,
				TileUrl = $"https://api.mapbox.com/styles/v1/{styleId}/tiles/256/{{z}}/{{x}}/{{y}}@2x?access_token={publicAccessToken}",
				StyleUrl = NormalizeMapboxStyleUrl(styleId),
				AccessToken = publicAccessToken,
				Attribution = MapBoxAttribution,
				IsDepartmentOverride = isDepartmentOverride
			};

			return true;
		}

		/// <summary>
		/// Builds a Mapbox config for a department-chosen style on the system's own public token for this
		/// surface. Independent of the surface's preferred provider: a department that picked a style gets
		/// it on the website even when the system default there is Leaflet, as long as a public token exists.
		/// </summary>
		public static bool TryCreateSystemMapboxConfig(string key, string styleUrl, out ResolvedMapConfig mapConfig)
		{
			var surfaceKey = string.IsNullOrWhiteSpace(key) ? InfoConfig.WebsiteKey : key;

			return TryCreateMapboxConfig(styleUrl, GetSystemMapboxAccessToken(surfaceKey), false, out mapConfig);
		}

		/// <summary>True when this surface has a public (pk.) Mapbox token to render a department style with.</summary>
		public static bool HasSystemMapboxAccessToken(string key)
		{
			var surfaceKey = string.IsNullOrWhiteSpace(key) ? InfoConfig.WebsiteKey : key;

			return !string.IsNullOrWhiteSpace(NormalizePublicMapboxAccessToken(GetSystemMapboxAccessToken(surfaceKey)));
		}

		/// <summary>
		/// The system public (pk.) Mapbox token for a native app surface, or empty when the operator has not
		/// configured one (the app then keeps its built-in token). Secret (sk.) tokens are never returned.
		/// </summary>
		public static string GetAppMapboxPublicAccessToken(string key)
		{
			string token;

			if (key == InfoConfig.UnitAppKey)
				token = UnitAppMapBoxKey;
			else if (key == InfoConfig.DispatchAppKey)
				token = DispatchAppMapboxKey;
			else if (key == InfoConfig.ResponderAppKey)
				token = ResponderAppMapboxKey;
			else if (key == InfoConfig.ICAppKey)
				token = ICAppMapboxKey;
			else if (key == InfoConfig.BigBoardKey)
				token = BigBoardMapboxKey;
			else
				token = string.Empty;

			return NormalizePublicMapboxAccessToken(token);
		}

		/// <summary>True for a public (pk.) Mapbox token; secret (sk.) and temporary (tk.) tokens are refused.</summary>
		public static bool IsPublicMapboxAccessToken(string accessToken)
		{
			return !string.IsNullOrWhiteSpace(NormalizePublicMapboxAccessToken(accessToken));
		}

		/// <summary>The style url a department override renders, normalized to mapbox://styles/owner/id; null when unusable.</summary>
		public static string GetNormalizedMapboxStyleUrl(string styleUrl)
		{
			var styleId = GetMapboxStyleId(styleUrl);

			return string.IsNullOrWhiteSpace(styleId) ? null : NormalizeMapboxStyleUrl(styleId);
		}

		/// <summary>The public token the website uses for Mapbox, or empty when there is none.</summary>
		public static string GetWebsiteMapboxPublicAccessToken()
		{
			return NormalizePublicMapboxAccessToken(GetSystemMapboxAccessToken(InfoConfig.WebsiteKey));
		}

		public static bool IsSupportedMapboxStyleUrl(string styleUrl)
		{
			return !string.IsNullOrWhiteSpace(GetMapboxStyleId(styleUrl));
		}

		public static string GetWebsiteOSMUrl()
		{
			return GetMapConfig(InfoConfig.WebsiteKey).TileUrl;
		}

		public static string GetApiOSMUrl()
		{
			return GetMapConfig(InfoConfig.ApiKey).TileUrl;
		}

		public static string GetResponderAppOSMUrl()
		{
			return GetMapConfig(InfoConfig.ResponderAppKey).TileUrl;
		}

		public static string GetUnitAppOSMUrl()
		{
			return GetMapConfig(InfoConfig.UnitAppKey).TileUrl;
		}

		public static string GetBigBoardAppOSMUrl()
		{
			return GetMapConfig(InfoConfig.BigBoardKey).TileUrl;
		}

		public static string GetDispatchAppOSMUrl()
		{
			return GetMapConfig(InfoConfig.DispatchAppKey).TileUrl;
		}

		private static string GetLegacyLeafletUrl(string key)
		{
			if (key == InfoConfig.WebsiteKey)
			{
				if (!string.IsNullOrWhiteSpace(WebsiteOSMKey))
					return ReplaceTileKey(LeafletTileUrl, WebsiteOSMKey);

				return LeafletTileUrl;
			}

			if (key == InfoConfig.ApiKey)
			{
				if (!string.IsNullOrWhiteSpace(ApiOSMKey))
					return ReplaceTileKey(LeafletTileUrl, ApiOSMKey);

				return LeafletTileUrl;
			}

			if (key == InfoConfig.ResponderAppKey)
			{
				if (!string.IsNullOrWhiteSpace(ResponderAppOSMKey))
					return ReplaceTileKey(LeafletTileUrl, ResponderAppOSMKey);

				return LeafletTileUrl;
			}

			if (key == InfoConfig.UnitAppKey)
			{
				if (!string.IsNullOrWhiteSpace(UnitAppOSMKey))
					return ReplaceTileKey(LeafletTileUrl, UnitAppOSMKey);

				return LeafletTileUrl;
			}

			if (key == InfoConfig.BigBoardKey)
			{
				if (!string.IsNullOrWhiteSpace(BigBoardOSMKey))
					return ReplaceTileKey(LeafletTileUrl, BigBoardOSMKey);

				return LeafletTileUrl;
			}

			if (key == InfoConfig.DispatchAppKey)
			{
				if (!string.IsNullOrWhiteSpace(DispatchAppOSMKey))
					return ReplaceTileKey(LeafletTileUrl, DispatchAppOSMKey);

				return LeafletTileUrl;
			}

			return LeafletTileUrl;
		}

		private static string GetPreferredMapProvider(string key)
		{
			if (key == InfoConfig.WebsiteKey)
				return NormalizeMapProvider(WebsiteMapMode);

			if (key == InfoConfig.DispatchAppKey && !string.IsNullOrWhiteSpace(DispatchAppMapboxKey))
				return MapboxMapProvider;

			if (key == InfoConfig.UnitAppKey && !string.IsNullOrWhiteSpace(UnitAppMapBoxKey))
				return MapboxMapProvider;

			return LeafletMapProvider;
		}

		private static string GetSystemMapboxAccessToken(string key)
		{
			if (key == InfoConfig.WebsiteKey)
			{
				if (!string.IsNullOrWhiteSpace(WebsiteMapboxAccessToken))
					return WebsiteMapboxAccessToken;

				if (!string.IsNullOrWhiteSpace(WebsiteMapboxKey))
					return WebsiteMapboxKey;

				return WebsiteOSMKey;
			}

			if (key == InfoConfig.DispatchAppKey)
				return DispatchAppMapboxKey;

			if (key == InfoConfig.UnitAppKey)
				return UnitAppMapBoxKey;

			return string.Empty;
		}

		private static string NormalizeMapProvider(string provider)
		{
			if (string.IsNullOrWhiteSpace(provider))
				return LeafletMapProvider;

			return provider.Trim().Equals(MapboxMapProvider, StringComparison.InvariantCultureIgnoreCase)
				? MapboxMapProvider
				: LeafletMapProvider;
		}

		private static string ReplaceTileKey(string tileUrl, string key)
		{
			if (string.IsNullOrWhiteSpace(tileUrl))
				return tileUrl;

			var normalizedTileUrl = tileUrl
				.Replace("{{", "{", StringComparison.InvariantCulture)
				.Replace("}}", "}", StringComparison.InvariantCulture);

			if (string.IsNullOrWhiteSpace(key))
				return normalizedTileUrl;

			return normalizedTileUrl.Replace("{0}", key, StringComparison.InvariantCulture);
		}

		// Rebuilt from the parsed id for mapbox:// input too, so a pasted ".html"/".json" suffix, query text, a
		// trailing "/draft" or an upper-case scheme never reaches the renderer.
		private static string NormalizeMapboxStyleUrl(string styleId)
		{
			return $"mapbox://styles/{styleId}";
		}

		private static string NormalizePublicMapboxAccessToken(string accessToken)
		{
			if (string.IsNullOrWhiteSpace(accessToken))
				return string.Empty;

			var trimmedAccessToken = accessToken.Trim();

			return trimmedAccessToken.StartsWith("pk.", StringComparison.Ordinal)
				? trimmedAccessToken
				: string.Empty;
		}

		private static string GetMapboxStyleId(string styleUrl)
		{
			if (string.IsNullOrWhiteSpace(styleUrl))
				return null;

			var trimmedStyleUrl = styleUrl.Trim();

			if (trimmedStyleUrl.StartsWith("mapbox://styles/", StringComparison.InvariantCultureIgnoreCase))
				return ExtractMapboxStyleId(trimmedStyleUrl.Substring("mapbox://styles/".Length));

			if (Uri.TryCreate(trimmedStyleUrl, UriKind.Absolute, out var mapboxStyleUri))
			{
				var path = mapboxStyleUri.AbsolutePath.Trim('/');
				var stylesIndex = path.IndexOf("styles/v1/", StringComparison.InvariantCultureIgnoreCase);

				if (stylesIndex >= 0)
				{
					var stylePath = path.Substring(stylesIndex + "styles/v1/".Length);
					return ExtractMapboxStyleId(stylePath);
				}
			}

			return null;
		}

		private static string ExtractMapboxStyleId(string stylePath)
		{
			if (string.IsNullOrWhiteSpace(stylePath))
				return null;

			var normalizedPath = stylePath.Trim('/');
			var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

			if (pathSegments.Length < 2)
				return null;

			// Mapbox Studio's share link ends the style id with ".html" (and the Styles API with ".json");
			// style ids themselves never contain a dot. A mapbox:// value is not parsed as a Uri, so its
			// query or fragment is still on the segment here.
			var styleId = pathSegments[1].Split('.', '?', '#')[0];

			if (string.IsNullOrWhiteSpace(styleId))
				return null;

			return $"{pathSegments[0]}/{styleId}";
		}
	}
}
