using System.Collections.Generic;
using System.Text.Json;
using Resgrid.Model;

namespace Resgrid.Providers.GeoLocationProvider.Routing
{
	/// <summary>
	/// Reads the OSRM v5 response shape that OSRM, Mapbox Directions and Valhalla (format=osrm) all return. Geometry is
	/// requested as polyline6 from all three, so the stored route object can be handed to clients as is.
	/// </summary>
	public static class OsrmShapeParser
	{
		public static RouteResult Parse(JsonDocument document, string provider, string attribution, bool keepNormalized)
		{
			var result = new RouteResult { Provider = provider, Attribution = attribution };

			if (document == null)
			{
				result.Error = "Empty response";
				return result;
			}

			var root = document.RootElement;

			if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && code.GetString() != "Ok")
			{
				result.Error = root.TryGetProperty("message", out var message) ? $"{code.GetString()}: {message}" : code.GetString();
				return result;
			}

			if (!root.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array || routes.GetArrayLength() == 0)
			{
				result.Error = "No route";
				return result;
			}

			var route = routes[0];
			result.DistanceMeters = GetDouble(route, "distance");
			result.DurationSeconds = GetDouble(route, "duration");

			if (route.TryGetProperty("duration_typical", out var typical) && typical.ValueKind == JsonValueKind.Number)
				result.TypicalDurationSeconds = typical.GetDouble();

			List<(double Latitude, double Longitude)> points = null;
			if (route.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.String)
			{
				points = PolylineCodec.Decode(geometry.GetString(), 6);
				result.GeometryGeoJson = PolylineCodec.ToGeoJsonLineString(points);
			}

			if (route.TryGetProperty("legs", out var legs) && legs.ValueKind == JsonValueKind.Array)
			{
				foreach (var leg in legs.EnumerateArray())
				{
					result.Legs.Add(new RouteLegSummary
					{
						DistanceMeters = GetDouble(leg, "distance"),
						DurationSeconds = GetDouble(leg, "duration")
					});

					if (leg.TryGetProperty("incidents", out var incidents) && incidents.ValueKind == JsonValueKind.Array)
					{
						foreach (var incident in incidents.EnumerateArray())
							result.Incidents.Add(ReadIncident(incident, points));
					}
				}
			}

			if (keepNormalized)
				result.NormalizedJson = route.GetRawText();

			result.Successful = true;
			return result;
		}

		private static RouteIncident ReadIncident(JsonElement incident, List<(double Latitude, double Longitude)> points)
		{
			var item = new RouteIncident
			{
				Type = GetString(incident, "type"),
				Description = GetString(incident, "description"),
				Impact = GetString(incident, "impact"),
				Closed = incident.TryGetProperty("closed", out var closed) && closed.ValueKind == JsonValueKind.True
			};

			if (points != null && incident.TryGetProperty("geometry_index_start", out var start) && start.ValueKind == JsonValueKind.Number)
			{
				var index = start.GetInt32();
				if (index >= 0 && index < points.Count)
				{
					item.Latitude = points[index].Latitude;
					item.Longitude = points[index].Longitude;
				}
			}

			return item;
		}

		public static double GetDouble(JsonElement element, string name)
		{
			return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
		}

		public static string GetString(JsonElement element, string name)
		{
			return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
		}

		/// <summary>OSRM/Mapbox table annotations ("durations"/"distances") as a jagged nullable matrix.</summary>
		public static double?[][] ReadTable(JsonElement root, string name)
		{
			if (!root.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array)
				return null;

			var table = new double?[rows.GetArrayLength()][];
			var r = 0;

			foreach (var row in rows.EnumerateArray())
			{
				var values = new double?[row.GetArrayLength()];
				var c = 0;

				foreach (var cell in row.EnumerateArray())
					values[c++] = cell.ValueKind == JsonValueKind.Number ? cell.GetDouble() : (double?)null;

				table[r++] = values;
			}

			return table;
		}

		/// <summary>The visiting order from an OSRM trip / Mapbox optimization response (waypoints[i].waypoint_index).</summary>
		public static List<int> ReadTripOrder(JsonElement root)
		{
			if (!root.TryGetProperty("waypoints", out var waypoints) || waypoints.ValueKind != JsonValueKind.Array)
				return null;

			var count = waypoints.GetArrayLength();
			var order = new int[count];
			var input = 0;

			foreach (var waypoint in waypoints.EnumerateArray())
			{
				if (!waypoint.TryGetProperty("waypoint_index", out var position) || position.ValueKind != JsonValueKind.Number)
					return null;

				var slot = position.GetInt32();
				if (slot < 0 || slot >= count)
					return null;

				order[slot] = input++;
			}

			return new List<int>(order);
		}
	}
}
