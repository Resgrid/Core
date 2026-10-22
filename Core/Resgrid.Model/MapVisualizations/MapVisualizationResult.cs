using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The data behind one rendering of a <see cref="MapVisualizationDefinition"/>. Points are compact arrays so a
	/// five-year heat map stays a few megabytes: <c>[latitude, longitude, categoryIndex, metric, referenceId]</c>, where
	/// categoryIndex is -1 when the layer is not colored by a category, metric is null when the row has no value for the
	/// layer's metric (a call nobody reached on scene) and referenceId is the call id (0 when the row has no call).
	/// </summary>
	public sealed class MapVisualizationResult
	{
		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		/// <summary>First moment of the window in the department's time zone (for display).</summary>
		public DateTime StartLocal { get; set; }

		/// <summary>Last moment of the window, inclusive, in the department's time zone (for display).</summary>
		public DateTime EndLocal { get; set; }

		/// <summary>The definition actually used (after normalization).</summary>
		public MapVisualizationDefinition Definition { get; set; }

		public List<MapVisualizationLayerResult> Layers { get; set; } = new List<MapVisualizationLayerResult>();

		public List<MapVisualizationStation> Stations { get; set; } = new List<MapVisualizationStation>();

		/// <summary>Counts over the calls that matched the shared filters.</summary>
		public MapVisualizationSummary Summary { get; set; } = new MapVisualizationSummary();

		/// <summary>The call the definition is focused on, when <see cref="MapVisualizationFilters.CallId"/> is set and visible.</summary>
		public MapVisualizationCallFocus Call { get; set; }

		/// <summary>More calls matched than <see cref="MapVisualizationLimits.MaxCalls"/>; the newest were used.</summary>
		public bool Truncated { get; set; }

		/// <summary>Map center when there is nothing to fit to (the department's center).</summary>
		public double? CenterLatitude { get; set; }

		public double? CenterLongitude { get; set; }
	}

	public sealed class MapVisualizationLayerResult
	{
		public string LayerId { get; set; }

		public string Source { get; set; }

		public string Display { get; set; }

		public string ColorBy { get; set; }

		public string Metric { get; set; }

		public List<MapVisualizationCategory> Categories { get; set; } = new List<MapVisualizationCategory>();

		/// <summary>[lat, lon, categoryIndex, metric, referenceId] per row.</summary>
		public List<double?[]> Points { get; set; } = new List<double?[]>();

		public List<MapVisualizationTrack> Tracks { get; set; } = new List<MapVisualizationTrack>();

		/// <summary>Rows in the layer before any cap.</summary>
		public int Total { get; set; }

		/// <summary>Rows the layer matched that had no usable location.</summary>
		public int WithoutLocation { get; set; }

		public bool Truncated { get; set; }

		/// <summary>
		/// Why the layer is empty or partial, as a <see cref="MapVisualizationNotices"/> key (localized by the page):
		/// no permission, no location history, track window shortened, …
		/// </summary>
		public List<string> Notices { get; set; } = new List<string>();
	}

	/// <summary>A legend entry. <see cref="Color"/> is fixed per key so a type keeps its color between visualizations.</summary>
	public sealed class MapVisualizationCategory
	{
		public string Key { get; set; }

		public string Label { get; set; }

		public string Color { get; set; }

		public int Count { get; set; }

		/// <summary>Order for bands and time buckets (lower first); 0 for named categories, which sort by count.</summary>
		public int Order { get; set; }
	}

	public sealed class MapVisualizationTrack
	{
		public string Id { get; set; }

		public string Label { get; set; }

		public int CategoryIndex { get; set; } = -1;

		/// <summary>[lat, lon, unixSeconds] in time order.</summary>
		public List<double[]> Coordinates { get; set; } = new List<double[]>();
	}

	public sealed class MapVisualizationStation
	{
		public int GroupId { get; set; }

		public string Name { get; set; }

		public double Latitude { get; set; }

		public double Longitude { get; set; }
	}

	public sealed class MapVisualizationSummary
	{
		public int Calls { get; set; }

		public int CallsWithLocation { get; set; }

		/// <summary>Median minutes to first on scene over calls that had one; null when none did.</summary>
		public double? MedianResponseMinutes { get; set; }

		/// <summary>90th percentile minutes to first on scene.</summary>
		public double? P90ResponseMinutes { get; set; }

		/// <summary>Calls by local day of week (0 = Sunday) and hour: 7 rows × 24 columns.</summary>
		public int[][] ByWeekdayHour { get; set; } = new int[0][];

		/// <summary>Busiest call types, most first (at most 10).</summary>
		public List<MapVisualizationCategory> TopCallTypes { get; set; } = new List<MapVisualizationCategory>();
	}

	public sealed class MapVisualizationCallFocus
	{
		public int CallId { get; set; }

		public string Number { get; set; }

		public string Name { get; set; }

		public string Type { get; set; }

		public string Address { get; set; }

		public DateTime LoggedOnUtc { get; set; }

		public DateTime? ClosedOnUtc { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }
	}

	/// <summary>Notice keys a layer can carry. Persisted nowhere; the page maps each to a localized sentence.</summary>
	public static class MapVisualizationNotices
	{
		/// <summary>The viewer may not see member locations, so a member-location layer is empty.</summary>
		public const string PersonnelLocationsHidden = "personnelLocationsHidden";
		/// <summary>The viewer may not see unit locations.</summary>
		public const string UnitLocationsHidden = "unitLocationsHidden";
		/// <summary>The track layer read only the last <see cref="MapVisualizationLimits.MaxTrackWindowDays"/> days of the window.</summary>
		public const string TrackWindowShortened = "trackWindowShortened";
		/// <summary>The layer hit <see cref="MapVisualizationLimits.MaxPointsPerLayer"/> or <see cref="MapVisualizationLimits.MaxTrackPoints"/>.</summary>
		public const string LayerTruncated = "layerTruncated";
		/// <summary>No station has a location, so distance metrics and rings are unavailable.</summary>
		public const string NoStationLocations = "noStationLocations";
		/// <summary>None of the layer's rows had a status that maps to on scene / responding (no base types set).</summary>
		public const string NoStatusTimes = "noStatusTimes";
		/// <summary>The focused call was not found or is not visible to the viewer.</summary>
		public const string CallNotFound = "callNotFound";
	}
}
