using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Providers.GeoLocationProvider.Routing
{
	/// <summary>
	/// One shared HttpClient for the routing providers (a client per request exhausts sockets). Timeouts come from the
	/// caller's CancellationToken (RoutingService applies MappingConfig.RoutingTimeoutMs), so the client's own timeout is
	/// only a backstop.
	/// </summary>
	internal static class RoutingHttp
	{
		private static readonly HttpClient Client = CreateClient();

		private static HttpClient CreateClient()
		{
			var handler = new SocketsHttpHandler
			{
				PooledConnectionLifetime = TimeSpan.FromMinutes(5),
				AutomaticDecompression = System.Net.DecompressionMethods.All
			};

			var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Resgrid/1.0 (+https://resgrid.com)");
			return client;
		}

		public static async Task<(bool Ok, int Status, JsonDocument Body)> GetJsonAsync(string url, CancellationToken cancellationToken, Action<HttpRequestMessage> configure = null)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			configure?.Invoke(request);
			return await SendAsync(request, cancellationToken);
		}

		public static async Task<(bool Ok, int Status, JsonDocument Body)> PostJsonAsync(string url, object body, CancellationToken cancellationToken, Action<HttpRequestMessage> configure = null)
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, url)
			{
				Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
			};
			configure?.Invoke(request);
			return await SendAsync(request, cancellationToken);
		}

		private static async Task<(bool Ok, int Status, JsonDocument Body)> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
			var text = await response.Content.ReadAsStringAsync(cancellationToken);
			JsonDocument document = null;

			if (!string.IsNullOrWhiteSpace(text))
			{
				try
				{
					document = JsonDocument.Parse(text);
				}
				catch (JsonException)
				{
					document = null;
				}
			}

			return (response.IsSuccessStatusCode, (int)response.StatusCode, document);
		}

		public static string Coordinate(double value)
		{
			return Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
		}

		/// <summary>"lon,lat;lon,lat" as OSRM and Mapbox take coordinates in the URL path.</summary>
		public static string LonLatPath(System.Collections.Generic.IEnumerable<Resgrid.Model.RoutingWaypoint> waypoints)
		{
			var builder = new StringBuilder();

			foreach (var waypoint in waypoints)
			{
				if (builder.Length > 0)
					builder.Append(';');
				builder.Append(Coordinate(waypoint.Longitude)).Append(',').Append(Coordinate(waypoint.Latitude));
			}

			return builder.ToString();
		}

		public static string TrimBase(string url)
		{
			return string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');
		}
	}
}
