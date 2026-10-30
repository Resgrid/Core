using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Xml;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using SharpKml.Base;
using SharpKml.Dom;
using SharpKml.Engine;

namespace Resgrid.Providers.GeoLocationProvider
{
	public class KmlProvider: IKmlProvider
	{
		// A NetworkLink is fetched by the server while the upload waits, so only the first few are followed.
		private const int MaxNetworkLinks = 5;

		// ogr2ogr and QGIS leave <name> empty and carry the name column in ExtendedData unless told which field is the name.
		private static readonly string[] ExtendedDataNameKeys = { "name", "title", "label" };

		// NetworkLink targets come from the uploaded file, so every connection (redirects included) must land on a public address.
		private static readonly HttpClient NetworkLinkClient = new HttpClient(new SocketsHttpHandler
		{
			UseProxy = false,
			MaxAutomaticRedirections = 3,
			ConnectCallback = ConnectToPublicAddressAsync
		})
		{
			Timeout = System.TimeSpan.FromSeconds(30),
			MaxResponseContentBufferSize = 10 * 1024 * 1024
		};

		public KmlImportResult ImportFile(Stream input, bool isKmz)
		{
			var result = new KmlImportResult();

			if (input == null)
				return result;

			try
			{
				var root = KmlDocumentReader.ReadRoot(input);
				if (root == null)
				{
					result.Error = "The file does not contain any KML.";
					return result;
				}

				ExtractCoordinates(root, result);

				// Resolve NetworkLinks to external KML/KMZ resources
				foreach (var networkLink in root.Flatten().OfType<NetworkLink>().Take(MaxNetworkLinks).ToList())
				{
					try
					{
						ResolveNetworkLink(networkLink, result);
					}
					catch
					{
						// Skip failed network link resolution
					}
				}
			}
			catch (XmlException ex)
			{
				result.Error = $"The file is not valid KML: {ex.Message}";
			}
			catch (InvalidDataException)
			{
				result.Error = "The KMZ file is damaged or is not a zip archive.";
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "KmlProvider.ImportFile could not read an uploaded file.");
				result.Error = "The file could not be read as KML or KMZ.";
			}

			return result;
		}

		private static void ExtractCoordinates(Element root, KmlImportResult result)
		{
			foreach (var placemark in root.Flatten().OfType<Placemark>())
			{
				Vector center = null;

				try
				{
					center = placemark.CalculateBounds()?.Center;
				}
				catch
				{
					// Skip placemarks that fail bounds calculation
				}

				if (center == null || !IsValidPosition(center.Latitude, center.Longitude))
				{
					result.SkippedCount++;
					continue;
				}

				result.Coordinates.Add(new Coordinates { Name = NameOf(placemark), Latitude = center.Latitude, Longitude = center.Longitude });
			}
		}

		/// <summary>
		/// Out-of-range values are usually latitude and longitude written the wrong way round; 0,0 is a converter's
		/// placeholder for a row it could not geocode.
		/// </summary>
		private static bool IsValidPosition(double latitude, double longitude) =>
			!double.IsNaN(latitude) && !double.IsNaN(longitude) &&
			latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 &&
			!(latitude == 0 && longitude == 0);

		private static string NameOf(Placemark placemark)
		{
			if (!string.IsNullOrWhiteSpace(placemark.Name))
				return placemark.Name.Trim();

			var data = placemark.ExtendedData;
			if (data == null)
				return null;

			var values = data.Data.Select(d => new KeyValuePair<string, string>(d.Name, d.Value))
				.Concat(data.SchemaData.SelectMany(s => s.SimpleData).Select(d => new KeyValuePair<string, string>(d.Name, d.Text)))
				.Where(v => !string.IsNullOrWhiteSpace(v.Value))
				.ToList();

			foreach (var key in ExtendedDataNameKeys)
			{
				var value = values.FirstOrDefault(v => string.Equals(v.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase)).Value;
				if (value != null)
					return value.Trim();
			}

			return null;
		}

		private static void ResolveNetworkLink(NetworkLink networkLink, KmlImportResult result)
		{
			var href = networkLink?.Link?.Href?.OriginalString;
			if (string.IsNullOrWhiteSpace(href))
				return;

			if (!Uri.TryCreate(href.Trim(), UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
				return;

			using (var response = NetworkLinkClient.GetAsync(uri).GetAwaiter().GetResult())
			{
				if (!response.IsSuccessStatusCode)
					return;

				// The content decides KML or KMZ, as for the upload itself.
				var root = KmlDocumentReader.ReadRoot(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
				if (root != null)
					ExtractCoordinates(root, result);
			}
		}

		private static async System.Threading.Tasks.ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, System.Threading.CancellationToken cancellationToken)
		{
			var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

			// Every address has to pass: a name that answers with one public and one internal address is refused rather than raced.
			if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a)))
				throw new HttpRequestException($"NetworkLink host '{context.DnsEndPoint.Host}' does not resolve to a public address.");

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

		/// <summary>False for anything that is not a routable public address (same ranges as ExternalFeedDestination).</summary>
		private static bool IsPublicAddress(IPAddress address)
		{
			if (address.IsIPv4MappedToIPv6)
				address = address.MapToIPv4();
			if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
				return false;

			if (address.AddressFamily == AddressFamily.InterNetwork)
			{
				var b = address.GetAddressBytes();
				if (b[0] == 0) return false;                                     // 0.0.0.0/8, "this network"
				if (b[0] == 10) return false;                                    // 10.0.0.0/8
				if (b[0] == 127) return false;                                   // loopback
				if (b[0] == 169 && b[1] == 254) return false;                    // link-local, including the cloud metadata address
				if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;        // 172.16.0.0/12
				if (b[0] == 192 && b[1] == 168) return false;                    // 192.168.0.0/16
				if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;          // IETF protocol assignments
				if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;       // carrier-grade NAT
				if (b[0] >= 224) return false;                                   // multicast and reserved
				return true;
			}

			if (address.AddressFamily == AddressFamily.InterNetworkV6)
				return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal);

			return false;
		}
	}
}
