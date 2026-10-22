using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// What a map visualization shows: one shared set of filters, the data layers drawn over the base map, and display
	/// options. Stored as JSON on <see cref="MapVisualization.Definition"/> and sent to the browser unchanged, so every
	/// enum-like value is a stable string key (see <see cref="MapVisualizationKeys"/>) rather than a number. A key the
	/// server does not know is dropped by <c>MapVisualizationDefinitionValidator</c>, never guessed at.
	/// </summary>
	public sealed class MapVisualizationDefinition
	{
		/// <summary>Schema version of this definition. Bumped only for a change old definitions cannot be read under.</summary>
		public int Version { get; set; } = MapVisualizationLimits.CurrentDefinitionVersion;

		public MapVisualizationFilters Filters { get; set; } = new MapVisualizationFilters();

		public List<MapVisualizationLayer> Layers { get; set; } = new List<MapVisualizationLayer>();

		public MapVisualizationOptions Options { get; set; } = new MapVisualizationOptions();
	}

	/// <summary>
	/// Filters every layer of a visualization shares. Empty lists mean "no restriction". Hours and days are in the
	/// department's local time.
	/// </summary>
	public sealed class MapVisualizationFilters
	{
		/// <summary>
		/// A rolling window ending now, in days. When set it wins over <see cref="Start"/>/<see cref="End"/>, so a saved
		/// "last 12 months" stays current. Null with no Start/End falls back to the default window.
		/// </summary>
		public int? LastDays { get; set; } = 365;

		/// <summary>Fixed window start, department local date (inclusive). Used when <see cref="LastDays"/> is null.</summary>
		public DateTime? Start { get; set; }

		/// <summary>Fixed window end, department local date (inclusive of that whole day). Used when <see cref="LastDays"/> is null.</summary>
		public DateTime? End { get; set; }

		/// <summary>Call type names (calls store their type by name).</summary>
		public List<string> CallTypes { get; set; } = new List<string>();

		/// <summary>Call priority ids (built-in 0–3 or the department's custom priority ids).</summary>
		public List<int> Priorities { get; set; } = new List<int>();

		/// <summary><see cref="CallStates"/> values. Empty means every state except Pending.</summary>
		public List<int> Outcomes { get; set; } = new List<int>();

		/// <summary>Station/group ids. A call matches when its home group (see CallGroupAssigner) is one of them.</summary>
		public List<int> GroupIds { get; set; } = new List<int>();

		/// <summary>Unit ids. A call matches when one of these units was dispatched to it or set a status against it.</summary>
		public List<int> UnitIds { get; set; } = new List<int>();

		/// <summary>Local hours of the day, 0–23, a call was logged in.</summary>
		public List<int> Hours { get; set; } = new List<int>();

		/// <summary>Local days of the week a call was logged on, 0 = Sunday … 6 = Saturday (<see cref="DayOfWeek"/>).</summary>
		public List<int> DaysOfWeek { get; set; } = new List<int>();

		/// <summary>One call: every layer is restricted to it, and track layers use the call's own time span.</summary>
		public int? CallId { get; set; }

		/// <summary>Only locations with at least this many calls in the window (repeat locations). 0 or 1 = every location.</summary>
		public int MinCallsAtLocation { get; set; }
	}

	/// <summary>One data layer: where the data comes from, how it is drawn and what drives its color/size.</summary>
	public sealed class MapVisualizationLayer
	{
		/// <summary>Client-generated id, unique within the definition. Used for the legend and toggles.</summary>
		public string Id { get; set; }

		/// <summary>Optional label for the legend. Blank uses the source's name.</summary>
		public string Name { get; set; }

		/// <summary>A <see cref="MapVisualizationKeys.Sources"/> key.</summary>
		public string Source { get; set; } = MapVisualizationKeys.SourceCalls;

		/// <summary>A <see cref="MapVisualizationKeys.Displays"/> key.</summary>
		public string Display { get; set; } = MapVisualizationKeys.DisplayHeat;

		/// <summary>A <see cref="MapVisualizationKeys.ColorBys"/> key, or <see cref="MapVisualizationKeys.ColorByNone"/>.</summary>
		public string ColorBy { get; set; } = MapVisualizationKeys.ColorByNone;

		/// <summary>A <see cref="MapVisualizationKeys.Metrics"/> key: what a point weighs or a cell measures.</summary>
		public string Metric { get; set; } = MapVisualizationKeys.MetricCount;

		/// <summary>A <see cref="MapVisualizationKeys.Aggregates"/> key: how a grid cell combines its points' metric.</summary>
		public string Aggregate { get; set; } = MapVisualizationKeys.AggregateCount;

		public bool Visible { get; set; } = true;

		public MapVisualizationLayerStyle Style { get; set; } = new MapVisualizationLayerStyle();
	}

	/// <summary>Drawing settings for a layer. Out-of-range values are clamped by the validator.</summary>
	public sealed class MapVisualizationLayerStyle
	{
		/// <summary>A <see cref="MapVisualizationKeys.Palettes"/> key.</summary>
		public string Palette { get; set; } = MapVisualizationKeys.PaletteHeat;

		/// <summary>0.1–1.</summary>
		public double Opacity { get; set; } = 0.75;

		/// <summary>Heat blur radius in screen pixels, 5–60.</summary>
		public int Radius { get; set; } = 22;

		/// <summary>Heat intensity multiplier, 0.2–5. Higher makes sparse data glow sooner.</summary>
		public double Intensity { get; set; } = 1;

		/// <summary>Grid cell width in meters (hex bins), 100–10000.</summary>
		public int CellSizeMeters { get; set; } = 800;

		/// <summary>Point radius in screen pixels, 2–20.</summary>
		public int PointSize { get; set; } = 6;

		/// <summary>Points: scale each point by its metric value instead of a fixed size.</summary>
		public bool SizeByMetric { get; set; }
	}

	/// <summary>Visualization-wide display settings.</summary>
	public sealed class MapVisualizationOptions
	{
		public bool ShowStations { get; set; } = true;

		/// <summary>Coverage rings drawn around every station, in <see cref="DistanceUnit"/>. Empty = none.</summary>
		public List<double> StationRings { get; set; } = new List<double>();

		/// <summary>"km" or "mi"; used for rings and the nearest-station metric.</summary>
		public string DistanceUnit { get; set; } = MapVisualizationKeys.DistanceKilometers;

		/// <summary>Distance bands (in <see cref="DistanceUnit"/>) used when coloring by nearest-station distance.</summary>
		public List<double> DistanceBands { get; set; } = new List<double>();

		/// <summary>Minute bands used when coloring by response or turnout time.</summary>
		public List<double> MinuteBands { get; set; } = new List<double>();
	}
}
