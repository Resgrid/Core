using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Geocoding;
using Geocoding.Google;
using Geocoding.Microsoft;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Providers.GeoLocationProvider.Routing;

namespace Resgrid.Providers.GeoLocationProvider.Geocoding
{
	/// <summary>Google Geocoding (MappingConfig.GoogleMapsApiKey); today's first choice.</summary>
	public class GoogleGeocodingProvider : IGeocodingProvider
	{
		public string Name => "google";

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.GoogleMapsApiKey);

		public async Task<GeocodeMatch> ForwardAsync(string address, CancellationToken cancellationToken)
		{
			var geocoder = new GoogleGeocoder { ApiKey = MappingConfig.GoogleMapsApiKey };
			var addresses = await geocoder.GeocodeAsync(address);
			var first = addresses?.FirstOrDefault();

			return first == null ? null : new GeocodeMatch { Latitude = first.Coordinates.Latitude, Longitude = first.Coordinates.Longitude, FormattedAddress = first.FormattedAddress };
		}

		public async Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken)
		{
			var geocoder = new GoogleGeocoder { ApiKey = MappingConfig.GoogleMapsApiKey };
			var addresses = await geocoder.ReverseGeocodeAsync(latitude, longitude);
			return addresses?.FirstOrDefault()?.FormattedAddress;
		}
	}

	/// <summary>Bing Maps (MappingConfig.BingMapsApiKey). Microsoft retired Bing Maps Basic keys in 2025; only enterprise keys still answer.</summary>
	public class BingGeocodingProvider : IGeocodingProvider
	{
		public string Name => "bing";

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.BingMapsApiKey);

		public async Task<GeocodeMatch> ForwardAsync(string address, CancellationToken cancellationToken)
		{
			IGeocoder geocoder = new BingMapsGeocoder(MappingConfig.BingMapsApiKey);
			var addresses = await geocoder.GeocodeAsync(address);
			var first = addresses?.FirstOrDefault();

			return first == null ? null : new GeocodeMatch { Latitude = first.Coordinates.Latitude, Longitude = first.Coordinates.Longitude, FormattedAddress = first.FormattedAddress };
		}

		public async Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken)
		{
			IGeocoder geocoder = new BingMapsGeocoder(MappingConfig.BingMapsApiKey);
			var addresses = await geocoder.ReverseGeocodeAsync(latitude, longitude);
			return addresses?.FirstOrDefault()?.FormattedAddress;
		}
	}

	/// <summary>LocationIQ (MappingConfig.LocationIQApiKey) over HTTPS; the key used to travel over plain http.</summary>
	public class LocationIqGeocodingProvider : IGeocodingProvider
	{
		private const string BaseUrl = "https://us1.locationiq.com/v1";

		public string Name => "locationiq";

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.LocationIQApiKey);

		public async Task<GeocodeMatch> ForwardAsync(string address, CancellationToken cancellationToken)
		{
			var url = $"{BaseUrl}/search?key={WebUtility.UrlEncode(MappingConfig.LocationIQApiKey)}&format=json&limit=1&q={WebUtility.UrlEncode(address)}";
			var (ok, _, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

			using (document)
			{
				if (!ok || document == null || document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
					return null;

				return NominatimShape.ReadPlace(document.RootElement[0]);
			}
		}

		public async Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken)
		{
			var url = $"{BaseUrl}/reverse?key={WebUtility.UrlEncode(MappingConfig.LocationIQApiKey)}&format=json&zoom=18" +
			          $"&lat={RoutingHttp.Coordinate(latitude)}&lon={RoutingHttp.Coordinate(longitude)}";
			var (ok, _, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

			using (document)
			{
				return ok && document != null ? OsrmShapeParser.GetString(document.RootElement, "display_name") : null;
			}
		}
	}

	/// <summary>
	/// Nominatim (MappingConfig.NominatimUrl) — self-hosted for production; the public server's usage policy allows
	/// about one request a second and asks for an identifying contact (MappingConfig.NominatimUserAgentEmail).
	/// </summary>
	public class NominatimGeocodingProvider : IGeocodingProvider
	{
		public string Name => "nominatim";

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.NominatimUrl);

		public async Task<GeocodeMatch> ForwardAsync(string address, CancellationToken cancellationToken)
		{
			var url = $"{RoutingHttp.TrimBase(MappingConfig.NominatimUrl)}/search?format=jsonv2&limit=1&addressdetails=0&q={WebUtility.UrlEncode(address)}{Contact()}";
			var (ok, _, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

			using (document)
			{
				if (!ok || document == null || document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
					return null;

				return NominatimShape.ReadPlace(document.RootElement[0]);
			}
		}

		public async Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken)
		{
			var url = $"{RoutingHttp.TrimBase(MappingConfig.NominatimUrl)}/reverse?format=jsonv2&zoom=18" +
			          $"&lat={RoutingHttp.Coordinate(latitude)}&lon={RoutingHttp.Coordinate(longitude)}{Contact()}";
			var (ok, _, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

			using (document)
			{
				return ok && document != null ? OsrmShapeParser.GetString(document.RootElement, "display_name") : null;
			}
		}

		private static string Contact()
		{
			return string.IsNullOrWhiteSpace(MappingConfig.NominatimUserAgentEmail) ? string.Empty : "&email=" + WebUtility.UrlEncode(MappingConfig.NominatimUserAgentEmail);
		}
	}

	/// <summary>
	/// Mapbox Geocoding v6 (MappingConfig.MapboxGeocodingAccessToken). Results are "temporary" under Mapbox's terms
	/// unless the account has the permanent SKU; Resgrid stores call coordinates, so check the terms before using it
	/// first in the chain (plan §16.2).
	/// </summary>
	public class MapboxGeocodingProvider : IGeocodingProvider
	{
		private const string BaseUrl = "https://api.mapbox.com/search/geocode/v6";

		public string Name => "mapbox";

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.MapboxGeocodingAccessToken);

		public async Task<GeocodeMatch> ForwardAsync(string address, CancellationToken cancellationToken)
		{
			var url = $"{BaseUrl}/forward?limit=1&access_token={WebUtility.UrlEncode(MappingConfig.MapboxGeocodingAccessToken)}&q={WebUtility.UrlEncode(address)}";
			var (ok, _, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

			using (document)
			{
				var feature = FirstFeature(ok, document);
				if (feature == null)
					return null;

				var coordinates = feature.Value.GetProperty("geometry").GetProperty("coordinates");
				return new GeocodeMatch
				{
					Longitude = coordinates[0].GetDouble(),
					Latitude = coordinates[1].GetDouble(),
					FormattedAddress = feature.Value.TryGetProperty("properties", out var properties) ? OsrmShapeParser.GetString(properties, "full_address") : null
				};
			}
		}

		public async Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken)
		{
			var url = $"{BaseUrl}/reverse?limit=1&access_token={WebUtility.UrlEncode(MappingConfig.MapboxGeocodingAccessToken)}" +
			          $"&longitude={RoutingHttp.Coordinate(longitude)}&latitude={RoutingHttp.Coordinate(latitude)}";
			var (ok, _, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

			using (document)
			{
				var feature = FirstFeature(ok, document);
				return feature != null && feature.Value.TryGetProperty("properties", out var properties)
					? OsrmShapeParser.GetString(properties, "full_address") ?? OsrmShapeParser.GetString(properties, "name")
					: null;
			}
		}

		private static JsonElement? FirstFeature(bool ok, JsonDocument document)
		{
			if (!ok || document == null || !document.RootElement.TryGetProperty("features", out var features) ||
			    features.ValueKind != JsonValueKind.Array || features.GetArrayLength() == 0)
				return null;

			return features[0];
		}
	}

	internal static class NominatimShape
	{
		/// <summary>A Nominatim/LocationIQ place: "lat"/"lon" as strings, "display_name".</summary>
		public static GeocodeMatch ReadPlace(JsonElement place)
		{
			var lat = OsrmShapeParser.GetString(place, "lat");
			var lon = OsrmShapeParser.GetString(place, "lon");

			if (!double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
			    !double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
				return null;

			return new GeocodeMatch { Latitude = latitude, Longitude = longitude, FormattedAddress = OsrmShapeParser.GetString(place, "display_name") };
		}
	}
}
