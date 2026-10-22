using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Brings a posted or stored <see cref="MapVisualizationDefinition"/> inside the supported shape: unknown keys fall
	/// back to defaults, keys that do not fit the layer's source are replaced, numbers are clamped and lists are capped.
	/// It never throws on bad input — a definition saved by an older build, or tampered with, still opens.
	/// </summary>
	public static class MapVisualizationDefinitionValidator
	{
		public static MapVisualizationDefinition Normalize(MapVisualizationDefinition definition)
		{
			var result = definition ?? new MapVisualizationDefinition();

			result.Version = MapVisualizationLimits.CurrentDefinitionVersion;
			result.Filters = NormalizeFilters(result.Filters);
			result.Options = NormalizeOptions(result.Options);

			var layers = (result.Layers ?? new List<MapVisualizationLayer>())
				.Where(l => l != null)
				.Take(MapVisualizationLimits.MaxLayers)
				.ToList();

			var usedIds = new HashSet<string>(StringComparer.Ordinal);

			for (var i = 0; i < layers.Count; i++)
			{
				layers[i] = NormalizeLayer(layers[i]);

				var id = layers[i].Id;
				if (string.IsNullOrWhiteSpace(id) || id.Length > 40 || !usedIds.Add(id))
				{
					var n = i + 1;
					while (!usedIds.Add($"layer-{n}"))
						n++;

					layers[i].Id = $"layer-{n}";
				}
			}

			if (layers.Count == 0)
				layers.Add(NormalizeLayer(new MapVisualizationLayer { Id = "layer-1" }));

			result.Layers = layers;

			return result;
		}

		/// <summary>
		/// Resolves the definition's window to UTC instants: a rolling window ends now; a fixed window covers whole local days.
		/// The span is capped at <see cref="MapVisualizationLimits.MaxWindowDays"/>.
		/// </summary>
		public static (DateTime StartUtc, DateTime EndUtc) ResolveWindow(MapVisualizationFilters filters, DateTime nowUtc, Func<DateTime, DateTime> localToUtc)
		{
			filters ??= new MapVisualizationFilters();

			if (filters.LastDays.HasValue || !filters.Start.HasValue || !filters.End.HasValue)
			{
				var days = Clamp(filters.LastDays ?? MapVisualizationLimits.DefaultWindowDays, 1, MapVisualizationLimits.MaxWindowDays);
				return (nowUtc.AddDays(-days), nowUtc);
			}

			var startLocal = filters.Start.Value.Date;
			var endLocal = filters.End.Value.Date.AddDays(1);

			if (endLocal <= startLocal)
				endLocal = startLocal.AddDays(1);

			if ((endLocal - startLocal).TotalDays > MapVisualizationLimits.MaxWindowDays)
				startLocal = endLocal.AddDays(-MapVisualizationLimits.MaxWindowDays);

			return (localToUtc(startLocal), localToUtc(endLocal));
		}

		private static MapVisualizationFilters NormalizeFilters(MapVisualizationFilters filters)
		{
			filters ??= new MapVisualizationFilters();

			if (filters.LastDays.HasValue)
				filters.LastDays = Clamp(filters.LastDays.Value, 1, MapVisualizationLimits.MaxWindowDays);
			else if (!filters.Start.HasValue || !filters.End.HasValue)
				filters.LastDays = MapVisualizationLimits.DefaultWindowDays;

			filters.CallTypes = (filters.CallTypes ?? new List<string>())
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.Select(t => t.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Take(MapVisualizationLimits.MaxFilterValues)
				.ToList();
			filters.Priorities = Ids(filters.Priorities, 0);
			filters.Outcomes = Ids(filters.Outcomes, 0).Where(o => Enum.IsDefined(typeof(CallStates), o)).ToList();
			filters.GroupIds = Ids(filters.GroupIds, 1);
			filters.UnitIds = Ids(filters.UnitIds, 1);
			filters.Hours = Ids(filters.Hours, 0).Where(h => h <= 23).ToList();
			filters.DaysOfWeek = Ids(filters.DaysOfWeek, 0).Where(d => d <= 6).ToList();

			if (filters.CallId.HasValue && filters.CallId.Value <= 0)
				filters.CallId = null;

			filters.MinCallsAtLocation = Clamp(filters.MinCallsAtLocation, 0, 1000);

			return filters;
		}

		private static MapVisualizationOptions NormalizeOptions(MapVisualizationOptions options)
		{
			options ??= new MapVisualizationOptions();

			options.DistanceUnit = options.DistanceUnit == MapVisualizationKeys.DistanceMiles
				? MapVisualizationKeys.DistanceMiles
				: MapVisualizationKeys.DistanceKilometers;
			options.StationRings = Bands(options.StationRings, MapVisualizationLimits.MaxStationRings, 0.1, 100);
			options.DistanceBands = Bands(options.DistanceBands, MapVisualizationLimits.MaxBands, 0.1, 500);
			options.MinuteBands = Bands(options.MinuteBands, MapVisualizationLimits.MaxBands, 0.5, 1440);

			return options;
		}

		private static MapVisualizationLayer NormalizeLayer(MapVisualizationLayer layer)
		{
			layer.Name = string.IsNullOrWhiteSpace(layer.Name) ? null : Truncate(layer.Name.Trim(), 100);
			layer.Source = MapVisualizationKeys.Sources.Contains(layer.Source) ? layer.Source : MapVisualizationKeys.SourceCalls;

			var displays = MapVisualizationKeys.DisplaysFor(layer.Source);
			layer.Display = displays.Contains(layer.Display) ? layer.Display : displays[0];

			var colorBys = MapVisualizationKeys.ColorBysFor(layer.Source);
			layer.ColorBy = colorBys.Contains(layer.ColorBy) ? layer.ColorBy : MapVisualizationKeys.ColorByNone;

			// A heat surface has one color ramp, so a category cannot be shown on it.
			if (layer.Display == MapVisualizationKeys.DisplayHeat)
				layer.ColorBy = MapVisualizationKeys.ColorByNone;

			var metrics = MapVisualizationKeys.MetricsFor(layer.Source);
			layer.Metric = metrics.Contains(layer.Metric) ? layer.Metric : MapVisualizationKeys.MetricCount;

			layer.Aggregate = MapVisualizationKeys.Aggregates.Contains(layer.Aggregate) ? layer.Aggregate : MapVisualizationKeys.AggregateCount;

			// Counting a count is the only thing "count" means; any other metric needs a real aggregate.
			if (layer.Metric == MapVisualizationKeys.MetricCount)
				layer.Aggregate = MapVisualizationKeys.AggregateCount;
			else if (layer.Aggregate == MapVisualizationKeys.AggregateCount)
				layer.Aggregate = MapVisualizationKeys.AggregateMedian;

			var style = layer.Style ?? new MapVisualizationLayerStyle();
			style.Palette = MapVisualizationKeys.Palettes.Contains(style.Palette) ? style.Palette : MapVisualizationKeys.PaletteHeat;
			style.Opacity = Clamp(double.IsNaN(style.Opacity) ? 0.75 : style.Opacity, 0.1, 1);
			style.Radius = Clamp(style.Radius, 5, 60);
			style.Intensity = Clamp(double.IsNaN(style.Intensity) ? 1 : style.Intensity, 0.2, 5);
			style.CellSizeMeters = Clamp(style.CellSizeMeters, 100, 10000);
			style.PointSize = Clamp(style.PointSize, 2, 20);
			layer.Style = style;

			return layer;
		}

		private static List<int> Ids(List<int> values, int min) =>
			(values ?? new List<int>()).Where(v => v >= min).Distinct().Take(MapVisualizationLimits.MaxFilterValues).ToList();

		private static List<double> Bands(List<double> values, int max, double min, double upper) =>
			(values ?? new List<double>())
				.Where(v => !double.IsNaN(v) && !double.IsInfinity(v) && v >= min && v <= upper)
				.Select(v => Math.Round(v, 2))
				.Distinct()
				.OrderBy(v => v)
				.Take(max)
				.ToList();

		private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

		private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

		private static string Truncate(string value, int length) => value.Length <= length ? value : value.Substring(0, length);
	}
}
