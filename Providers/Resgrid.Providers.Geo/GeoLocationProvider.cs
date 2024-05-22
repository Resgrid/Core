using System;
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

namespace Resgrid.Providers.GeoLocationProvider
{
	public class GeoLocationProvider : IGeoLocationProvider
	{
		private static string ForwardCacheKey = "FGeo_{0}";
		private static string ReverseCacheKey = "RGeo_{0}";
		private static string RouteCacheKey = "Route_{0}_{1}";
		private static string W3WCacheKey = "W3W_{0}";
		private static string ReverseW3WCacheKey = "RW3W_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(30);

		private readonly ICacheProvider _cacheProvider;
		public GeoLocationProvider(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		public async Task<string> GetAproxAddressFromLatLong(double lat, double lon)
		{
			return await GetAddressFromLatLong(lat, lon);
		}

		public async Task<string> GetAddressFromLatLong(double lat, double lon)
		{
			async Task<string> getAddressFromCords()
			{
				string address = null;

				try
				{
					IGeocoder googleGeoCoder = new GoogleGeocoder()
					{
						ApiKey = Config.MappingConfig.GoogleMapsApiKey
					};

					var addresses = await googleGeoCoder.ReverseGeocodeAsync(lat, lon);

					if (addresses != null && addresses.Any())
						address = addresses.First().FormattedAddress;
				}
				catch { /* Don't report on GeoLocation failures */ }

				if (string.IsNullOrWhiteSpace(address))
				{
					try
					{
						var addressGeo = await GetAddressFromLatLonLocationIQ(lat.ToString(), lon.ToString());

						if (!String.IsNullOrWhiteSpace(addressGeo))
							address = addressGeo;
					}
					catch { /* Don't report on GeoLocation failures */ }
				}

				if (string.IsNullOrWhiteSpace(address))
				{
					try
					{
						IGeocoder bingGeoCoder = new BingMapsGeocoder(Config.MappingConfig.BingMapsApiKey);

						var addresses = await bingGeoCoder.ReverseGeocodeAsync(lat, lon);

						if (addresses != null && addresses.Count() > 0)
							address = addresses.First().FormattedAddress;
					}
					catch { /* Don't report on GeoLocation failures */ }
				}
				return address;
			}

			return await _cacheProvider.RetrieveAsync<string>(string.Format(ReverseCacheKey, string.Format("{0} {1}", lat, lon).GetHashCode()), getAddressFromCords, CacheLength);
		}

		public async Task<string> GetLatLonFromAddress(string address)
		{
			async Task<string> getCordsFromAddress()
			{
				string coordinates = null;

				try
				{
					IGeocoder googleGeoCoder = new GoogleGeocoder()
					{
						ApiKey = Config.MappingConfig.GoogleMapsApiKey
					};

					var addresses = await googleGeoCoder.GeocodeAsync(address);

					if (addresses != null && addresses.Any())
					{
						var firstAddress = addresses.First();

						coordinates = string.Format("{0},{1}", firstAddress.Coordinates.Latitude, firstAddress.Coordinates.Longitude);
					}
				}
				catch { /* Don't report on GeoLocation failures */ }

				if (string.IsNullOrWhiteSpace(coordinates))
				{
					try
					{
						var coords = await GetLatLonFromAddressLocationIQ(address);

						if (coords != null)
							coordinates = string.Format("{0},{1}", coords.Latitude, coords.Longitude);
					}
					catch { /* Don't report on GeoLocation failures */ }
				}

				if (string.IsNullOrWhiteSpace(coordinates))
				{
					try
					{
						IGeocoder bingGeoCoder = new BingMapsGeocoder(Config.MappingConfig.BingMapsApiKey);

						var addresses = await bingGeoCoder.GeocodeAsync(address);

						if (addresses != null && addresses.Count() > 0)
							coordinates = string.Format("{0},{1}", addresses.First().Coordinates.Latitude, addresses.First().Coordinates.Longitude);
					}
					catch { /* Don't report on GeoLocation failures */ }
				}

				return coordinates;
			}

			return await _cacheProvider.RetrieveAsync<string>(string.Format(ForwardCacheKey, address.GetHashCode()), getCordsFromAddress, CacheLength);
		}

		public async Task<RouteInformation> GetRoute(double startLat, double startLon, double endLat, double endLon)
		{
			async Task<RouteInformation> getRoute()
			{
				var info = new RouteInformation();

				try
				{
					DirectionsRequest directionsRequest = new DirectionsRequest()
					{
						Origin = string.Format("{0},{1}", startLat, startLon),
						Destination = string.Format("{0},{1}", endLat, endLon),
						ApiKey = Config.MappingConfig.GoogleMapsApiKey
					};
					DirectionsResponse directions = await GoogleMapsApi.GoogleMaps.Directions.QueryAsync(directionsRequest);

					if (directions != null && directions.Status == DirectionsStatusCodes.OK)
					{
						var route = directions.Routes.FirstOrDefault();

						if (route != null)
						{
							info.Name = route.Summary;
							info.ProcessedOn = DateTime.UtcNow;
							info.Successful = true;
						}
					}
				}
				catch
				{
					info.ProcessedOn = DateTime.UtcNow;
					info.Successful = false;
				}

				return info;
			};

			return await _cacheProvider.RetrieveAsync<RouteInformation>(string.Format(RouteCacheKey, string.Format("{0}{1}", startLat, startLon).GetHashCode(), string.Format("{0}{1}", endLat, endLon).GetHashCode()), getRoute, CacheLength);
		}

		public async Task<RouteInformation> GetRoute(string start, string end)
		{
			if (String.IsNullOrWhiteSpace(start) || String.IsNullOrWhiteSpace(end))
				return new RouteInformation();

			async Task<RouteInformation> getRoute()
			{
				var info = new RouteInformation();

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
								info.ProcessedOn = DateTime.UtcNow;
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
					info.ProcessedOn = DateTime.UtcNow;
					info.Successful = false;
				}

				return info;
			};

			return await _cacheProvider.RetrieveAsync<RouteInformation>(string.Format(RouteCacheKey, start.GetHashCode(), end.GetHashCode()), getRoute, CacheLength);
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
			Func<Task<string>> getLocationFromW3W = async () =>
			{
				try
				{
					var client = new RestClient("https://api.what3words.com");
					var request = new RestRequest($"/v3/convert-to-3wa?key={Config.MappingConfig.What3WordsApiKey}&coordinates={$"{coordinates.Latitude},{coordinates.Longitude}"}", Method.Get);

					var response = await client.ExecuteAsync<ReverseW3WResponse>(request);

					if (response.Data != null && !String.IsNullOrWhiteSpace(response.Data.words))
					{
						return response.Data.words;
					}
				}
				catch { /* Don't report on GeoLocation failures */ }

				return null;
			};

			return await _cacheProvider.RetrieveAsync<string>(string.Format(ReverseW3WCacheKey, $"{coordinates.Latitude},{coordinates.Longitude}"), getLocationFromW3W, CacheLength);
		}

