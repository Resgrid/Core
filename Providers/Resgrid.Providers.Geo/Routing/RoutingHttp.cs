using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
		private static readonly HttpClient Client = CreateClient(false);

		// Department-supplied URLs (their own Valhalla or OSRM): every connection must land on a public address, so a
		// department can't point the server at the install's private network. No redirects, which would bypass the check.
		private static readonly HttpClient PublicOnlyClient = CreateClient(true);

		private static HttpClient CreateClient(bool publicOnly)
		{
			var handler = new SocketsHttpHandler
			{
				PooledConnectionLifetime = TimeSpan.FromMinutes(5),
				AutomaticDecompression = System.Net.DecompressionMethods.All
			};

			if (publicOnly)
			{
				handler.UseProxy = false;
				handler.AllowAutoRedirect = false;
				handler.ConnectCallback = ConnectToPublicAddressAsync;
			}

			var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Resgrid/1.0 (+https://resgrid.com)");

			// A department's server is not ours: its whole answer is buffered before parsing, so it is capped well above any
			// real route or matrix (a 25-stop route with voice instructions is a few MB) and far below the 2 GB default. Over
			// the cap the call fails and the install's providers answer.
			if (publicOnly)
				client.MaxResponseContentBufferSize = DepartmentResponseMaxBytes;

			return client;
		}

		/// <summary>Largest answer read from a department's own routing server.</summary>
		public const long DepartmentResponseMaxBytes = 32L * 1024 * 1024;

		public static async Task<(bool Ok, int Status, JsonDocument Body)> GetJsonAsync(string url, CancellationToken cancellationToken, Action<HttpRequestMessage> configure = null, bool publicOnly = false)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			configure?.Invoke(request);
			return await SendAsync(request, cancellationToken, publicOnly);
		}

		public static async Task<(bool Ok, int Status, JsonDocument Body)> PostJsonAsync(string url, object body, CancellationToken cancellationToken, Action<HttpRequestMessage> configure = null, bool publicOnly = false)
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, url)
			{
				Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
			};
			configure?.Invoke(request);
			return await SendAsync(request, cancellationToken, publicOnly);
		}

		/// <summary>True for an absolute https URL with no user info, the only form a department may give for its own server.</summary>
		public static bool IsAcceptableDepartmentUrl(string url)
		{
			return Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) &&
			       uri.Scheme == Uri.UriSchemeHttps &&
			       string.IsNullOrEmpty(uri.UserInfo) &&
			       string.IsNullOrEmpty(uri.Fragment);
		}

		private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
		{
			var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

			// Every address has to pass: a name answering with one public and one internal address is refused, not raced.
			if (addresses.Length == 0 || addresses.Any(a => !Resgrid.Framework.PublicNetworkAddress.IsPublic(a)))
				throw new HttpRequestException($"Routing host '{context.DnsEndPoint.Host}' does not resolve to a public address.");

			var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
			try
			{
				await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
				return new NetworkStream(socket, ownsSocket: true);
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}

		private static async Task<(bool Ok, int Status, JsonDocument Body)> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool publicOnly)
		{
			if (publicOnly && request.RequestUri?.Scheme != Uri.UriSchemeHttps)
				throw new HttpRequestException("A department's routing server must use https.");

			using var response = await (publicOnly ? PublicOnlyClient : Client).SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
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
