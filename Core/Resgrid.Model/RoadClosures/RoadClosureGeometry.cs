using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// A road closure's drawn shape: areas (polygons; only the outer ring counts) and closed road lines. Parsed from the
	/// GeoJSON on <c>RoadClosures.GeometryJson</c> (a geometry, a Feature or a FeatureCollection of Polygon, MultiPolygon,
	/// LineString or MultiLineString), and measured against routes in metres on a local flat projection, which is exact
	/// enough at road-closure scale.
	/// </summary>
	public sealed class RoadClosureShape
	{
		/// <summary>A road line counts as covering anything within this distance of it (half a wide road).</summary>
		public const double LineBufferMeters = 15;

		/// <summary>
		/// A route has to run along a closed line for at least this far to be using it. Crossing the line at a junction
		/// overlaps the buffer by about its width and doesn't count: the closed road is not the one being driven.
		/// </summary>
		public const double MinimumLineOverlapMeters = 40;

		/// <summary>Routes are sampled every this many metres when measured against a shape.</summary>
		public const double SampleStepMeters = 10;

		public const int MaxVertices = 5000;

		public List<List<GeoMath.GeoPoint>> Polygons { get; } = new List<List<GeoMath.GeoPoint>>();

		public List<List<GeoMath.GeoPoint>> Lines { get; } = new List<List<GeoMath.GeoPoint>>();

		public bool IsEmpty => Polygons.Count == 0 && Lines.Count == 0;

		public int VertexCount => Polygons.Sum(p => p.Count) + Lines.Sum(l => l.Count);

		public double MinLatitude { get; private set; }
		public double MaxLatitude { get; private set; }
		public double MinLongitude { get; private set; }
		public double MaxLongitude { get; private set; }

		/// <summary>
		/// Parses the GeoJSON; false (with an English reason for logs) when it is unreadable, holds no usable shape, or is too
		/// big. Polygon rings need three distinct points and lines two; coordinates must be valid WGS84.
		/// </summary>
		public static bool TryParse(string geoJson, out RoadClosureShape shape, out string error)
		{
			shape = null;
			error = null;

			if (string.IsNullOrWhiteSpace(geoJson))
			{
				error = "No shape was drawn.";
				return false;
			}

			JToken root;
			try
			{
				root = JToken.Parse(geoJson);
			}
			catch (Exception)
			{
				error = "The shape is not valid GeoJSON.";
				return false;
			}

			var parsed = new RoadClosureShape();
			if (!parsed.Read(root, 0, out error))
				return false;

			if (parsed.IsEmpty)
			{
				error = "Draw at least one area or road line.";
				return false;
			}

			if (parsed.VertexCount > MaxVertices)
			{
				error = $"The shape has more than {MaxVertices} points.";
				return false;
			}

			parsed.ComputeBounds();
			shape = parsed;
			return true;
		}

		/// <summary>Parses, or returns null for an unreadable shape (stored rows were validated when saved).</summary>
		public static RoadClosureShape ParseOrNull(string geoJson)
		{
			return TryParse(geoJson, out var shape, out _) ? shape : null;
		}

		/// <summary>The center of the shape's bounds, for list maps and zooming.</summary>
		public GeoMath.GeoPoint Center => new GeoMath.GeoPoint((MinLatitude + MaxLatitude) / 2, (MinLongitude + MaxLongitude) / 2);

		/// <summary>True when the point is inside an area or within <see cref="LineBufferMeters"/> of a road line.</summary>
		public bool Contains(double latitude, double longitude)
		{
			return InPolygon(latitude, longitude) || NearLine(latitude, longitude, LineBufferMeters);
		}

		/// <summary>True when the box around the shape, grown by <paramref name="marginMeters"/>, overlaps the box of the two points.</summary>
		public bool IntersectsCorridor(GeoMath.GeoPoint from, GeoMath.GeoPoint to, double marginMeters)
		{
			var latMargin = marginMeters / MetersPerDegreeLatitude;
			var lonMargin = marginMeters / MetersPerDegreeLongitude((from.Latitude + to.Latitude) / 2);

			var minLat = Math.Min(from.Latitude, to.Latitude) - latMargin;
			var maxLat = Math.Max(from.Latitude, to.Latitude) + latMargin;
			var minLon = Math.Min(from.Longitude, to.Longitude) - lonMargin;
			var maxLon = Math.Max(from.Longitude, to.Longitude) + lonMargin;

			return MaxLatitude >= minLat && MinLatitude <= maxLat && MaxLongitude >= minLon && MinLongitude <= maxLon;
		}

		/// <summary>
		/// How far the route runs inside the closure, and where it first goes in. Areas count every metre inside; a road line
		/// counts only stretches of at least <see cref="MinimumLineOverlapMeters"/> along it, so crossing it doesn't.
		/// </summary>
		public RoadClosureRouteMeasure Measure(IReadOnlyList<GeoMath.GeoPoint> route)
		{
			var measure = new RoadClosureRouteMeasure();
			if (route == null || route.Count < 2 || IsEmpty)
				return measure;

			var lonMargin = (LineBufferMeters * 2) / MetersPerDegreeLongitude((MinLatitude + MaxLatitude) / 2);
			var latMargin = (LineBufferMeters * 2) / MetersPerDegreeLatitude;

			double areaRun = 0, lineRun = 0;
			GeoMath.GeoPoint? areaRunStart = null, lineRunStart = null;

			void EndAreaRun()
			{
				if (areaRun > 0)
				{
					measure.MetersInside += areaRun;
					if (areaRunStart.HasValue)
						measure.EntryPoints.Add(areaRunStart.Value);
				}

				areaRun = 0;
				areaRunStart = null;
			}

			void EndLineRun()
			{
				if (lineRun >= MinimumLineOverlapMeters)
				{
					measure.MetersInside += lineRun;
					if (lineRunStart.HasValue)
						measure.EntryPoints.Add(lineRunStart.Value);
				}

				lineRun = 0;
				lineRunStart = null;
			}

			for (var i = 1; i < route.Count; i++)
			{
				var a = route[i - 1];
				var b = route[i];
				var length = GeoMath.HaversineMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
				if (length <= 0)
					continue;

				var steps = Math.Max(1, (int)Math.Ceiling(length / SampleStepMeters));
				var step = length / steps;

				for (var s = 0; s < steps; s++)
				{
					var t = (s + 0.5) / steps;
					var lat = a.Latitude + (b.Latitude - a.Latitude) * t;
					var lon = a.Longitude + (b.Longitude - a.Longitude) * t;
					var point = new GeoMath.GeoPoint(lat, lon);

					var nearBounds = lat >= MinLatitude - latMargin && lat <= MaxLatitude + latMargin &&
						lon >= MinLongitude - lonMargin && lon <= MaxLongitude + lonMargin;

					if (nearBounds && InPolygon(lat, lon))
					{
						areaRunStart ??= point;
						areaRun += step;
					}
					else
					{
						EndAreaRun();
					}

					if (nearBounds && NearLine(lat, lon, LineBufferMeters))
					{
						lineRunStart ??= point;
						lineRun += step;
					}
					else
					{
						EndLineRun();
					}
				}
			}

			EndAreaRun();
			EndLineRun();
			return measure;
		}

		/// <summary>
		/// Points along the closed road lines for routers that exclude by point (each is snapped to the nearest road). They sit
		/// between the drawn vertices, never on them: a vertex is usually a junction, where the nearest road could be the cross
		/// street. At most <paramref name="maxPoints"/>, spread evenly.
		/// </summary>
		public List<GeoMath.GeoPoint> LineExclusionPoints(int maxPoints, double spacingMeters = 60)
		{
			var points = new List<GeoMath.GeoPoint>();
			if (maxPoints <= 0)
				return points;

			foreach (var line in Lines)
			{
				for (var i = 1; i < line.Count; i++)
				{
					var a = line[i - 1];
					var b = line[i];
					var length = GeoMath.HaversineMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
					var count = Math.Max(1, (int)Math.Floor(length / spacingMeters));

					for (var k = 0; k < count; k++)
					{
						var t = (k + 0.5) / count;
						points.Add(new GeoMath.GeoPoint(a.Latitude + (b.Latitude - a.Latitude) * t, a.Longitude + (b.Longitude - a.Longitude) * t));
					}
				}
			}

			if (points.Count <= maxPoints)
				return points;

			var stride = (double)points.Count / maxPoints;
			return Enumerable.Range(0, maxPoints).Select(i => points[(int)Math.Floor(i * stride)]).ToList();
		}

		/// <summary>The shape as GeoJSON (a FeatureCollection of its areas and lines), for maps.</summary>
		public static string Normalize(string geoJson)
		{
			if (!TryParse(geoJson, out var shape, out _))
				return null;

			var features = new JArray();
			foreach (var polygon in shape.Polygons)
			{
				var ring = new JArray(polygon.Select(p => new JArray(Round(p.Longitude), Round(p.Latitude))));
				ring.Add(new JArray(Round(polygon[0].Longitude), Round(polygon[0].Latitude)));

				// Not new JArray(ring): that is JArray's copy constructor and would drop the ring nesting.
				var rings = new JArray();
				rings.Add(ring);
				features.Add(Feature("Polygon", rings));
			}

			foreach (var line in shape.Lines)
				features.Add(Feature("LineString", new JArray(line.Select(p => new JArray(Round(p.Longitude), Round(p.Latitude))))));

			return new JObject { ["type"] = "FeatureCollection", ["features"] = features }.ToString(Newtonsoft.Json.Formatting.None);
		}

		private static JObject Feature(string type, JArray coordinates)
		{
			return new JObject
			{
				["type"] = "Feature",
				["properties"] = new JObject(),
				["geometry"] = new JObject { ["type"] = type, ["coordinates"] = coordinates }
			};
		}

		private static double Round(double value) => Math.Round(value, 7);

		private bool Read(JToken token, int depth, out string error)
		{
			error = null;
			if (depth > 4 || !(token is JObject obj))
			{
				error = "The shape is not valid GeoJSON.";
				return false;
			}

			var type = obj.Value<string>("type") ?? string.Empty;
			switch (type)
			{
				case "FeatureCollection":
					foreach (var feature in obj["features"] as JArray ?? new JArray())
						if (!Read(feature, depth + 1, out error))
							return false;
					return true;
				case "Feature":
					return obj["geometry"] == null || obj["geometry"].Type == JTokenType.Null || Read(obj["geometry"], depth + 1, out error);
				case "GeometryCollection":
					foreach (var geometry in obj["geometries"] as JArray ?? new JArray())
						if (!Read(geometry, depth + 1, out error))
							return false;
					return true;
				case "Polygon":
					return AddPolygon(obj["coordinates"] as JArray, out error);
				case "MultiPolygon":
					foreach (var polygon in obj["coordinates"] as JArray ?? new JArray())
						if (!AddPolygon(polygon as JArray, out error))
							return false;
					return true;
				case "LineString":
					return AddLine(obj["coordinates"] as JArray, out error);
				case "MultiLineString":
					foreach (var line in obj["coordinates"] as JArray ?? new JArray())
						if (!AddLine(line as JArray, out error))
							return false;
					return true;
				default:
					// Points and other shapes (a label, a marker) carry no closure; they are ignored, not refused.
					return true;
			}
		}

		private bool AddPolygon(JArray rings, out string error)
		{
			error = null;
			var outer = ReadPositions(rings?.FirstOrDefault() as JArray, out error);
			if (outer == null)
				return false;

			if (outer.Count > 1 && SamePoint(outer[0], outer[outer.Count - 1]))
				outer.RemoveAt(outer.Count - 1);

			if (outer.Count < 3)
			{
				error = "An area needs at least three points.";
				return false;
			}

			Polygons.Add(outer);
			return true;
		}

		private bool AddLine(JArray positions, out string error)
		{
			var line = ReadPositions(positions, out error);
			if (line == null)
				return false;

			if (line.Count < 2)
			{
				error = "A road line needs at least two points.";
				return false;
			}

			Lines.Add(line);
			return true;
		}

		private static List<GeoMath.GeoPoint> ReadPositions(JArray positions, out string error)
		{
			error = null;
			if (positions == null)
			{
				error = "The shape has no coordinates.";
				return null;
			}

			var points = new List<GeoMath.GeoPoint>();
			foreach (var position in positions)
			{
				if (!(position is JArray pair) || pair.Count < 2 ||
					!TryNumber(pair[0], out var lon) || !TryNumber(pair[1], out var lat) ||
					lat < -90 || lat > 90 || lon < -180 || lon > 180)
				{
					error = "The shape has a coordinate that is not a valid longitude and latitude.";
					return null;
				}

				var point = new GeoMath.GeoPoint(lat, lon);
				if (points.Count == 0 || !SamePoint(points[points.Count - 1], point))
					points.Add(point);
			}

			return points;
		}

		private static bool TryNumber(JToken token, out double value)
		{
			value = 0;
			if (token == null)
				return false;

			if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
			{
				value = token.Value<double>();
				return !double.IsNaN(value) && !double.IsInfinity(value);
			}

			return token.Type == JTokenType.String &&
				double.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
				!double.IsNaN(value) && !double.IsInfinity(value);
		}

		private static bool SamePoint(GeoMath.GeoPoint a, GeoMath.GeoPoint b)
		{
			return Math.Abs(a.Latitude - b.Latitude) < 1e-9 && Math.Abs(a.Longitude - b.Longitude) < 1e-9;
		}

		private void ComputeBounds()
		{
			var all = Polygons.SelectMany(p => p).Concat(Lines.SelectMany(l => l)).ToList();
			MinLatitude = all.Min(p => p.Latitude);
			MaxLatitude = all.Max(p => p.Latitude);
			MinLongitude = all.Min(p => p.Longitude);
			MaxLongitude = all.Max(p => p.Longitude);
		}

		private bool InPolygon(double latitude, double longitude)
		{
			foreach (var polygon in Polygons)
				if (GeoMath.IsPointInPolygon(latitude, longitude, polygon))
					return true;

			return false;
		}

		private bool NearLine(double latitude, double longitude, double meters)
		{
			if (Lines.Count == 0)
				return false;

			var kx = MetersPerDegreeLongitude(latitude);
			const double ky = MetersPerDegreeLatitude;

			foreach (var line in Lines)
			{
				for (var i = 1; i < line.Count; i++)
				{
					// Flat projection centred on the sampled point.
					var ax = (line[i - 1].Longitude - longitude) * kx;
					var ay = (line[i - 1].Latitude - latitude) * ky;
					var bx = (line[i].Longitude - longitude) * kx;
					var by = (line[i].Latitude - latitude) * ky;

					var dx = bx - ax;
					var dy = by - ay;
					var lengthSquared = dx * dx + dy * dy;
					var t = lengthSquared > 0 ? Math.Max(0, Math.Min(1, -(ax * dx + ay * dy) / lengthSquared)) : 0;
					var px = ax + t * dx;
					var py = ay + t * dy;

					if (px * px + py * py <= meters * meters)
						return true;
				}
			}

			return false;
		}

		private const double MetersPerDegreeLatitude = 111320d;

		private static double MetersPerDegreeLongitude(double latitude)
		{
			return Math.Max(1d, 111320d * Math.Cos(latitude * Math.PI / 180d));
		}
	}

	/// <summary>How a route meets one closure.</summary>
	public sealed class RoadClosureRouteMeasure
	{
		/// <summary>Metres of the route inside the area or along the closed road.</summary>
		public double MetersInside { get; set; }

		/// <summary>The first sampled point of each stretch inside: where the route goes in.</summary>
		public List<GeoMath.GeoPoint> EntryPoints { get; } = new List<GeoMath.GeoPoint>();

		public bool Crosses => MetersInside > 0;
	}
}
