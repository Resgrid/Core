using System;
using System.Globalization;
using System.Linq;
using Geocoding;
using Geocoding.Google;
using Geocoding.Microsoft;
using GoogleMapsApi.Entities.Directions.Request;
using GoogleMapsApi.Entities.Directions.Response;
using Resgrid.Model;
using Resgrid.Model.Providers;
using RestSharp;
using System.Net;
using System.Threading.Tasks;
using Resgrid.Providers.Geo.Models.LocationIQ;
using Resgrid.Providers.GeoLocationProvider.Geocoding;
using System.Collections.Generic;
using System.Threading;

namespace Resgrid.Providers.GeoLocationProvider
{
	public class GeoLocationProvider : IGeoLocationProvider
	{
		// Geocode, route and reverse what3words keys are built by GeoLocationCacheKeys.
		private static string W3WCacheKey = "W3W_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(30);

		private readonly ICacheProvider _cacheProvider;
		private readonly IReadOnlyList<IGeocodingProvider> _geocoders;

		public GeoLocationProvider(ICacheProvider cacheProvider)
			: this(cacheProvider, DefaultGeocoders())
		{
		}

		public GeoLocationProvider(ICacheProvider cacheProvider, IEnumerable<IGeocodingProvider> geocoders)
		{
			_cacheProvider = cacheProvider;
			var registered = geocoders?.ToList();
			_geocoders = registered != null && registered.Count > 0 ? registered : DefaultGeocoders().ToList();
		}

		private static IEnumerable<IGeocodingProvider> DefaultGeocoders()
		{
			return new IGeocodingProvider[]
			{
				new GoogleGeocodingProvider(), new LocationIqGeocodingProvider(), new BingGeocodingProvider(),
				new NominatimGeocodingProvider(), new MapboxGeocodingProvider()
			};
		}

		/// <summary>
		/// The geocoders MappingConfig.GeocodingProviders lists, in that order (plan §6.4). The default
		/// "google,locationiq,bing" is the order this provider always used.
		/// </summary>
		private List<IGeocodingProvider> GeocoderChain()
		{
			var names = (Config.MappingConfig.GeocodingProviders ?? string.Empty)
				.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(n => n.Trim().ToLowerInvariant())
				.Distinct();

			return names
				.Select(name => _geocoders.FirstOrDefault(g => g.Name == name))
				.Where(g => g != null && g.IsConfigured)
				.ToList();
		}

