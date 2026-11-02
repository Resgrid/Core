using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Resgrid.Providers.GeoLocationProvider.Routing
{
	/// <summary>
	/// Google's encoded polyline algorithm at precision 5 (Google, OSRM default) or 6 (Valhalla, Mapbox polyline6).
	/// Positions are (latitude, longitude).
	/// </summary>
	public static class PolylineCodec
	{
		public static List<(double Latitude, double Longitude)> Decode(string encoded, int precision)
		{
			var points = new List<(double, double)>();

			if (string.IsNullOrEmpty(encoded))
				return points;

			var factor = Math.Pow(10, precision);
			int index = 0, latitude = 0, longitude = 0;

			while (index < encoded.Length)
			{
				latitude += DecodeValue(encoded, ref index);
				if (index > encoded.Length)
					break;
				longitude += DecodeValue(encoded, ref index);
				points.Add((latitude / factor, longitude / factor));
			}

			return points;
		}

		public static string Encode(IEnumerable<(double Latitude, double Longitude)> points, int precision)
		{
			var factor = Math.Pow(10, precision);
			var builder = new StringBuilder();
			long lastLatitude = 0, lastLongitude = 0;

			foreach (var (lat, lon) in points)
			{
				var latitude = (long)Math.Round(lat * factor);
				var longitude = (long)Math.Round(lon * factor);
				EncodeValue(latitude - lastLatitude, builder);
				EncodeValue(longitude - lastLongitude, builder);
				lastLatitude = latitude;
				lastLongitude = longitude;
			}

			return builder.ToString();
		}

		/// <summary>A GeoJSON LineString ([lon, lat] positions) for the decoded points.</summary>
		public static string ToGeoJsonLineString(IReadOnlyList<(double Latitude, double Longitude)> points)
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

		private static int DecodeValue(string encoded, ref int index)
		{
			int result = 0, shift = 0, b;

			do
			{
				if (index >= encoded.Length)
				{
					index++;
					return 0;
				}

				b = encoded[index++] - 63;
				result |= (b & 0x1f) << shift;
				shift += 5;
			} while (b >= 0x20);

			return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
		}

		private static void EncodeValue(long value, StringBuilder builder)
		{
			value = value < 0 ? ~(value << 1) : value << 1;

			while (value >= 0x20)
			{
				builder.Append((char)((0x20 | (value & 0x1f)) + 63));
				value >>= 5;
			}

			builder.Append((char)(value + 63));
		}
	}
}
