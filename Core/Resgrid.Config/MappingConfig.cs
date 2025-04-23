namespace Resgrid.Config
{
	public static class MappingConfig
	{
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

		public static string LeafletTileUrl = "https://api.maptiler.com/maps/streets/{{z}}/{{x}}/{{y}}.png?key={0}";
		public static string MapBoxTileUrl = "";

		public static string LeafletAttribution = "© OpenStreetMap contributors CC-BY-SA";

		public static string GetWebsiteOSMUrl()
		{
			if (!string.IsNullOrWhiteSpace(WebsiteOSMKey))
				return string.Format(MapBoxTileUrl, WebsiteOSMKey);
			else
				return LeafletTileUrl;
		}

		public static string GetApiOSMUrl()
		{
			if (!string.IsNullOrWhiteSpace(ApiOSMKey))
				return string.Format(LeafletTileUrl, ApiOSMKey);
			else
				return LeafletTileUrl;
		}

		public static string GetResponderAppOSMUrl()
		{
			if (!string.IsNullOrWhiteSpace(ResponderAppOSMKey))
				return string.Format(LeafletTileUrl, ResponderAppOSMKey);
			else
				return LeafletTileUrl;
		}

		public static string GetUnitAppOSMUrl()
		{
			if (!string.IsNullOrWhiteSpace(UnitAppOSMKey))
				return string.Format(LeafletTileUrl, UnitAppOSMKey);
			else
				return LeafletTileUrl;
		}

		public static string GetBigBoardAppOSMUrl()
		{
			if (!string.IsNullOrWhiteSpace(BigBoardOSMKey))
				return string.Format(LeafletTileUrl, BigBoardOSMKey);
			else
				return LeafletTileUrl;
		}

		public static string GetDispatchAppOSMUrl()
		{
			if (!string.IsNullOrWhiteSpace(DispatchAppMapboxKey))
				return string.Format(MapBoxTileUrl, DispatchAppMapboxKey);
			else
				return LeafletTileUrl;
		}
	}
}
