using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>
	/// One geometry format for weather alert areas (maplibre-mapping-routing-navigation-plan.md §6.9). NWS and
	/// MeteoAlarm already store GeoJSON; Environment Canada stores CAP polygons ("lat,lon lat,lon ...", several polygons
	/// separated by "|" or new lines). MapLibre clients draw only GeoJSON.
	/// </summary>
	public static class WeatherAlertGeometry
	{
		public static string ToGeoJson(string polygon)
		{
			if (string.IsNullOrWhiteSpace(polygon))
				return null;

			var trimmed = polygon.Trim();

			// Already GeoJSON (a geometry, Feature or FeatureCollection).
			if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
				return trimmed;

			var rings = new List<List<(double Lat, double Lon)>>();
			foreach (var part in trimmed.Split(new[] { '|', '\n', '\r', ';' }, System.StringSplitOptions.RemoveEmptyEntries))
			{
				var ring = new List<(double, double)>();
				foreach (var pair in part.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries))
				{
					var values = pair.Split(',');
					if (values.Length == 2 &&
					    double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
					    double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
					    lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
						ring.Add((lat, lon));
				}

				if (ring.Count >= 3)
				{
					if (ring[0] != ring[ring.Count - 1])
						ring.Add(ring[0]);
					rings.Add(ring);
				}
			}

			if (rings.Count == 0)
				return null;

			var builder = new StringBuilder();
			builder.Append(rings.Count == 1 ? "{\"type\":\"Polygon\",\"coordinates\":" : "{\"type\":\"MultiPolygon\",\"coordinates\":[");

			for (var r = 0; r < rings.Count; r++)
			{
				if (r > 0)
					builder.Append(',');
				builder.Append("[[");
				for (var i = 0; i < rings[r].Count; i++)
				{
					if (i > 0)
						builder.Append(',');
					builder.Append('[').Append(rings[r][i].Lon.ToString("R", CultureInfo.InvariantCulture)).Append(',')
						.Append(rings[r][i].Lat.ToString("R", CultureInfo.InvariantCulture)).Append(']');
				}
				builder.Append("]]");
			}

			builder.Append(rings.Count == 1 ? "}" : "]}");
			return builder.ToString();
		}
	}
}
