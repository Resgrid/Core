using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Resgrid.Model;

namespace Resgrid.Services.Offline
{
	/// <summary>
	/// A department's maximum response area (maplibre-mapping-routing-navigation-plan.md §19.3): GeoJSON polygons in, the
	/// z10 base-map cells covering them out. Holes are ignored (a hole is still covered: coverage only errs larger).
	/// </summary>
	public static class OfflineResponseArea
	{
		/// <summary>Base-map cells are z10 tiles (about 39 km at the equator).</summary>
		public const int CellZoom = 10;

		/// <summary>Sanity bound before the size check.</summary>
		public const double MaxAreaSquareKm = 25000;

		public const int MaxVertices = 5000;

		private const double EarthRadiusKm = 6371.0088;

		/// <summary>Outer rings as (lon, lat), closed. Null when the text is not a usable Polygon/MultiPolygon/Feature(Collection).</summary>
		public static List<List<(double Lon, double Lat)>> Parse(string geoJson, out string error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(geoJson))
			{
				error = "empty";
				return null;
			}

			JToken root;
			try
			{
				root = JToken.Parse(geoJson);
			}
			catch (Exception)
			{
				error = "invalid";
				return null;
			}

			var rings = new List<List<(double, double)>>();
			if (!Collect(root, rings) || rings.Count == 0)
			{
				error = "invalid";
				return null;
			}

			if (rings.Sum(r => r.Count) > MaxVertices)
			{
				error = "too-many-points";
				return null;
			}

			foreach (var ring in rings)
			{
				if (ring.Count < 4 || ring.Any(p => p.Item1 < -180 || p.Item1 > 180 || p.Item2 < -85.05 || p.Item2 > 85.05))
				{
					error = "invalid";
					return null;
				}

				if (IsSelfIntersecting(ring))
				{
					error = "self-intersecting";
					return null;
				}
			}

			return rings;
		}

		private static bool Collect(JToken token, List<List<(double, double)>> rings)
		{
			if (token is not JObject obj)
				return false;

			switch ((string)obj["type"])
			{
				case "FeatureCollection":
					return obj["features"] is JArray features && features.All(f => Collect(f, rings));
				case "Feature":
					return Collect(obj["geometry"], rings);
				case "Polygon":
					return AddPolygon(obj["coordinates"] as JArray, rings);
				case "MultiPolygon":
					return obj["coordinates"] is JArray polygons && polygons.All(p => AddPolygon(p as JArray, rings));
				default:
					return false;
			}
		}

		private static bool AddPolygon(JArray polygon, List<List<(double, double)>> rings)
		{
			if (polygon == null || polygon.Count == 0 || polygon[0] is not JArray outer)
				return false;

			var ring = new List<(double, double)>();
			foreach (var position in outer)
			{
				if (position is not JArray pair || pair.Count < 2)
					return false;

				ring.Add(((double)pair[0], (double)pair[1]));
			}

			if (ring.Count > 0 && ring[0] != ring[^1])
				ring.Add(ring[0]);

			rings.Add(ring);
			return true;
		}

