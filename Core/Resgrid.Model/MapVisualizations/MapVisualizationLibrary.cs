using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Model
{
	/// <summary>What a library visualization is mainly for; the library groups and filters on it.</summary>
	public enum MapVisualizationCategories
	{
		CallDemand = 1,
		ResponsePerformance = 2,
		Coverage = 3,
		Resources = 4,
		Tracking = 5,
		CommunityRisk = 6
	}

	/// <summary>
	/// A built-in starting point in the visualization library. Its name and description are localized by key
	/// (<c>Library_{Key}_Name</c> / <c>Library_{Key}_Description</c> in the MapVisualizations resx family); the
	/// definition is a fresh copy every call so callers can change it freely. Every template is available to every
	/// department; <see cref="RecommendedFor"/> only decides which ones the library lists first, from the department's
	/// operating profile (never its organization type, which describes a department and gates nothing).
	/// </summary>
	public sealed class MapVisualizationTemplate
	{
		public MapVisualizationTemplate(string key, MapVisualizationCategories category, IReadOnlyList<string> recommendedFor,
			Func<MapVisualizationDefinition> build, bool needsCall = false, bool recommendedForVolunteers = false)
		{
			Key = key;
			Category = category;
			RecommendedFor = recommendedFor ?? Array.Empty<string>();
			_build = build;
			NeedsCall = needsCall;
			RecommendedForVolunteers = recommendedForVolunteers;
		}

		private readonly Func<MapVisualizationDefinition> _build;

		/// <summary>Stable id (persisted as <see cref="MapVisualization.TemplateKey"/> on visualizations made from it).</summary>
		public string Key { get; }

		public MapVisualizationCategories Category { get; }

		/// <summary>Operating profile archetype codes (<see cref="DepartmentOperatingProfile.ArchetypeCodes"/>) this suits. Empty = every department.</summary>
		public IReadOnlyList<string> RecommendedFor { get; }

		/// <summary>Also suited to a volunteer or combination workforce (availability and where responders come from).</summary>
		public bool RecommendedForVolunteers { get; }

		/// <summary>The template only shows something once a call is picked (incident footprint).</summary>
		public bool NeedsCall { get; }

		public MapVisualizationDefinition CreateDefinition() => _build();

		/// <summary>True when the profile's archetypes or workforce match this template's targets (general templates never are).</summary>
		public bool IsRecommendedFor(DepartmentOperatingProfile profile)
		{
			if (profile == null)
				return false;

			if (profile.Archetypes != null && profile.Archetypes.Any(a => RecommendedFor.Contains(a)))
				return true;

			return RecommendedForVolunteers && (profile.WorkforceMix == "volunteer" || profile.WorkforceMix == "combination");
		}
	}

	/// <summary>
	/// The predefined visualizations every department can open and adjust. Chosen for the kinds of work Resgrid's
	/// departments do (operating profile archetypes): call demand for everyone, response performance and station coverage
	/// for fire and EMS (NFPA 1710/1720 and standards-of-cover practice state response goals as 90th percentiles and
	/// coverage as distance from a station), repeat-location work for EMS, community paramedicine and crisis teams, and
	/// movement/coverage tracks for SAR, security patrols and field services. Nothing here is built for one customer.
	/// </summary>
	public static class MapVisualizationLibrary
	{
		// Operating profile archetype codes (DepartmentOperatingProfile.ArchetypeCodes).
		private const string FireCode = "fire";
		private const string EmsCode = "ems";
		private const string MentalHealthCode = "mental-health";
		private const string SarCode = "sar";
		private const string EmergencyResponseCode = "emergency-response";
		private const string HazmatCode = "hazmat";
		private const string IndustrialCode = "industrial";
		private const string SecurityCode = "security";

		private static readonly string[] Everyone = Array.Empty<string>();
		private static readonly string[] FireEms = { FireCode, EmsCode };
		private static readonly string[] Fire = { FireCode };

		public static readonly IReadOnlyList<MapVisualizationTemplate> All = new List<MapVisualizationTemplate>
		{
			// Call demand
			new MapVisualizationTemplate("call-hot-zones", MapVisualizationCategories.CallDemand, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHeat))),
			new MapVisualizationTemplate("busiest-areas", MapVisualizationCategories.CallDemand, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHexbins, palette: MapVisualizationKeys.PaletteFire))),
			new MapVisualizationTemplate("calls-by-type", MapVisualizationCategories.CallDemand, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByCallType), lastDays: 90)),
			new MapVisualizationTemplate("dominant-call-type", MapVisualizationCategories.CallDemand, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHexbins, MapVisualizationKeys.ColorByCallType))),
			new MapVisualizationTemplate("calls-by-priority", MapVisualizationCategories.CallDemand, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByPriority), lastDays: 90)),
			new MapVisualizationTemplate("call-outcomes", MapVisualizationCategories.CallDemand, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByOutcome))),
			new MapVisualizationTemplate("time-of-day", MapVisualizationCategories.CallDemand, new[] { FireCode, EmsCode, EmergencyResponseCode },
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByTimeOfDay))),
			new MapVisualizationTemplate("weekday-vs-weekend", MapVisualizationCategories.CallDemand, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByWeekpart)), recommendedForVolunteers: true),
			new MapVisualizationTemplate("seasonal-patterns", MapVisualizationCategories.CallDemand, new[] { FireCode, SarCode, EmergencyResponseCode },
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByMonth), lastDays: 730)),

			// Response performance
			new MapVisualizationTemplate("response-time-by-area", MapVisualizationCategories.ResponsePerformance, FireEms,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHexbins,
					metric: MapVisualizationKeys.MetricResponseMinutes, aggregate: MapVisualizationKeys.AggregateP90, palette: MapVisualizationKeys.PaletteTraffic))),
			new MapVisualizationTemplate("response-time-bands", MapVisualizationCategories.ResponsePerformance, FireEms,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByResponseBand,
					metric: MapVisualizationKeys.MetricResponseMinutes), minuteBands: new List<double> { 4, 8, 12, 20 })),
			new MapVisualizationTemplate("turnout-by-area", MapVisualizationCategories.ResponsePerformance, Fire,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHexbins,
					metric: MapVisualizationKeys.MetricTurnoutMinutes, aggregate: MapVisualizationKeys.AggregateMedian, palette: MapVisualizationKeys.PaletteTraffic))),
			new MapVisualizationTemplate("time-committed", MapVisualizationCategories.ResponsePerformance, new[] { EmsCode, MentalHealthCode, SecurityCode, IndustrialCode },
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHexbins,
					metric: MapVisualizationKeys.MetricDurationMinutes, aggregate: MapVisualizationKeys.AggregateMean, palette: MapVisualizationKeys.PaletteViridis))),

			// Coverage
			new MapVisualizationTemplate("station-coverage-gaps", MapVisualizationCategories.Coverage, FireEms,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByDistanceBand,
					metric: MapVisualizationKeys.MetricStationDistance), stationRings: new List<double> { 1.5, 2.5, 5 },
					distanceBands: new List<double> { 1.5, 2.5, 5 }, distanceUnit: MapVisualizationKeys.DistanceMiles)),
			new MapVisualizationTemplate("station-area-workload", MapVisualizationCategories.Coverage, FireEms,
				() => Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByGroup))),
			new MapVisualizationTemplate("where-units-go", MapVisualizationCategories.Resources, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceUnitResponses, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByUnit), lastDays: 90)),
			new MapVisualizationTemplate("unit-type-demand", MapVisualizationCategories.Resources, new[] { FireCode, EmsCode, HazmatCode, IndustrialCode },
				() => Single(Layer(MapVisualizationKeys.SourceUnitResponses, MapVisualizationKeys.DisplayHexbins, MapVisualizationKeys.ColorByUnitType))),
			new MapVisualizationTemplate("personnel-response-footprint", MapVisualizationCategories.Resources, new[] { SarCode, EmergencyResponseCode },
				() => Single(Layer(MapVisualizationKeys.SourcePersonnelResponses, MapVisualizationKeys.DisplayHeat)), recommendedForVolunteers: true),
			new MapVisualizationTemplate("responder-origins", MapVisualizationCategories.Coverage, new[] { SarCode, EmergencyResponseCode },
				() => Single(Layer(MapVisualizationKeys.SourceResponderOrigins, MapVisualizationKeys.DisplayHeat, palette: MapVisualizationKeys.PaletteViridis),
					stationRings: new List<double> { 5 }, distanceUnit: MapVisualizationKeys.DistanceMiles), recommendedForVolunteers: true),

			// Community risk / repeat demand
			new MapVisualizationTemplate("repeat-locations", MapVisualizationCategories.CommunityRisk, new[] { EmsCode, MentalHealthCode, SecurityCode, FireCode },
				() =>
				{
					var definition = Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByRepeatBand,
						metric: MapVisualizationKeys.MetricCallsAtLocation, sizeByMetric: true));
					definition.Filters.MinCallsAtLocation = 3;
					return definition;
				}),
			new MapVisualizationTemplate("false-alarm-hot-spots", MapVisualizationCategories.CommunityRisk, Fire,
				() =>
				{
					var definition = Single(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHeat, palette: MapVisualizationKeys.PaletteFire));
					definition.Filters.Outcomes = new List<int> { (int)CallStates.FalseAlarm, (int)CallStates.Unfounded };
					return definition;
				}),

			// Tracking
			new MapVisualizationTemplate("unit-movement-24h", MapVisualizationCategories.Tracking, Everyone,
				() => Single(Layer(MapVisualizationKeys.SourceUnitTracks, MapVisualizationKeys.DisplayTracks, MapVisualizationKeys.ColorByUnit), lastDays: 1, showStations: false)),
			new MapVisualizationTemplate("patrol-coverage", MapVisualizationCategories.Tracking, new[] { SecurityCode, IndustrialCode },
				() => Single(Layer(MapVisualizationKeys.SourceUnitTracks, MapVisualizationKeys.DisplayHeat, palette: MapVisualizationKeys.PaletteBlues), lastDays: 7, showStations: false)),
			new MapVisualizationTemplate("search-coverage", MapVisualizationCategories.Tracking, new[] { SarCode, EmergencyResponseCode },
				() =>
				{
					var definition = new MapVisualizationDefinition();
					definition.Filters.LastDays = 3;
					definition.Options.ShowStations = false;
					definition.Layers.Add(Layer(MapVisualizationKeys.SourcePersonnelTracks, MapVisualizationKeys.DisplayHexbins, palette: MapVisualizationKeys.PaletteBlues, cellSize: 150, id: "layer-1"));
					definition.Layers.Add(Layer(MapVisualizationKeys.SourcePersonnelTracks, MapVisualizationKeys.DisplayTracks, MapVisualizationKeys.ColorByPerson, id: "layer-2"));
					definition.Layers.Add(Layer(MapVisualizationKeys.SourceUnitTracks, MapVisualizationKeys.DisplayTracks, MapVisualizationKeys.ColorByUnit, id: "layer-3"));
					return definition;
				}),
			new MapVisualizationTemplate("incident-footprint", MapVisualizationCategories.Tracking, Everyone,
				() =>
				{
					var definition = new MapVisualizationDefinition();
					definition.Filters.LastDays = 30;
					definition.Layers.Add(Layer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByCallType, id: "layer-1"));
					definition.Layers.Add(Layer(MapVisualizationKeys.SourceUnitTracks, MapVisualizationKeys.DisplayTracks, MapVisualizationKeys.ColorByUnit, id: "layer-2"));
					definition.Layers.Add(Layer(MapVisualizationKeys.SourcePersonnelTracks, MapVisualizationKeys.DisplayTracks, MapVisualizationKeys.ColorByPerson, id: "layer-3"));
					definition.Layers.Add(Layer(MapVisualizationKeys.SourceUnitStatusLocations, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByStatus, id: "layer-4"));
					definition.Layers.Add(Layer(MapVisualizationKeys.SourceResponderOrigins, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByStatus, id: "layer-5"));
					return definition;
				}, needsCall: true),
		};

		public static MapVisualizationTemplate Find(string key) =>
			string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(t => string.Equals(t.Key, key.Trim(), StringComparison.Ordinal));

		/// <summary>The library in display order: templates the operating profile points at first, then general ones, then the rest.</summary>
		public static IReadOnlyList<MapVisualizationTemplate> ForOperatingProfile(DepartmentOperatingProfile profile)
		{
			return All
				.Select((template, index) => new { template, index })
				.OrderBy(t => t.template.IsRecommendedFor(profile) ? 0 : t.template.RecommendedFor.Count == 0 && !t.template.RecommendedForVolunteers ? 1 : 2)
				.ThenBy(t => t.index)
				.Select(t => t.template)
				.ToList();
		}

		private static MapVisualizationDefinition Single(MapVisualizationLayer layer, int lastDays = MapVisualizationLimits.DefaultWindowDays,
			List<double> stationRings = null, List<double> distanceBands = null, List<double> minuteBands = null,
			string distanceUnit = null, bool showStations = true)
		{
			var definition = new MapVisualizationDefinition();
			definition.Filters.LastDays = lastDays;
			definition.Options.ShowStations = showStations;

			if (stationRings != null)
				definition.Options.StationRings = stationRings;

			if (distanceBands != null)
				definition.Options.DistanceBands = distanceBands;

			if (minuteBands != null)
				definition.Options.MinuteBands = minuteBands;

			if (distanceUnit != null)
				definition.Options.DistanceUnit = distanceUnit;

			layer.Id = "layer-1";
			definition.Layers.Add(layer);

			return definition;
		}

		private static MapVisualizationLayer Layer(string source, string display, string colorBy = MapVisualizationKeys.ColorByNone,
			string metric = MapVisualizationKeys.MetricCount, string aggregate = null, string palette = MapVisualizationKeys.PaletteHeat,
			bool sizeByMetric = false, int? cellSize = null, string id = null)
		{
			var layer = new MapVisualizationLayer
			{
				Id = id,
				Source = source,
				Display = display,
				ColorBy = colorBy,
				Metric = metric,
				Aggregate = aggregate ?? (metric == MapVisualizationKeys.MetricCount ? MapVisualizationKeys.AggregateCount : MapVisualizationKeys.AggregateMedian)
			};

			layer.Style.Palette = palette;
			layer.Style.SizeByMetric = sizeByMetric;

			if (cellSize.HasValue)
				layer.Style.CellSizeMeters = cellSize.Value;

			return layer;
		}
	}
}
