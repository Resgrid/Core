using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Resgrid.Model;
using Resgrid.Model.Providers;
using SharpKml.Dom;
using SharpKml.Engine;

namespace Resgrid.Providers.GeoLocationProvider
{
	public class KmlProvider: IKmlProvider
	{
		public List<Coordinates> ImportFile(Stream input, bool isKmz)
		{
			var coordinates = new List<Coordinates>();

			if (input == null)
				return coordinates;

			try
			{
				KmlFile file;
				if (isKmz)
				{
					var kmz = KmzFile.Open(input);
					file = kmz?.GetDefaultKmlFile();
				}
				else
					file = KmlFile.Load(input);

				if (file?.Root is Kml kml)
				{
					ExtractCoordinates(kml, coordinates);

					// Resolve NetworkLinks to external KML/KMZ resources
					var networkLinks = kml.Flatten().OfType<NetworkLink>().ToList();
					foreach (var networkLink in networkLinks)
					{
						try
						{
							ResolveNetworkLink(networkLink, coordinates);
						}
						catch
						{
							// Skip failed network link resolution
						}
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"KmlProvider.ImportFile error: {ex.Message}");
			}

			return coordinates;
		}

		private static void ExtractCoordinates(Kml kml, List<Coordinates> coordinates)
		{
			foreach (var placemark in kml.Flatten().OfType<Placemark>())
			{
				var coords = new Coordinates();
				coords.Name = placemark.Name;

				try
				{
					var bounds = placemark.CalculateBounds();
					if (bounds != null)
					{
						coords.Latitude = bounds.Center.Latitude;
						coords.Longitude = bounds.Center.Longitude;
					}
				}
				catch
				{
					// Skip placemarks that fail bounds calculation
				}

				if (coords.Latitude.HasValue && coords.Longitude.HasValue)
					coordinates.Add(coords);
			}
		}

		private static void ResolveNetworkLink(NetworkLink networkLink, List<Coordinates> coordinates)
		{
			if (networkLink?.Link?.Href == null)
				return;

			var href = networkLink.Link.Href.OriginalString;
			if (string.IsNullOrWhiteSpace(href))
				return;

			// Step 1: URI-based detection — check AbsolutePath for .kmz/.kml suffix
			bool? isKmzFromUri = null;
			if (Uri.TryCreate(href, UriKind.Absolute, out Uri uri))
			{
				var path = uri.AbsolutePath;
				if (path.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
					isKmzFromUri = true;
				else if (path.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
					isKmzFromUri = false;
			}

			using (var httpClient = new HttpClient { Timeout = System.TimeSpan.FromSeconds(30) })
			using (var response = httpClient.GetAsync(href).GetAwaiter().GetResult())
			{
				if (!response.IsSuccessStatusCode)
					return;

				// Step 2: Content-Type detection — fallback when URI is inconclusive
				bool? isKmzFromContentType = null;
				var contentType = response.Content.Headers.ContentType?.MediaType;
				if (string.Equals(contentType, "application/vnd.google-earth.kmz", StringComparison.OrdinalIgnoreCase))
					isKmzFromContentType = true;
				else if (string.Equals(contentType, "application/vnd.google-earth.kml+xml", StringComparison.OrdinalIgnoreCase))
					isKmzFromContentType = false;

				// Download into memory so we can peek bytes and re-read for parsing
				var contentBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

				// Step 3: ZIP magic-byte detection — final fallback
				bool? isKmzFromMagic = null;
				if (contentBytes.Length >= 4)
				{
					// PK\x03\x04 is the ZIP magic number
					isKmzFromMagic = contentBytes[0] == 0x50 && contentBytes[1] == 0x4B
					              && contentBytes[2] == 0x03 && contentBytes[3] == 0x04;
				}

				// Precedence: URI suffix > Content-Type header > magic bytes; default to false
				bool isKmz = isKmzFromUri ?? isKmzFromContentType ?? isKmzFromMagic ?? false;

				using (var ms = new MemoryStream(contentBytes))
				{
					KmlFile file;
					if (isKmz)
					{
						var kmz = KmzFile.Open(ms);
						file = kmz?.GetDefaultKmlFile();
						if (file == null)
							return;
					}
					else
					{
						file = KmlFile.Load(ms);
					}

					if (file?.Root is Kml kml)
					{
						ExtractCoordinates(kml, coordinates);
					}
				}
			}
		}
	}
}
