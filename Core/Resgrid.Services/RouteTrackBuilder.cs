using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// The driven track of a route instance from the unit's location history (maplibre-mapping-routing-navigation-plan.md
	/// §10.4, RD6): GPS jitter under a few metres is dropped, the distance is summed over what remains, and the line is
	/// simplified with Douglas–Peucker for storage. Pure, so it is unit-tested directly.
	/// </summary>
	public static class RouteTrackBuilder
	{
		private const double JitterMeters = 5;
		private const double SimplifyToleranceMeters = 8;

		public sealed class Track
		{
			/// <summary>GeoJSON LineString string; null when fewer than two distinct points were reported.</summary>
			public string GeometryGeoJson { get; set; }

			public double DistanceMeters { get; set; }

			public int PointCount { get; set; }
		}

		public static Track Build(IEnumerable<(double Latitude, double Longitude, DateTime Timestamp)> locations)
		{
			var ordered = (locations ?? Enumerable.Empty<(double, double, DateTime)>())
				.Where(l => IsValid(l.Item1, l.Item2))
				.OrderBy(l => l.Item3)
				.Select(l => (Latitude: l.Item1, Longitude: l.Item2))
				.ToList();

			var points = new List<(double Latitude, double Longitude)>();
			foreach (var point in ordered)
			{
				if (points.Count > 0 && GeoMath.HaversineMeters(points[^1].Latitude, points[^1].Longitude, point.Latitude, point.Longitude) < JitterMeters)
					continue;

				points.Add(point);
			}

			var track = new Track { PointCount = points.Count };
			if (points.Count < 2)
				return track;

			for (var i = 1; i < points.Count; i++)
				track.DistanceMeters += GeoMath.HaversineMeters(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);

			track.GeometryGeoJson = ToGeoJson(Simplify(points, SimplifyToleranceMeters));
			return track;
		}

		/// <summary>Douglas–Peucker on a local equirectangular projection (metres), keeping the first and last points.</summary>
		public static List<(double Latitude, double Longitude)> Simplify(IReadOnlyList<(double Latitude, double Longitude)> points, double toleranceMeters)
		{
			if (points == null || points.Count < 3)
				return points?.ToList() ?? new List<(double, double)>();

			var originLatitude = points[0].Latitude * Math.PI / 180.0;
			var projected = points.Select(p => (X: p.Longitude * Math.PI / 180.0 * Math.Cos(originLatitude) * 6371000.0, Y: p.Latitude * Math.PI / 180.0 * 6371000.0)).ToList();

			var keep = new bool[points.Count];
			keep[0] = keep[points.Count - 1] = true;

			var stack = new Stack<(int First, int Last)>();
			stack.Push((0, points.Count - 1));

			while (stack.Count > 0)
			{
				var (first, last) = stack.Pop();
				var maxDistance = 0.0;
				var index = -1;

				for (var i = first + 1; i < last; i++)
				{
					var distance = SegmentDistance(projected[i], projected[first], projected[last]);
					if (distance > maxDistance)
					{
						maxDistance = distance;
						index = i;
					}
				}

				if (index > 0 && maxDistance > toleranceMeters)
				{
					keep[index] = true;
					stack.Push((first, index));
					stack.Push((index, last));
				}
			}

			return points.Where((_, i) => keep[i]).ToList();
		}

		private static double SegmentDistance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
		{
			var dx = b.X - a.X;
			var dy = b.Y - a.Y;
			var lengthSquared = dx * dx + dy * dy;

			if (lengthSquared <= 0)
				return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

			var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
			var x = a.X + t * dx;
			var y = a.Y + t * dy;
			return Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
		}

		private static bool IsValid(double latitude, double longitude)
		{
			return latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 && !(latitude == 0 && longitude == 0);
		}

		private static string ToGeoJson(IReadOnlyList<(double Latitude, double Longitude)> points)
		{
			var builder = new StringBuilder("{\"type\":\"LineString\",\"coordinates\":[");

			for (var i = 0; i < points.Count; i++)
			{
				if (i > 0)
					builder.Append(',');

				builder.Append('[')
					.Append(Math.Round(points[i].Longitude, 6).ToString("R", CultureInfo.InvariantCulture))
					.Append(',')
					.Append(Math.Round(points[i].Latitude, 6).ToString("R", CultureInfo.InvariantCulture))
					.Append(']');
			}

			return builder.Append("]}").ToString();
		}
	}
}
