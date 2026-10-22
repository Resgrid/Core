using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Turns a loaded <see cref="MapVisualizationContext"/> into a <see cref="MapVisualizationResult"/>: one row set per
	/// layer source, each row assigned its category (legend entry) and metric value. Pure: no I/O, no clock.
	/// </summary>
	/// <remarks>
	/// Category keys carry a prefix per dimension (<c>ct:</c> call type, <c>dow:</c> day of week, <c>rb:</c> response band, …)
	/// so the page can localize the ones that are not department data. Named categories (call types, units, people) keep
	/// the <see cref="MaxCategories"/> busiest and fold the rest into <see cref="OtherKey"/>, which keeps a legend readable.
	/// </remarks>
	public static class MapVisualizationBuilder
	{
		public const int MaxCategories = 12;
		public const string OtherKey = "other";
		public const string NoneSuffix = "none";

		/// <summary>Twelve colors that stay apart on street, dark and satellite tiles (Tableau 10 plus two).</summary>
		public static readonly IReadOnlyList<string> CategoricalPalette = new[]
		{
			"#4e79a7", "#f28e2b", "#e15759", "#76b7b2", "#59a14f", "#edc948", "#b07aa1", "#ff9da7", "#9c755f", "#17becf", "#bcbd22", "#8c564b"
		};

		public const string OtherColor = "#9ca3af";
		public const string NoValueColor = "#6b7280";

		private static readonly string[] TrafficRamp = { "#1a9850", "#91cf60", "#d9ef8b", "#fee08b", "#fc8d59", "#d73027", "#a50026" };
		private static readonly string[] SequentialRamp = { "#440154", "#3b528b", "#21918c", "#5ec962", "#fde725" };
		private static readonly string[] TimeOfDayColors = { "#1e3a8a", "#facc15", "#f97316", "#7c3aed" };
		private static readonly string[] MonthColors =
		{
			"#1d4ed8", "#2563eb", "#10b981", "#22c55e", "#84cc16", "#eab308", "#f59e0b", "#f97316", "#ef4444", "#b45309", "#7c3aed", "#3b82f6"
		};

		private static readonly string[] RepeatKeys = { "rp:1", "rp:2-4", "rp:5-9", "rp:10+" };

		private sealed class Row
		{
			public double Latitude;
			public double Longitude;
			public int CallId;
			public MapVisualizationCallFact Call;
			public int UnitId;
			public string UserId;
			public int Status;
			public bool IsUnit;
			public DateTime Local;
			public double? Response;
			public double? Turnout;
			public double? StationDistance;
		}

		private sealed class Category
		{
			public string Key;
			public string Label;
			public string Color;
			public int Order;
			public int Count;
		}

		public static MapVisualizationResult Build(MapVisualizationContext context)
		{
			var definition = context.Definition ?? MapVisualizationDefinitionValidator.Normalize(null);

			var result = new MapVisualizationResult
			{
				StartUtc = context.StartUtc,
				EndUtc = context.EndUtc,
				StartLocal = context.StartLocal,
				EndLocal = context.EndLocal,
				Definition = definition,
				Stations = context.Stations ?? new List<MapVisualizationStation>(),
				Call = context.Call,
				Truncated = context.CallsTruncated,
				CenterLatitude = context.CenterLatitude,
				CenterLongitude = context.CenterLongitude,
				Summary = BuildSummary(context)
			};

			foreach (var layer in definition.Layers)
				result.Layers.Add(BuildLayer(context, definition, layer));

			return result;
		}

		private static MapVisualizationLayerResult BuildLayer(MapVisualizationContext context, MapVisualizationDefinition definition, MapVisualizationLayer layer)
		{
			var layerResult = new MapVisualizationLayerResult
			{
				LayerId = layer.Id,
				Source = layer.Source,
				Display = layer.Display,
				ColorBy = layer.ColorBy,
				Metric = layer.Metric
			};

			if (context.SourceNotices.TryGetValue(layer.Source, out var notices))
				layerResult.Notices.AddRange(notices);

			if (context.SourceWithoutLocation.TryGetValue(layer.Source, out var withoutLocation))
				layerResult.WithoutLocation = withoutLocation;

			if (MapVisualizationKeys.IsTrackSource(layer.Source))
			{
				BuildTracks(context, definition, layer, layerResult);
				return layerResult;
			}

			var rows = RowsFor(context, layer.Source).ToList();
			layerResult.Total = rows.Count;

			var categoryOf = CategoryFunction(context, definition, layer.ColorBy);
			var categories = new Dictionary<string, Category>(StringComparer.Ordinal);
			var rowCategories = new List<Category>(rows.Count);

			foreach (var row in rows)
			{
				var category = categoryOf?.Invoke(row);
				if (category != null)
				{
					if (categories.TryGetValue(category.Key, out var existing))
						category = existing;
					else
						categories[category.Key] = category;

					category.Count++;
				}

				rowCategories.Add(category);
			}

			var ordered = OrderAndFold(categories.Values.ToList(), layer.ColorBy, out var fold);
			var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
			for (var i = 0; i < ordered.Count; i++)
			{
				indexes[ordered[i].Key] = i;
				layerResult.Categories.Add(new MapVisualizationCategory
				{
					Key = ordered[i].Key,
					Label = ordered[i].Label,
					Color = ordered[i].Color,
					Count = ordered[i].Count,
					Order = ordered[i].Order
				});
			}

			var max = MapVisualizationLimits.MaxPointsPerLayer;
			for (var i = 0; i < rows.Count; i++)
			{
				if (layerResult.Points.Count >= max)
				{
					layerResult.Truncated = true;
					break;
				}

				var row = rows[i];
				var categoryIndex = -1;
				if (rowCategories[i] != null)
				{
					var key = fold.TryGetValue(rowCategories[i].Key, out var folded) ? folded : rowCategories[i].Key;
					categoryIndex = indexes.TryGetValue(key, out var index) ? index : -1;
				}

				layerResult.Points.Add(new double?[]
				{
					Math.Round(row.Latitude, 6),
					Math.Round(row.Longitude, 6),
					categoryIndex,
					MetricOf(row, layer.Metric),
					row.CallId
				});
			}

			if (layerResult.Truncated)
				layerResult.Notices.Add(MapVisualizationNotices.LayerTruncated);

			if (NeedsUnitTimes(layer) && context.UnitTimesLoaded && rows.Count > 0 && rows.All(r => r.Response == null && r.Turnout == null))
				layerResult.Notices.Add(MapVisualizationNotices.NoStatusTimes);

			return layerResult;
		}

		/// <summary>True when the layer shows on-scene or responding times, which need the call's unit statuses.</summary>
		public static bool NeedsUnitTimes(MapVisualizationLayer layer) =>
			layer.Metric == MapVisualizationKeys.MetricResponseMinutes || layer.Metric == MapVisualizationKeys.MetricTurnoutMinutes ||
			layer.ColorBy == MapVisualizationKeys.ColorByResponseBand;

		private static IEnumerable<Row> RowsFor(MapVisualizationContext context, string source)
		{
			switch (source)
			{
				case MapVisualizationKeys.SourceCalls:
					foreach (var call in context.Calls.Where(c => c.Latitude.HasValue && c.Longitude.HasValue))
						yield return new Row
						{
							Latitude = call.Latitude.Value, Longitude = call.Longitude.Value, CallId = call.CallId, Call = call, Local = call.LoggedOnLocal,
							Response = call.ResponseMinutes, Turnout = call.TurnoutMinutes, StationDistance = call.StationDistance
						};
					break;
				case MapVisualizationKeys.SourceUnitResponses:
					foreach (var call in context.Calls.Where(c => c.Latitude.HasValue && c.Longitude.HasValue))
						foreach (var unit in call.Units)
							yield return new Row
							{
								Latitude = call.Latitude.Value, Longitude = call.Longitude.Value, CallId = call.CallId, Call = call, Local = call.LoggedOnLocal,
								UnitId = unit.UnitId, IsUnit = true, Response = unit.ResponseMinutes, Turnout = unit.TurnoutMinutes, StationDistance = unit.StationDistance
							};
					break;
				case MapVisualizationKeys.SourcePersonnelResponses:
					foreach (var call in context.Calls.Where(c => c.Latitude.HasValue && c.Longitude.HasValue))
						foreach (var userId in call.Responders)
							yield return new Row
							{
								Latitude = call.Latitude.Value, Longitude = call.Longitude.Value, CallId = call.CallId, Call = call, Local = call.LoggedOnLocal,
								UserId = userId, StationDistance = call.StationDistance
							};
					break;
				case MapVisualizationKeys.SourceResponderOrigins:
					foreach (var status in context.ResponderOrigins)
						yield return StatusRow(status);
					break;
				case MapVisualizationKeys.SourceUnitStatusLocations:
					foreach (var status in context.UnitStatusLocations)
						yield return StatusRow(status);
					break;
			}
		}

		private static Row StatusRow(MapVisualizationStatusFact status) => new Row
		{
			Latitude = status.Latitude,
			Longitude = status.Longitude,
			CallId = status.CallId,
			UnitId = status.UnitId,
			UserId = status.UserId,
			Status = status.Status,
			IsUnit = status.IsUnit,
			Local = status.TimestampLocal,
			StationDistance = status.StationDistance
		};

		private static double? MetricOf(Row row, string metric)
		{
			double? value = metric switch
			{
				MapVisualizationKeys.MetricResponseMinutes => row.Response,
				MapVisualizationKeys.MetricTurnoutMinutes => row.Turnout,
				MapVisualizationKeys.MetricDurationMinutes => row.Call?.DurationMinutes,
				MapVisualizationKeys.MetricUnitsDispatched => row.Call?.UnitsDispatched,
				MapVisualizationKeys.MetricPersonnelResponded => row.Call?.PersonnelResponded,
				MapVisualizationKeys.MetricStationDistance => row.StationDistance,
				MapVisualizationKeys.MetricCallsAtLocation => row.Call?.CallsAtLocation,
				_ => 1
			};

			return value.HasValue ? Math.Round(value.Value, 2) : (double?)null;
		}

		private static void BuildTracks(MapVisualizationContext context, MapVisualizationDefinition definition, MapVisualizationLayer layer, MapVisualizationLayerResult layerResult)
		{
			var isUnit = layer.Source == MapVisualizationKeys.SourceUnitTracks;
			var tracks = isUnit ? context.UnitTracks : context.PersonnelTracks;
			var categoryOf = CategoryFunction(context, definition, layer.ColorBy);
			var categories = new Dictionary<string, Category>(StringComparer.Ordinal);
			var trackCategories = new List<Category>();

			foreach (var track in tracks)
			{
				var category = categoryOf?.Invoke(new Row { UnitId = track.UnitId, UserId = track.UserId, IsUnit = isUnit });
				if (category != null)
				{
					if (categories.TryGetValue(category.Key, out var existing))
						category = existing;
					else
						categories[category.Key] = category;

					category.Count++;
				}

				trackCategories.Add(category);
			}

			var ordered = OrderAndFold(categories.Values.ToList(), layer.ColorBy, out var fold);
			var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
			for (var i = 0; i < ordered.Count; i++)
			{
				indexes[ordered[i].Key] = i;
				layerResult.Categories.Add(new MapVisualizationCategory
				{
					Key = ordered[i].Key, Label = ordered[i].Label, Color = ordered[i].Color, Count = ordered[i].Count, Order = ordered[i].Order
				});
			}

			var budget = MapVisualizationLimits.MaxTrackPoints;
			for (var t = 0; t < tracks.Count; t++)
			{
				var track = tracks[t];
				layerResult.Total += track.Points.Count;

				var categoryIndex = -1;
				if (trackCategories[t] != null)
				{
					var key = fold.TryGetValue(trackCategories[t].Key, out var folded) ? folded : trackCategories[t].Key;
					categoryIndex = indexes.TryGetValue(key, out var index) ? index : -1;
				}

				var label = isUnit ? UnitName(context, track.UnitId) : PersonName(context, track.UserId);
				var id = isUnit ? $"u{track.UnitId}" : $"p{track.UserId}";
				var segment = 0;

				foreach (var part in SplitAtGaps(track.Points))
				{
					if (budget <= 0)
					{
						layerResult.Truncated = true;
						break;
					}

					var coordinates = part.Take(budget)
						.Select(p => new[] { Math.Round(p.Latitude, 6), Math.Round(p.Longitude, 6), (double)new DateTimeOffset(DateTime.SpecifyKind(p.TimestampUtc, DateTimeKind.Utc)).ToUnixTimeSeconds() })
						.ToList();
					budget -= coordinates.Count;

					layerResult.Tracks.Add(new MapVisualizationTrack { Id = $"{id}-{segment++}", Label = label, CategoryIndex = categoryIndex, Coordinates = coordinates });
				}
			}

			if (layerResult.Truncated)
				layerResult.Notices.Add(MapVisualizationNotices.LayerTruncated);
		}

		/// <summary>
		/// Splits a track where the device went quiet for more than 30 minutes, so the map does not draw a straight line across
		/// a gap in the recording, and drops fixes closer than ~10 m to the previous one.
		/// </summary>
		public static IEnumerable<List<(double Latitude, double Longitude, DateTime TimestampUtc)>> SplitAtGaps(
			IReadOnlyList<(double Latitude, double Longitude, DateTime TimestampUtc)> points)
		{
			var current = new List<(double, double, DateTime)>();
			(double Latitude, double Longitude, DateTime TimestampUtc)? previous = null;

			foreach (var point in points.OrderBy(p => p.TimestampUtc))
			{
				if (previous.HasValue)
				{
					if ((point.TimestampUtc - previous.Value.TimestampUtc).TotalMinutes > 30)
					{
						if (current.Count > 0)
							yield return current;

						current = new List<(double, double, DateTime)>();
					}
					else if (GeoMath.HaversineMeters(previous.Value.Latitude, previous.Value.Longitude, point.Latitude, point.Longitude) < 10)
					{
						continue;
					}
				}

				current.Add(point);
				previous = point;
			}

			if (current.Count > 0)
				yield return current;
		}

		#region Categories
		private static bool IsOrderedDimension(string colorBy) => colorBy is MapVisualizationKeys.ColorByTimeOfDay or MapVisualizationKeys.ColorByHourOfDay
			or MapVisualizationKeys.ColorByDayOfWeek or MapVisualizationKeys.ColorByWeekpart or MapVisualizationKeys.ColorByMonth
			or MapVisualizationKeys.ColorByYear or MapVisualizationKeys.ColorByResponseBand or MapVisualizationKeys.ColorByDistanceBand
			or MapVisualizationKeys.ColorByRepeatBand or MapVisualizationKeys.ColorByOutcome;

		/// <summary>
		/// Orders the legend and assigns colors. Ordered dimensions keep their natural order and every bucket. Named ones sort by
		/// count, keep the busiest <see cref="MaxCategories"/> - 1 and fold the rest into "Other" (<paramref name="fold"/> maps
		/// each folded key to it).
		/// </summary>
		private static List<Category> OrderAndFold(List<Category> categories, string colorBy, out Dictionary<string, string> fold)
		{
			fold = new Dictionary<string, string>(StringComparer.Ordinal);

			if (IsOrderedDimension(colorBy))
			{
				var ordered = categories.OrderBy(c => c.Order).ThenBy(c => c.Key, StringComparer.Ordinal).ToList();
				if (colorBy == MapVisualizationKeys.ColorByYear)
					for (var i = 0; i < ordered.Count; i++)
						ordered[i].Color = Ramp(SequentialRamp, i, ordered.Count);
				return ordered;
			}

			var byCount = categories
				.OrderBy(c => c.Key.EndsWith(":" + NoneSuffix, StringComparison.Ordinal) ? 1 : 0)
				.ThenByDescending(c => c.Count)
				.ThenBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)
				.ToList();

			List<Category> kept;
			if (byCount.Count > MaxCategories)
			{
				kept = byCount.Take(MaxCategories - 1).ToList();
				var other = new Category { Key = OtherKey, Label = "Other", Color = OtherColor, Order = int.MaxValue };
				foreach (var folded in byCount.Skip(MaxCategories - 1))
				{
					other.Count += folded.Count;
					fold[folded.Key] = OtherKey;
				}

				kept.Add(other);
			}
			else
			{
				kept = byCount;
			}

			var used = new HashSet<string>(kept.Where(c => !string.IsNullOrWhiteSpace(c.Color)).Select(c => c.Color), StringComparer.OrdinalIgnoreCase);
			var paletteIndex = 0;
			foreach (var category in kept.Where(c => string.IsNullOrWhiteSpace(c.Color)))
			{
				while (paletteIndex < CategoricalPalette.Count && used.Contains(CategoricalPalette[paletteIndex]))
					paletteIndex++;

				category.Color = paletteIndex < CategoricalPalette.Count ? CategoricalPalette[paletteIndex++] : OtherColor;
			}

			return kept;
		}

		private static Func<Row, Category> CategoryFunction(MapVisualizationContext context, MapVisualizationDefinition definition, string colorBy)
		{
			var lookups = context.Lookups ?? new MapVisualizationLookups();
			var options = definition.Options ?? new MapVisualizationOptions();

			switch (colorBy)
			{
				case MapVisualizationKeys.ColorByCallType:
					return row => row.Call == null ? null
						: string.IsNullOrWhiteSpace(row.Call.Type)
							? new Category { Key = "ct:" + NoneSuffix, Label = "No type", Color = NoValueColor }
							: new Category { Key = "ct:" + row.Call.Type.Trim().ToLowerInvariant(), Label = row.Call.Type.Trim() };
				case MapVisualizationKeys.ColorByPriority:
					return row =>
					{
						if (row.Call == null)
							return null;

						var known = lookups.Priorities.TryGetValue(row.Call.Priority, out var priority);
						return new Category
						{
							Key = "p:" + row.Call.Priority.ToString(CultureInfo.InvariantCulture),
							Label = known ? priority.Name : ((CallPriority)Math.Min(Math.Max(row.Call.Priority, 0), 3)).ToString(),
							Color = known ? NormalizeColor(priority.Color) : null
						};
					};
				case MapVisualizationKeys.ColorByOutcome:
					return row => row.Call == null ? null : new Category
					{
						Key = "o:" + row.Call.State.ToString(CultureInfo.InvariantCulture),
						Label = Enum.IsDefined(typeof(CallStates), row.Call.State) ? ((CallStates)row.Call.State).ToString() : row.Call.State.ToString(CultureInfo.InvariantCulture),
						Color = OutcomeColor(row.Call.State),
						Order = row.Call.State
					};
				case MapVisualizationKeys.ColorByGroup:
					return row =>
					{
						if (row.Call == null)
							return null;

						if (!row.Call.HomeGroupId.HasValue || !lookups.Groups.TryGetValue(row.Call.HomeGroupId.Value, out var group))
							return new Category { Key = "g:" + NoneSuffix, Label = "No station", Color = NoValueColor };

						return new Category { Key = "g:" + row.Call.HomeGroupId.Value.ToString(CultureInfo.InvariantCulture), Label = group.Name, Color = NormalizeColor(group.Color) };
					};
				case MapVisualizationKeys.ColorByTimeOfDay:
					return row =>
					{
						var block = row.Local.Hour / 6;
						return new Category { Key = "tod:" + block.ToString(CultureInfo.InvariantCulture), Label = $"{block * 6:00}:00–{block * 6 + 6:00}:00", Color = TimeOfDayColors[block], Order = block };
					};
				case MapVisualizationKeys.ColorByHourOfDay:
					return row => new Category
					{
						Key = "h:" + row.Local.Hour.ToString(CultureInfo.InvariantCulture),
						Label = $"{row.Local.Hour:00}:00",
						Color = HourColor(row.Local.Hour),
						Order = row.Local.Hour
					};
				case MapVisualizationKeys.ColorByDayOfWeek:
					return row =>
					{
						var day = (int)row.Local.DayOfWeek;
						return new Category { Key = "dow:" + day.ToString(CultureInfo.InvariantCulture), Label = row.Local.DayOfWeek.ToString(), Color = CategoricalPalette[day], Order = day == 0 ? 7 : day };
					};
				case MapVisualizationKeys.ColorByWeekpart:
					return row =>
					{
						var weekend = row.Local.DayOfWeek == DayOfWeek.Saturday || row.Local.DayOfWeek == DayOfWeek.Sunday;
						return weekend
							? new Category { Key = "wp:weekend", Label = "Weekend", Color = "#f28e2b", Order = 2 }
							: new Category { Key = "wp:weekday", Label = "Weekday", Color = "#4e79a7", Order = 1 };
					};
				case MapVisualizationKeys.ColorByMonth:
					return row => new Category
					{
						Key = "m:" + row.Local.Month.ToString(CultureInfo.InvariantCulture),
						Label = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(row.Local.Month),
						Color = MonthColors[row.Local.Month - 1],
						Order = row.Local.Month
					};
				case MapVisualizationKeys.ColorByYear:
					return row => new Category
					{
						Key = "y:" + row.Local.Year.ToString(CultureInfo.InvariantCulture),
						Label = row.Local.Year.ToString(CultureInfo.InvariantCulture),
						Order = row.Local.Year
					};
				case MapVisualizationKeys.ColorByUnit:
					return row => row.UnitId <= 0 ? null : new Category
					{
						Key = "u:" + row.UnitId.ToString(CultureInfo.InvariantCulture),
						Label = UnitName(context, row.UnitId)
					};
				case MapVisualizationKeys.ColorByUnitType:
					return row =>
					{
						if (row.UnitId <= 0)
							return null;

						var type = lookups.Units.TryGetValue(row.UnitId, out var unit) ? unit.Type : null;
						return string.IsNullOrWhiteSpace(type)
							? new Category { Key = "ut:" + NoneSuffix, Label = "No type", Color = NoValueColor }
							: new Category { Key = "ut:" + type.Trim().ToLowerInvariant(), Label = type.Trim() };
					};
				case MapVisualizationKeys.ColorByStation:
					return row =>
					{
						if (row.UnitId <= 0)
							return null;

						var stationId = lookups.Units.TryGetValue(row.UnitId, out var unit) ? unit.StationGroupId : null;
						if (!stationId.HasValue || !lookups.Groups.TryGetValue(stationId.Value, out var group))
							return new Category { Key = "s:" + NoneSuffix, Label = "No station", Color = NoValueColor };

						return new Category { Key = "s:" + stationId.Value.ToString(CultureInfo.InvariantCulture), Label = group.Name, Color = NormalizeColor(group.Color) };
					};
				case MapVisualizationKeys.ColorByPerson:
					return row => string.IsNullOrWhiteSpace(row.UserId) ? null : new Category
					{
						Key = "pe:" + row.UserId.ToLowerInvariant(),
						Label = PersonName(context, row.UserId)
					};
				case MapVisualizationKeys.ColorByStatus:
					return row =>
					{
						if (row.UnitId <= 0 && string.IsNullOrWhiteSpace(row.UserId))
							return null;

						if (lookups.CustomStatuses.TryGetValue(row.Status, out var custom))
							return new Category { Key = "st:" + row.Status.ToString(CultureInfo.InvariantCulture), Label = custom.Name, Color = NormalizeColor(custom.Color) };

						return row.IsUnit
							? new Category { Key = "stu:" + row.Status.ToString(CultureInfo.InvariantCulture), Label = Enum.IsDefined(typeof(UnitStateTypes), row.Status) ? ((UnitStateTypes)row.Status).ToString() : row.Status.ToString(CultureInfo.InvariantCulture) }
							: new Category { Key = "stp:" + row.Status.ToString(CultureInfo.InvariantCulture), Label = Enum.IsDefined(typeof(ActionTypes), row.Status) ? ((ActionTypes)row.Status).ToString() : row.Status.ToString(CultureInfo.InvariantCulture) };
					};
				case MapVisualizationKeys.ColorByResponseBand:
					{
						var bands = options.MinuteBands.Count > 0 ? options.MinuteBands : DefaultMinuteBands;
						return row => BandCategory("rb", row.Response, bands);
					}
				case MapVisualizationKeys.ColorByDistanceBand:
					{
						var bands = options.DistanceBands.Count > 0 ? options.DistanceBands : DefaultDistanceBands(options.DistanceUnit);
						return row => BandCategory("db", row.StationDistance, bands);
					}
				case MapVisualizationKeys.ColorByRepeatBand:
					return row =>
					{
						if (row.Call == null)
							return null;

						var count = row.Call.CallsAtLocation;
						var band = count >= 10 ? 3 : count >= 5 ? 2 : count >= 2 ? 1 : 0;
						return new Category { Key = RepeatKeys[band], Label = RepeatKeys[band].Substring(3), Color = Ramp(TrafficRamp, band, RepeatKeys.Length), Order = band };
					};
				default:
					return null;
			}
		}

		public static readonly IReadOnlyList<double> DefaultMinuteBands = new[] { 5d, 10d, 15d, 20d };

		public static IReadOnlyList<double> DefaultDistanceBands(string unit) =>
			unit == MapVisualizationKeys.DistanceMiles ? new[] { 1.5d, 3d, 6d } : new[] { 2.5d, 5d, 10d };

		/// <summary>
		/// Band keys are "rb:0" (below the first bound) … "rb:N" (at or above the last), plus "rb:none" for no value. The label
		/// carries the bounds ("&lt; 4", "4–8", "≥ 20"); the page adds the unit.
		/// </summary>
		private static Category BandCategory(string prefix, double? value, IReadOnlyList<double> bands)
		{
			if (!value.HasValue)
				return new Category { Key = prefix + ":" + NoneSuffix, Label = "—", Color = NoValueColor, Order = int.MaxValue };

			var index = 0;
			while (index < bands.Count && value.Value >= bands[index])
				index++;

			string label;
			if (index == 0)
				label = "< " + Format(bands[0]);
			else if (index == bands.Count)
				label = "≥ " + Format(bands[bands.Count - 1]);
			else
				label = Format(bands[index - 1]) + "–" + Format(bands[index]);

			return new Category
			{
				Key = prefix + ":" + index.ToString(CultureInfo.InvariantCulture),
				Label = label,
				Color = Ramp(TrafficRamp, index, bands.Count + 1),
				Order = index
			};
		}

		private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

		private static string Ramp(IReadOnlyList<string> ramp, int index, int count)
		{
			if (count <= 1)
				return ramp[0];

			var position = (int)Math.Round(index * (ramp.Count - 1) / (double)(count - 1));
			return ramp[Math.Min(Math.Max(position, 0), ramp.Count - 1)];
		}

		private static string HourColor(int hour)
		{
			// Night hours dark, midday bright: the 24 buckets read as a day.
			var distanceFromNoon = Math.Abs(hour - 12) / 12d;
			return Ramp(new[] { "#fde725", "#f59e0b", "#ef4444", "#7c3aed", "#1e3a8a" }, (int)Math.Round(distanceFromNoon * 4), 5);
		}

		private static string OutcomeColor(int state) => state switch
		{
			(int)CallStates.Active => "#2563eb",
			(int)CallStates.Closed => "#059669",
			(int)CallStates.Cancelled => "#6b7280",
			(int)CallStates.Unfounded => "#d97706",
			(int)CallStates.Founded => "#7c3aed",
			(int)CallStates.Minor => "#0891b2",
			(int)CallStates.Transferred => "#db2777",
			(int)CallStates.FalseAlarm => "#dc2626",
			_ => NoValueColor
		};

		/// <summary>A department color as #rrggbb, or null when it is not a usable hex color.</summary>
		public static string NormalizeColor(string color)
		{
			if (string.IsNullOrWhiteSpace(color))
				return null;

			var value = color.Trim();
			if (!value.StartsWith("#", StringComparison.Ordinal))
				value = "#" + value;

			if (value.Length == 4)
				value = "#" + value[1] + value[1] + value[2] + value[2] + value[3] + value[3];

			if (value.Length != 7 || !value.Skip(1).All(Uri.IsHexDigit))
				return null;

			return value.ToLowerInvariant();
		}

		private static string UnitName(MapVisualizationContext context, int unitId) =>
			context.Lookups != null && context.Lookups.Units.TryGetValue(unitId, out var unit) && !string.IsNullOrWhiteSpace(unit.Name)
				? unit.Name
				: "#" + unitId.ToString(CultureInfo.InvariantCulture);

		private static string PersonName(MapVisualizationContext context, string userId) =>
			userId != null && context.Lookups != null && context.Lookups.People.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name)
				? name
				: "—";
		#endregion Categories

		#region Summary
		private static MapVisualizationSummary BuildSummary(MapVisualizationContext context)
		{
			var calls = context.Calls ?? new List<MapVisualizationCallFact>();
			var summary = new MapVisualizationSummary
			{
				Calls = calls.Count,
				CallsWithLocation = calls.Count(c => c.Latitude.HasValue && c.Longitude.HasValue)
			};

			var grid = new int[7][];
			for (var d = 0; d < 7; d++)
				grid[d] = new int[24];

			foreach (var call in calls)
				grid[(int)call.LoggedOnLocal.DayOfWeek][call.LoggedOnLocal.Hour]++;

			summary.ByWeekdayHour = grid;

			if (context.UnitTimesLoaded)
			{
				var times = calls.Where(c => c.ResponseMinutes.HasValue).Select(c => c.ResponseMinutes.Value).OrderBy(v => v).ToList();
				if (times.Count > 0)
				{
					summary.MedianResponseMinutes = Math.Round(Percentile(times, 50), 1);
					summary.P90ResponseMinutes = Math.Round(Percentile(times, 90), 1);
				}
			}

			summary.TopCallTypes = calls
				.GroupBy(c => string.IsNullOrWhiteSpace(c.Type) ? string.Empty : c.Type.Trim(), StringComparer.OrdinalIgnoreCase)
				.Select(g => new MapVisualizationCategory
				{
					Key = g.Key.Length == 0 ? "ct:" + NoneSuffix : "ct:" + g.Key.ToLowerInvariant(),
					Label = g.Key.Length == 0 ? "No type" : g.First().Type.Trim(),
					Count = g.Count()
				})
				.OrderByDescending(c => c.Count)
				.ThenBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)
				.Take(10)
				.ToList();

			return summary;
		}

		/// <summary>Linear-interpolated percentile of ascending <paramref name="sorted"/> values.</summary>
		public static double Percentile(IReadOnlyList<double> sorted, double percentile)
		{
			if (sorted.Count == 0)
				return 0;

			if (sorted.Count == 1)
				return sorted[0];

			var rank = percentile / 100d * (sorted.Count - 1);
			var lower = (int)Math.Floor(rank);
			var upper = (int)Math.Ceiling(rank);
			return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
		}
		#endregion Summary
	}
}
