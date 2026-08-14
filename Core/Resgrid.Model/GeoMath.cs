using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Pure geometry helpers for the dispatch system: station geofence parsing
	/// (DepartmentGroup.Geofence polygon JSON), point-in-polygon containment,
	/// centroid and haversine distance. No I/O, no department context.
	/// </summary>
	public static class GeoMath
	{
		public readonly struct GeoPoint
		{
			public GeoPoint(double latitude, double longitude)
			{
				Latitude = latitude;
				Longitude = longitude;
			}

			public double Latitude { get; }

			public double Longitude { get; }
		}

		/// <summary>
		/// Parses a station geofence stored on DepartmentGroup.Geofence. The current
		/// writer emits [{"lat":39.7,"lng":-104.9},...]; legacy rows from the old
		/// Google Maps drawing tool used [{"k":39.7,"A":-104.9},...] (also seen with
		/// lower-case "a"). Returns null when the JSON is unparseable or describes
		/// fewer than 3 vertices — callers must treat that as "no geofence".
		/// </summary>
		public static List<GeoPoint> ParseGeofence(string geofenceJson)
		{
			if (string.IsNullOrWhiteSpace(geofenceJson))
				return null;

			JArray array;
			try
			{
				array = JArray.Parse(geofenceJson);
			}
			catch (Exception)
			{
				return null;
			}

			var points = new List<GeoPoint>();

			foreach (var token in array)
			{
				if (token.Type != JTokenType.Object)
					return null;

				var obj = (JObject)token;

				var lat = GetNumber(obj, "lat") ?? GetNumber(obj, "k");
				var lon = GetNumber(obj, "lng") ?? GetNumber(obj, "A") ?? GetNumber(obj, "a") ?? GetNumber(obj, "lon");

				if (!lat.HasValue || !lon.HasValue)
					return null;

				points.Add(new GeoPoint(lat.Value, lon.Value));
			}

			if (points.Count < 3)
				return null;

			return points;
		}

		/// <summary>
		/// Ray-casting containment test against a polygon's exterior ring. The ring
		/// does not need to be explicitly closed. Points exactly on an edge may fall
		/// on either side; station fences are hand-drawn so this is acceptable.
		/// </summary>
		public static bool IsPointInPolygon(double latitude, double longitude, IReadOnlyList<GeoPoint> polygon)
		{
			if (polygon == null || polygon.Count < 3)
				return false;

			bool inside = false;

			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
			{
				var pi = polygon[i];
				var pj = polygon[j];

				bool crossesLatitude = (pi.Latitude > latitude) != (pj.Latitude > latitude);

				if (!crossesLatitude)
					continue;

				double intersectLongitude = (pj.Longitude - pi.Longitude) * (latitude - pi.Latitude) / (pj.Latitude - pi.Latitude) + pi.Longitude;

				if (longitude < intersectLongitude)
					inside = !inside;
			}

			return inside;
		}

		/// <summary>
		/// Arithmetic-mean centroid of the polygon vertices. Adequate for the small,
		/// roughly convex fences stations draw; not an area-weighted centroid.
		/// </summary>
		public static GeoPoint Centroid(IReadOnlyList<GeoPoint> polygon)
		{
			if (polygon == null || polygon.Count == 0)
				return new GeoPoint(0, 0);

			double latSum = 0, lonSum = 0;

			foreach (var point in polygon)
			{
				latSum += point.Latitude;
				lonSum += point.Longitude;
			}

			return new GeoPoint(latSum / polygon.Count, lonSum / polygon.Count);
		}

		/// <summary>
		/// Great-circle distance in meters (haversine, R = 6,371,000 m).
		/// </summary>
		public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
		{
			const double earthRadiusMeters = 6371000d;

			double dLat = ToRadians(lat2 - lat1);
			double dLon = ToRadians(lon2 - lon1);

			double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
					   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
					   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

			double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

			return earthRadiusMeters * c;
		}

		/// <summary>
		/// Invariant-culture "lat,long" parser (Call.GeoLocationData, ActionLog
		/// GeoLocationData, DepartmentGroup Latitude/Longitude strings). Returns null
		/// for missing/unparseable input or a 0,0 fix.
		/// </summary>
		public static GeoPoint? ParseCoordinatePair(string latitude, string longitude)
		{
			if (string.IsNullOrWhiteSpace(latitude) || string.IsNullOrWhiteSpace(longitude))
				return null;

			if (!double.TryParse(latitude.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
				return null;

			if (!double.TryParse(longitude.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
				return null;

			if (lat == 0 && lon == 0)
				return null;

			return new GeoPoint(lat, lon);
		}

		/// <summary>
		/// Splits a "lat,long" blob (Call.GeoLocationData convention) into a point.
		/// </summary>
		public static GeoPoint? ParseLatLonString(string geoLocationData)
		{
			if (string.IsNullOrWhiteSpace(geoLocationData))
				return null;

			var parts = geoLocationData.Split(',');

			if (parts.Length != 2)
				return null;

			return ParseCoordinatePair(parts[0], parts[1]);
		}

		private static double? GetNumber(JObject obj, string propertyName)
		{
			if (!obj.TryGetValue(propertyName, StringComparison.Ordinal, out var token))
				return null;

			if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
				return token.Value<double>();

			if (token.Type == JTokenType.String &&
				double.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
				return parsed;

			return null;
		}

		private static double ToRadians(double degrees)
		{
			return degrees * Math.PI / 180d;
		}
	}
}
