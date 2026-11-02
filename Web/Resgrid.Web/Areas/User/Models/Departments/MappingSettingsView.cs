using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class MappingSettingsView
	{
		public bool? SaveSuccess { get; set; }
		public string Message { get; set; }

		/// <summary>
		/// Time-in-status thresholds driving Big Board highlighting, one row per canonical status
		/// meaning. Entered in minutes because that is how dispatchers talk about them.
		/// </summary>
		public List<UnitStatusThresholdRow> UnitStatusThresholds { get; set; } = new List<UnitStatusThresholdRow>();

		/// <summary>When acknowledging or muting a status timer alert on the Big Board needs a reason.</summary>
		public UnitStatusAlertNoteRequirements UnitStatusAlertNoteRequirement { get; set; }

		/// <summary>Why some timers can never fire (statuses without a meaning, untimed dispatch statuses). Built server-side on every render.</summary>
		public List<string> UnitStatusTimerWarnings { get; set; } = new List<string>();

		public int PersonnelLocationTTL { get; set; }
		public int UnitLocationTTL { get; set; }

		public bool PersonnelAllowStatusWithNoLocationToOverwrite { get; set; }
		public bool UnitAllowStatusWithNoLocationToOverwrite { get; set; }

		/// <summary>Base map in a light theme, on the website and in every app.</summary>
		public MapStyleTypes MapStyle { get; set; }

		/// <summary>Base map the apps use in a dark theme. Automatic pairs with <see cref="MapStyle"/>.</summary>
		public MapStyleTypes MapStyleNight { get; set; }

		/// <summary>False when the website cannot draw the chosen style (no Mapbox token, and not on MapLibre with self-hosted styles).</summary>
		public bool WebsiteSupportsMapStyle { get; set; }

		/// <summary>
		/// Signed GetStaticMap template ({lat} {lon} {zoom} {width} {height} {scale} {scheme}) the style previews fill, adding
		/// the style; empty disables them. Built server-side on every render.
		/// </summary>
		public string PreviewUrlTemplate { get; set; }

		/// <summary>The presets this install can render (provider-aware, plan §5.3), in picker order.</summary>
		public List<MapStyleTypes> AvailableStyles { get; set; } = new List<MapStyleTypes>();

		/// <summary>Where department styles render on this install: "mapbox" or "selfhosted" (shown read-only).</summary>
		public string BaseMapProvider { get; set; }

		/// <summary>The install's routing provider (<see cref="RoutingProviderNames"/>), shown read-only.</summary>
		public string RoutingProvider { get; set; }

		/// <summary>The live traffic layer is configured on this install, shown read-only.</summary>
		public bool TrafficLayerAvailable { get; set; }

		public double PreviewLatitude { get; set; }
		public double PreviewLongitude { get; set; }

		public bool UseMapboxOverride { get; set; }

		[Display(Name = "Mapbox Style Url")]
		public string MapboxStyleUrl { get; set; }

		[Display(Name = "Mapbox Public Access Token")]
		public string MapboxAccessToken { get; set; }

		// Setting 122 (MappingNavigationConfig), positive wording for the form; stored inverted ("Off") for protobuf.

		/// <summary>The department has the Navigation.InApp flag; the defaults are kept either way. Built server-side on every render.</summary>
		public bool NavigationEnabled { get; set; }

		public bool NavigationVoiceGuidance { get; set; } = true;
		public NavigationDistanceUnits NavigationDistanceUnits { get; set; }
		public int NavigationArrivalRadiusMeters { get; set; } = MappingNavigationConfig.DefaultArrivalRadiusMeters;
		public bool NavigationOnScenePrompt { get; set; } = true;
		public bool NavigationAvoidTolls { get; set; }
		public bool NavigationAvoidHighways { get; set; }
		public bool NavigationAvoidFerries { get; set; }
		public bool NavigationAvoidUnpaved { get; set; }

		/// <summary>Record late arrivals and long stops on routes.</summary>
		public bool RouteTimeDeviations { get; set; } = true;
		public int RouteDeviationThresholdMinutes { get; set; } = MappingNavigationConfig.DefaultDeviationThresholdMinutes;

		// Setting 123 (OfflineMapPacksConfig).

		/// <summary>Offline maps are on for the install and the department (Maps.Offline). Built server-side on every render.</summary>
		public bool OfflineMapsEnabled { get; set; }

		/// <summary>Radius of the circle used when no area is drawn. Built server-side on every render.</summary>
		public int OfflineFallbackRadiusKm { get; set; }

		/// <summary>The maximum response area as GeoJSON; empty uses the fallback circle.</summary>
		public string OfflineResponseAreaGeoJson { get; set; }

		public bool OfflineIncludeCustomMaps { get; set; } = true;
		public bool OfflineIncludeIndoorMaps { get; set; } = true;
		public bool OfflineIncludeLayers { get; set; } = true;
		public bool OfflineIncludePois { get; set; } = true;
		public bool OfflineIncludeHydrants { get; set; } = true;
		public OfflineDownloadPolicy OfflineDownloadPolicy { get; set; }
		public bool OfflineIncidentPacks { get; set; } = true;
		public int OfflineIncidentRadiusKm { get; set; } = OfflineMapPacksConfig.DefaultIncidentRadiusKm;
		public bool OfflineCorridorPacks { get; set; } = true;
		public int OfflineCorridorBufferMeters { get; set; } = OfflineMapPacksConfig.DefaultCorridorBufferMeters;
		public int OfflineRefreshHours { get; set; } = OfflineMapPacksConfig.DefaultRefreshHours;
		public int OfflineStaleDays { get; set; } = OfflineMapPacksConfig.DefaultStaleDays;
		public bool OfflinePreferLocalInArea { get; set; }
	}

	/// <summary>Body of Department/EstimateResponseArea: the area being drawn, as GeoJSON (empty = the fallback circle).</summary>
	public class EstimateResponseAreaInput
	{
		public string GeoJson { get; set; }
	}
}