		/// <summary>A circle as a 64-sided polygon ring.</summary>
		public static List<(double Lon, double Lat)> Circle(double latitude, double longitude, double radiusKm, int segments = 64)
		{
			var ring = new List<(double, double)>();
			var angular = radiusKm / EarthRadiusKm;
			var lat1 = latitude * Math.PI / 180;
			var lon1 = longitude * Math.PI / 180;

			for (var i = 0; i < segments; i++)
			{
				var bearing = 2 * Math.PI * i / segments;
				var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angular) + Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing));
				var lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(lat1), Math.Cos(angular) - Math.Sin(lat1) * Math.Sin(lat2));
				ring.Add((NormalizeLon(lon2 * 180 / Math.PI), Math.Clamp(lat2 * 180 / Math.PI, -85.05, 85.05)));
			}

			ring.Add(ring[0]);
			return ring;
		}

		/// <summary>Area of the rings (spherical excess approximation), summed.</summary>
		public static double AreaSquareKm(IEnumerable<List<(double Lon, double Lat)>> rings)
		{
			double total = 0;
			foreach (var ring in rings)
			{
				double sum = 0;
				for (var i = 0; i < ring.Count - 1; i++)
				{
					var (lon1, lat1) = ring[i];
					var (lon2, lat2) = ring[i + 1];
					sum += (lon2 - lon1) * Math.PI / 180 * (2 + Math.Sin(lat1 * Math.PI / 180) + Math.Sin(lat2 * Math.PI / 180));
				}

				total += Math.Abs(sum * EarthRadiusKm * EarthRadiusKm / 2);
			}

			return total;
		}

		public static bool IsSelfIntersecting(List<(double Lon, double Lat)> ring)
		{
			var n = ring.Count - 1;
			for (var i = 0; i < n; i++)
			{
				for (var j = i + 1; j < n; j++)
				{
					// Neighbouring segments share a point; the first and last share the closing point.
					if (j == i + 1 || (i == 0 && j == n - 1))
						continue;

					if (SegmentsCross(ring[i], ring[i + 1], ring[j], ring[j + 1]))
						return true;
				}
			}

			return false;
		}

		/// <summary>The z10 tiles any ring touches, as (x, y).</summary>
		public static List<(int X, int Y)> CoveringCells(IEnumerable<List<(double Lon, double Lat)>> rings, int zoom = CellZoom)
		{
			var cells = new HashSet<(int, int)>();

			foreach (var ring in rings)
			{
				var minX = TileX(ring.Min(p => p.Lon), zoom);
				var maxX = TileX(ring.Max(p => p.Lon), zoom);
				var minY = TileY(ring.Max(p => p.Lat), zoom);
				var maxY = TileY(ring.Min(p => p.Lat), zoom);

				for (var x = minX; x <= maxX; x++)
				{
					for (var y = minY; y <= maxY; y++)
					{
						if (cells.Contains((x, y)))
							continue;

						if (TileTouches(ring, TileBounds(zoom, x, y)))
							cells.Add((x, y));
					}
				}
			}

			return cells.OrderBy(c => c.Item1).ThenBy(c => c.Item2).ToList();
		}

		/// <summary>(west, south, east, north) of a tile.</summary>
		public static (double West, double South, double East, double North) TileBounds(int z, int x, int y)
		{
			var n = Math.Pow(2, z);
			return (x / n * 360 - 180, TileLat(y + 1, n), (x + 1) / n * 360 - 180, TileLat(y, n));
		}

		public static int TileX(double lon, int z)
		{
			var n = 1 << z;
			return Math.Clamp((int)Math.Floor((lon + 180) / 360 * n), 0, n - 1);
		}

		public static int TileY(double lat, int z)
		{
			var n = 1 << z;
			var sin = Math.Sin(Math.Clamp(lat, -85.05, 85.05) * Math.PI / 180);
			return Math.Clamp((int)Math.Floor((0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * n), 0, n - 1);
		}

		private static double TileLat(double y, double n)
		{
			var r = Math.PI - 2 * Math.PI * y / n;
			return 180 / Math.PI * Math.Atan(0.5 * (Math.Exp(r) - Math.Exp(-r)));
		}

		private static bool TileTouches(List<(double Lon, double Lat)> ring, (double West, double South, double East, double North) tile)
		{
			// A ring vertex inside the tile, a tile corner inside the ring, or an edge crossing.
			if (ring.Any(p => p.Lon >= tile.West && p.Lon <= tile.East && p.Lat >= tile.South && p.Lat <= tile.North))
				return true;

			var corners = new[] { (tile.West, tile.South), (tile.East, tile.South), (tile.East, tile.North), (tile.West, tile.North) };
			if (corners.Any(c => GeoMath.IsPointInPolygon(c.Item2, c.Item1, ring.Select(p => new GeoMath.GeoPoint(p.Lat, p.Lon)).ToList())))
				return true;

			for (var i = 0; i < ring.Count - 1; i++)
			{
				for (var c = 0; c < 4; c++)
				{
					if (SegmentsCross(ring[i], ring[i + 1], corners[c], corners[(c + 1) % 4]))
						return true;
				}
			}

			return false;
		}

		private static bool SegmentsCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
		{
			double Cross((double X, double Y) o, (double X, double Y) p, (double X, double Y) q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);

			var d1 = Cross(c, d, a);
			var d2 = Cross(c, d, b);
			var d3 = Cross(a, b, c);
			var d4 = Cross(a, b, d);

			return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
		}

		private static double NormalizeLon(double lon) => ((lon + 540) % 360) - 180;
	}
}