		/// <summary>One geocoder call's timeout; the caller disposes it (with <c>using</c>) once the call returns.</summary>
		private static CancellationTokenSource GeocodeTimeout()
		{
			return new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1000, Config.MappingConfig.RoutingTimeoutMs)));
		}

		public async Task<string> GetAproxAddressFromLatLong(double lat, double lon)
		{
			return await GetAddressFromLatLong(lat, lon);
		}

		public async Task<string> GetAddressFromLatLong(double lat, double lon)
		{
			async Task<string> getAddressFromCords()
			{
				foreach (var geocoder in GeocoderChain())
				{
					try
					{
						using var timeout = GeocodeTimeout();
						var address = await geocoder.ReverseAsync(lat, lon, timeout.Token);

						if (!string.IsNullOrWhiteSpace(address))
							return address;
					}
					catch { /* Don't report on GeoLocation failures; the next geocoder in the chain answers */ }
				}

				return null;
			}

			return await _cacheProvider.RetrieveAsync<string>(GeoLocationCacheKeys.ReverseGeocode(lat, lon), getAddressFromCords, CacheLength);
		}

		public async Task<string> GetLatLonFromAddress(string address)
		{
			if (string.IsNullOrWhiteSpace(address))
				return null;

			async Task<string> getCordsFromAddress()
			{
				foreach (var geocoder in GeocoderChain())
				{
					try
					{
						using var timeout = GeocodeTimeout();
						var match = await geocoder.ForwardAsync(address, timeout.Token);

						// Always invariant "lat,lon": many callers split and double.Parse it.
						if (match != null)
							return string.Format(CultureInfo.InvariantCulture, "{0},{1}", match.Latitude, match.Longitude);
					}
					catch { /* Don't report on GeoLocation failures; the next geocoder in the chain answers */ }
				}

				return null;
			}

			return await _cacheProvider.RetrieveAsync<string>(GeoLocationCacheKeys.ForwardGeocode(address), getCordsFromAddress, CacheLength);
		}

		public async Task<RouteInformation> GetRoute(double startLat, double startLon, double endLat, double endLon)
		{
			async Task<RouteInformation> getRoute()
			{
				// Stamped up front so a failure is cached briefly (RouteLookupCache), not for the success length.
				var info = new RouteInformation { ProcessedOn = DateTime.UtcNow };

				try
				{
					DirectionsRequest directionsRequest = new DirectionsRequest()
					{
						Origin = string.Format(CultureInfo.InvariantCulture, "{0},{1}", startLat, startLon),
						Destination = string.Format(CultureInfo.InvariantCulture, "{0},{1}", endLat, endLon),
						ApiKey = Config.MappingConfig.GoogleMapsApiKey
					};
					DirectionsResponse directions = await GoogleMapsApi.GoogleMaps.Directions.QueryAsync(directionsRequest);

					if (directions != null && directions.Status == DirectionsStatusCodes.OK)
					{
						var route = directions.Routes.FirstOrDefault();
						var leg = route?.Legs.FirstOrDefault();

						if (leg != null)
						{
							info.Name = route.Summary;
							info.Successful = true;
							info.TimeString = leg.Duration.Text;
							info.Seconds = leg.Duration.Value.TotalSeconds;
							info.DistanceInMeters = leg.Distance.Value;
						}
					}
				}
				catch
				{
					info.Successful = false;
				}

				return info;
			};

			return await RouteLookupCache.GetAsync(_cacheProvider, GeoLocationCacheKeys.Route(startLat, startLon, endLat, endLon), getRoute);
		}

		public async Task<RouteInformation> GetRoute(string start, string end)
		{
			if (String.IsNullOrWhiteSpace(start) || String.IsNullOrWhiteSpace(end))
				return new RouteInformation();

			async Task<RouteInformation> getRoute()
			{
				// Stamped up front so a failure is cached briefly (RouteLookupCache), not for the success length.
				var info = new RouteInformation { ProcessedOn = DateTime.UtcNow };

				try
				{
					DirectionsRequest directionsRequest = new DirectionsRequest()
					{
						Origin = start,
						Destination = end,
						ApiKey = Config.MappingConfig.GoogleMapsApiKey
					};

					DirectionsResponse directions = await GoogleMapsApi.GoogleMaps.Directions.QueryAsync(directionsRequest);

					if (directions != null && directions.Status == DirectionsStatusCodes.OK)
					{
						var route = directions.Routes.FirstOrDefault();

						if (route != null)
						{
							var leg = route.Legs.FirstOrDefault();

							if (leg != null)
							{
								info.Name = route.Summary;
								info.Successful = true;
								info.TimeString = leg.Duration.Text;
								info.Seconds = leg.Duration.Value.TotalSeconds;
								info.DistanceInMeters = leg.Distance.Value;
							}
						}
					}
				}
				catch
				{
					info.Successful = false;
				}

				return info;
			};

			return await RouteLookupCache.GetAsync(_cacheProvider, GeoLocationCacheKeys.Route(start, end), getRoute);
		}

		public async Task<Coordinates> GetCoordinatesFromW3W(string words)
		{
			Func<Task<Coordinates>> getLocationFromW3W = async () =>
			{
				try
				{
					var client = new RestClient("https://api.what3words.com");
					var request = new RestRequest($"/v3/convert-to-coordinates?key={Config.MappingConfig.What3WordsApiKey}&words={words}", Method.Get);

					var response = await client.ExecuteAsync<W3WResponse>(request);

					if (response.Data != null && response.Data.coordinates != null)
					{
						var coords = new Coordinates();
						coords.Latitude = response.Data.coordinates.lat;
						coords.Longitude = response.Data.coordinates.lng;

						return coords;
					}
				}
				catch { /* Don't report on GeoLocation failures */ }

				return null;
			};

			return await _cacheProvider.RetrieveAsync<Coordinates>(string.Format(W3WCacheKey, words), getLocationFromW3W, CacheLength);
		}

		public async Task<Coordinates> GetCoordinatesFromW3WAsync(string words)
		{
			Func<Task<Coordinates>> getLocationFromW3W = async () =>
			{
				try
				{
					var client = new RestClient("https://api.what3words.com");
					var request = new RestRequest($"/v3/convert-to-coordinates?key={Config.MappingConfig.What3WordsApiKey}&words={words}", Method.Get);

					var response = await client.ExecuteAsync<W3WResponse>(request);

					if (response.Data != null && response.Data.coordinates != null)
					{
						var coords = new Coordinates();
						coords.Latitude = response.Data.coordinates.lat;
						coords.Longitude = response.Data.coordinates.lng;

						return coords;
					}
				}
				catch { /* Don't report on GeoLocation failures */ }

				return null;
			};

			return await _cacheProvider.RetrieveAsync<Coordinates>(string.Format(W3WCacheKey, words), getLocationFromW3W, CacheLength);
		}

		public async Task<string> GetW3WFromCoordinates(Coordinates coordinates)
		{
			if (coordinates?.Latitude == null || coordinates.Longitude == null)
				return null;

			Func<Task<string>> getLocationFromW3W = async () =>
			{
				try
				{
					var client = new RestClient("https://api.what3words.com");
					var request = new RestRequest($"/v3/convert-to-3wa?key={Config.MappingConfig.What3WordsApiKey}&coordinates={GeoLocationCacheKeys.FormatCoordinate(coordinates.Latitude.Value)},{GeoLocationCacheKeys.FormatCoordinate(coordinates.Longitude.Value)}", Method.Get);

					var response = await client.ExecuteAsync<ReverseW3WResponse>(request);

					if (response.Data != null && !String.IsNullOrWhiteSpace(response.Data.words))
					{
						return response.Data.words;
					}
				}
				catch { /* Don't report on GeoLocation failures */ }

				return null;
			};

			return await _cacheProvider.RetrieveAsync<string>(GeoLocationCacheKeys.ReverseW3W(coordinates.Latitude.Value, coordinates.Longitude.Value), getLocationFromW3W, CacheLength);
		}

		public async Task<Coordinates> GetLatLonFromAddressLocationIQ(string address)
		{
			try
			{
				using var timeout = GeocodeTimeout();
				var match = await new LocationIqGeocodingProvider().ForwardAsync(address, timeout.Token);

				if (match != null)
					return new Coordinates { Latitude = match.Latitude, Longitude = match.Longitude };
			}
			catch { }

			return null;
		}

		public async Task<string> GetAddressFromLatLonLocationIQ(string lat, string lon)
		{
			try
			{
				if (double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) &&
					double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
				{
					using var timeout = GeocodeTimeout();
					return await new LocationIqGeocodingProvider().ReverseAsync(latitude, longitude, timeout.Token);
				}
			}
			catch { }

			return null;
		}
	}
}