		public async Task<Coordinates> GetLatLonFromAddressLocationIQ(string address)
		{
			Coordinates coordinates = new Coordinates();

			try
			{
				var client = new RestClient("http://locationiq.org");
				var request = new RestRequest($"/v1/search.php?key={Config.MappingConfig.LocationIQApiKey}&format=json&q={WebUtility.UrlEncode(address)}", Method.Get);

				var response = await client.ExecuteAsync<dynamic>(request);

				if (response.IsSuccessful && response.Data != null)
				{
					var geocode = response.Data[0];

					double latitude = 0;
					double longitude = 0;

					if (double.TryParse(geocode["lat"], out latitude) &&
							double.TryParse(geocode["lon"], out longitude))
					{
						coordinates.Latitude = latitude;
						coordinates.Longitude = longitude;

						return coordinates;
					}
				}
			}
			catch { }

			return null;
		}

		public async Task<string> GetAddressFromLatLonLocationIQ(string lat, string lon)
		{
			try
			{
				var client = new RestClient("http://locationiq.org");
				var request = new RestRequest($"/v1/reverse.php?key={Config.MappingConfig.LocationIQApiKey}&format=json&lat={WebUtility.UrlEncode(lat)}&lon={WebUtility.UrlEncode(lon)}&zoom=18", Method.Get);

				var response = await client.ExecuteAsync<LocationIQReverseResult>(request);

				if (response.Data != null)
				{
					//var geocode = response.Data[0];

					//return geocode["display_name"];
					return response.Data.display_name;
				}
			}
			catch { }

			return null;
		}
	}
}
