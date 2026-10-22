using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The string keys a <see cref="MapVisualizationDefinition"/> uses. They are persisted inside saved definitions and
	/// sent to the browser, so a key is never renamed or reused; retire one by leaving it here unused.
	/// </summary>
	public static class MapVisualizationKeys
	{
		// Sources: where a layer's rows come from.
		/// <summary>One point per call, at the call's location.</summary>
		public const string SourceCalls = "calls";
		/// <summary>One point per unit per call it was dispatched to or set a status against, at the call's location.</summary>
		public const string SourceUnitResponses = "unitResponses";
		/// <summary>One point per member per call they responded to, at the call's location.</summary>
		public const string SourcePersonnelResponses = "personnelResponses";
		/// <summary>Where members were when they set a responding status (needs their device location).</summary>
		public const string SourceResponderOrigins = "responderOrigins";
		/// <summary>Where units were when they changed status.</summary>
		public const string SourceUnitStatusLocations = "unitStatusLocations";
		/// <summary>Unit location history (AVL/app GPS) as paths.</summary>
		public const string SourceUnitTracks = "unitTracks";
		/// <summary>Member location history (app GPS) as paths.</summary>
		public const string SourcePersonnelTracks = "personnelTracks";

		public static readonly IReadOnlyList<string> Sources = new[]
		{
			SourceCalls, SourceUnitResponses, SourcePersonnelResponses, SourceResponderOrigins, SourceUnitStatusLocations,
			SourceUnitTracks, SourcePersonnelTracks
		};

		// Displays: how a layer is drawn.
		public const string DisplayHeat = "heat";
		public const string DisplayPoints = "points";
		/// <summary>Hexagon grid; each cell colored by its aggregate, or by its most common category when colored by one.</summary>
		public const string DisplayHexbins = "hexbins";
		/// <summary>Paths; track sources only.</summary>
		public const string DisplayTracks = "tracks";

		public static readonly IReadOnlyList<string> Displays = new[] { DisplayHeat, DisplayPoints, DisplayHexbins, DisplayTracks };

		// Color by: the category each row falls into.
		public const string ColorByNone = "none";
		public const string ColorByCallType = "callType";
		public const string ColorByPriority = "priority";
		public const string ColorByOutcome = "outcome";
		/// <summary>The call's home station/group (geofence, then dispatch plurality, then responders).</summary>
		public const string ColorByGroup = "group";
		/// <summary>Four six-hour blocks: overnight, morning, afternoon, evening.</summary>
		public const string ColorByTimeOfDay = "timeOfDay";
		public const string ColorByHourOfDay = "hourOfDay";
		public const string ColorByDayOfWeek = "dayOfWeek";
		/// <summary>Weekday vs weekend.</summary>
		public const string ColorByWeekpart = "weekpart";
		public const string ColorByMonth = "month";
		public const string ColorByYear = "year";
		public const string ColorByUnit = "unit";
		public const string ColorByUnitType = "unitType";
		/// <summary>A unit's assigned station.</summary>
		public const string ColorByStation = "station";
		public const string ColorByPerson = "person";
		/// <summary>The status a unit or member set.</summary>
		public const string ColorByStatus = "status";
		/// <summary>Minute bands of the first-on-scene time (bands from <see cref="MapVisualizationOptions.MinuteBands"/>).</summary>
		public const string ColorByResponseBand = "responseBand";
		/// <summary>Distance bands to the nearest station (bands from <see cref="MapVisualizationOptions.DistanceBands"/>).</summary>
		public const string ColorByDistanceBand = "distanceBand";
		/// <summary>How many calls the same location had in the window: 1, 2–4, 5–9, 10+.</summary>
		public const string ColorByRepeatBand = "repeatBand";

		public static readonly IReadOnlyList<string> ColorBys = new[]
		{
			ColorByNone, ColorByCallType, ColorByPriority, ColorByOutcome, ColorByGroup, ColorByTimeOfDay, ColorByHourOfDay,
			ColorByDayOfWeek, ColorByWeekpart, ColorByMonth, ColorByYear, ColorByUnit, ColorByUnitType, ColorByStation,
			ColorByPerson, ColorByStatus, ColorByResponseBand, ColorByDistanceBand, ColorByRepeatBand
		};

		// Metrics: what a row weighs (heat), sizes (points) or what a cell measures (hex bins).
		public const string MetricCount = "count";
		/// <summary>Minutes from the call being logged to the first on-scene status (the unit's own, for unit responses).</summary>
		public const string MetricResponseMinutes = "responseMinutes";
		/// <summary>Minutes from the call being logged to the first responding/en-route status.</summary>
		public const string MetricTurnoutMinutes = "turnoutMinutes";
		/// <summary>Minutes from the call being logged to it being closed.</summary>
		public const string MetricDurationMinutes = "durationMinutes";
		public const string MetricUnitsDispatched = "unitsDispatched";
		public const string MetricPersonnelResponded = "personnelResponded";
		/// <summary>Straight-line distance to the nearest station, in the definition's distance unit.</summary>
		public const string MetricStationDistance = "stationDistance";
		/// <summary>Calls the same location had in the window.</summary>
		public const string MetricCallsAtLocation = "callsAtLocation";

		public static readonly IReadOnlyList<string> Metrics = new[]
		{
			MetricCount, MetricResponseMinutes, MetricTurnoutMinutes, MetricDurationMinutes, MetricUnitsDispatched,
			MetricPersonnelResponded, MetricStationDistance, MetricCallsAtLocation
		};

		public const string AggregateCount = "count";
		public const string AggregateSum = "sum";
		public const string AggregateMean = "mean";
		public const string AggregateMedian = "median";
		/// <summary>90th percentile: the NFPA 1710 / CFAI way to state a response-time goal.</summary>
		public const string AggregateP90 = "p90";
		public const string AggregateMax = "max";

		public static readonly IReadOnlyList<string> Aggregates = new[]
		{
			AggregateCount, AggregateSum, AggregateMean, AggregateMedian, AggregateP90, AggregateMax
		};

		public const string PaletteHeat = "heat";
		public const string PaletteFire = "fire";
		public const string PaletteViridis = "viridis";
		public const string PaletteBlues = "blues";
		/// <summary>Green (low) to red (high): for times and distances where low is good.</summary>
		public const string PaletteTraffic = "traffic";

		public static readonly IReadOnlyList<string> Palettes = new[] { PaletteHeat, PaletteFire, PaletteViridis, PaletteBlues, PaletteTraffic };

		public const string DistanceKilometers = "km";
		public const string DistanceMiles = "mi";

		/// <summary>Sources whose rows are member data: gated by the viewer's personnel location visibility.</summary>
		public static readonly IReadOnlyList<string> PersonnelSources = new[] { SourceResponderOrigins, SourcePersonnelTracks };

		public static readonly IReadOnlyList<string> TrackSources = new[] { SourceUnitTracks, SourcePersonnelTracks };

		private static readonly string[] CallDimensions =
		{
			ColorByCallType, ColorByPriority, ColorByOutcome, ColorByGroup, ColorByTimeOfDay, ColorByHourOfDay, ColorByDayOfWeek,
			ColorByWeekpart, ColorByMonth, ColorByYear
		};

		private static readonly string[] TimeDimensions =
		{
			ColorByTimeOfDay, ColorByHourOfDay, ColorByDayOfWeek, ColorByWeekpart, ColorByMonth, ColorByYear
		};

		private static readonly Dictionary<string, string[]> ColorBysBySource = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			[SourceCalls] = CallDimensions.Concat(new[] { ColorByResponseBand, ColorByDistanceBand, ColorByRepeatBand }).ToArray(),
			[SourceUnitResponses] = new[] { ColorByUnit, ColorByUnitType, ColorByStation }.Concat(CallDimensions).Concat(new[] { ColorByResponseBand, ColorByDistanceBand }).ToArray(),
			[SourcePersonnelResponses] = new[] { ColorByPerson }.Concat(CallDimensions).Concat(new[] { ColorByDistanceBand }).ToArray(),
			[SourceResponderOrigins] = new[] { ColorByPerson, ColorByStatus, ColorByDistanceBand }.Concat(TimeDimensions).ToArray(),
			[SourceUnitStatusLocations] = new[] { ColorByUnit, ColorByUnitType, ColorByStation, ColorByStatus }.Concat(TimeDimensions).ToArray(),
			[SourceUnitTracks] = new[] { ColorByUnit, ColorByUnitType, ColorByStation },
			[SourcePersonnelTracks] = new[] { ColorByPerson },
		};

		private static readonly Dictionary<string, string[]> MetricsBySource = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			[SourceCalls] = new[]
			{
				MetricCount, MetricResponseMinutes, MetricTurnoutMinutes, MetricDurationMinutes, MetricUnitsDispatched,
				MetricPersonnelResponded, MetricStationDistance, MetricCallsAtLocation
			},
			[SourceUnitResponses] = new[] { MetricCount, MetricResponseMinutes, MetricTurnoutMinutes, MetricDurationMinutes, MetricStationDistance },
			[SourcePersonnelResponses] = new[] { MetricCount, MetricDurationMinutes, MetricStationDistance },
			[SourceResponderOrigins] = new[] { MetricCount, MetricStationDistance },
			[SourceUnitStatusLocations] = new[] { MetricCount, MetricStationDistance },
			[SourceUnitTracks] = new[] { MetricCount },
			[SourcePersonnelTracks] = new[] { MetricCount },
		};

		public static bool IsTrackSource(string source) => TrackSources.Contains(source);

		public static bool IsPersonnelSource(string source) => PersonnelSources.Contains(source);

		/// <summary>The color-by keys that make sense for <paramref name="source"/> (always including <see cref="ColorByNone"/>).</summary>
		public static IReadOnlyList<string> ColorBysFor(string source)
		{
			if (source == null || !ColorBysBySource.TryGetValue(source, out var keys))
				return new[] { ColorByNone };

			return new[] { ColorByNone }.Concat(keys).ToArray();
		}

		public static IReadOnlyList<string> MetricsFor(string source)
		{
			if (source == null || !MetricsBySource.TryGetValue(source, out var keys))
				return new[] { MetricCount };

			return keys;
		}

		/// <summary>The displays that make sense for <paramref name="source"/>: paths only for track sources.</summary>
		public static IReadOnlyList<string> DisplaysFor(string source)
		{
			if (IsTrackSource(source))
				return new[] { DisplayTracks, DisplayHeat, DisplayHexbins };

			return new[] { DisplayHeat, DisplayPoints, DisplayHexbins };
		}
	}

	/// <summary>Bounds the map visualization service enforces. Shared with the browser through the page's options payload.</summary>
	public static class MapVisualizationLimits
	{
		public const int CurrentDefinitionVersion = 1;

		/// <summary>Longest analysis window (about five years).</summary>
		public const int MaxWindowDays = 1830;

		/// <summary>Default rolling window for a new definition.</summary>
		public const int DefaultWindowDays = 365;

		/// <summary>Longest window a track layer reads location history for; a longer analysis window keeps its last part.</summary>
		public const int MaxTrackWindowDays = 7;

		/// <summary>Calls read for one request; more and the result says it is truncated (newest kept).</summary>
		public const int MaxCalls = 100000;

		/// <summary>Points returned for one layer; more and the layer says it is truncated.</summary>
		public const int MaxPointsPerLayer = 75000;

		/// <summary>Location history points read for one track layer.</summary>
		public const int MaxTrackPoints = 150000;

		public const int MaxLayers = 6;

		public const int MaxNameLength = 200;

		public const int MaxDescriptionLength = 1000;

		/// <summary>Largest stored definition JSON, in characters.</summary>
		public const int MaxDefinitionLength = 32000;

		/// <summary>Saved visualizations one department may keep (shared and personal together).</summary>
		public const int MaxSavedPerDepartment = 250;

		/// <summary>Personal (unshared) visualizations one member may keep.</summary>
		public const int MaxPersonalPerUser = 50;

		public const int MaxFilterValues = 200;

		public const int MaxStationRings = 4;

		public const int MaxBands = 6;
	}
}
