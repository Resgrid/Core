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

			using (var httpClient = new HttpClient { Timeout = System.TimeSpan.FromSeconds(30) })
			{
				var response = httpClient.GetAsync(href).GetAwaiter().GetResult();
				if (!response.IsSuccessStatusCode)
					return;

				using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
				{
					bool isKmz = href.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase);

					KmlFile file;
					if (isKmz)
					{
						var kmz = KmzFile.Open(stream);
						file = kmz?.GetDefaultKmlFile();
						if (file == null)
							return;
					}
					else
					{
						file = KmlFile.Load(stream);
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
