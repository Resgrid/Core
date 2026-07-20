using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Weather
{
	/// <summary>
	/// Real-time incident weather from the NWS point/forecast/observation APIs, plus the official NOAA/NWS MRMS
	/// radar map-service manifest used by the commander map. Upstream responses are cached briefly to respect NWS
	/// rate limits while multiple command clients watch the same incident.
	/// </summary>
	public class NwsIncidentWeatherProvider : IIncidentWeatherProvider
	{
		private const string NwsBaseUrl = "https://api.weather.gov";
		private const string RadarServiceUrl = "https://mapservices.weather.noaa.gov/eventdriven/rest/services/radar/radar_base_reflectivity/MapServer";
		private static readonly HttpClient SharedHttpClient = CreateHttpClient();
		private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new ConcurrentDictionary<string, CacheEntry>();
		private readonly HttpClient _httpClient;

		public NwsIncidentWeatherProvider() : this(SharedHttpClient)
		{
		}

		/// <summary>Constructor exposed for deterministic provider tests with a stubbed HTTP handler.</summary>
		public NwsIncidentWeatherProvider(HttpClient httpClient)
		{
			_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		}

		public async Task<IncidentWeather> GetWeatherAsync(decimal latitude, decimal longitude, int forecastHours = 24, CancellationToken cancellationToken = default)
		{
			ValidateCoordinates(latitude, longitude);
			forecastHours = Math.Clamp(forecastHours, 1, 168);

			var cacheKey = $"{Math.Round(latitude, 4)}:{Math.Round(longitude, 4)}:{forecastHours}";
			if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
				return cached.Weather;

			var pointUrl = $"{NwsBaseUrl}/points/{latitude.ToString("0.####", CultureInfo.InvariantCulture)},{longitude.ToString("0.####", CultureInfo.InvariantCulture)}";
			using var pointDoc = await GetJsonAsync(pointUrl, cancellationToken);
			var pointProperties = pointDoc.RootElement.GetProperty("properties");
			var forecastUrl = GetString(pointProperties, "forecastHourly");
			var stationsUrl = GetString(pointProperties, "observationStations");

			if (string.IsNullOrWhiteSpace(forecastUrl))
				throw new InvalidOperationException("NWS did not return an hourly forecast endpoint for the incident location.");

			var weather = new IncidentWeather
			{
				Latitude = latitude,
				Longitude = longitude,
				Source = "National Weather Service",
				Attribution = "NOAA / National Weather Service",
				GeneratedAtUtc = DateTime.UtcNow,
				ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
			};

			using (var forecastDoc = await GetJsonAsync(forecastUrl, cancellationToken))
			{
				var properties = forecastDoc.RootElement.GetProperty("properties");
				weather.UpdatedAtUtc = GetDate(properties, "updated");
				if (properties.TryGetProperty("periods", out var periods))
				{
					weather.HourlyForecast = periods.EnumerateArray()
						.Take(forecastHours)
						.Select(ParseForecastPeriod)
						.ToList();
				}
			}

			if (!string.IsNullOrWhiteSpace(stationsUrl))
			{
				try
				{
					weather.Current = await GetLatestObservationAsync(stationsUrl, cancellationToken);
				}
				catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is InvalidOperationException)
				{
					// A station observation can be delayed or temporarily unavailable. Forecast/radar data remains useful,
					// so degrade only the current-conditions portion instead of failing the commander weather panel.
					weather.Current = null;
				}
			}

			weather.Overlays.Add(CreateRadarOverlay());
			Cache[cacheKey] = new CacheEntry(weather, weather.ExpiresAtUtc);
			return weather;
		}

		private async Task<IncidentWeatherObservation> GetLatestObservationAsync(string stationsUrl, CancellationToken cancellationToken)
		{
			using var stationsDoc = await GetJsonAsync(stationsUrl, cancellationToken);
			if (!stationsDoc.RootElement.TryGetProperty("features", out var features) || features.GetArrayLength() == 0)
				return null;

			var stationFeature = features[0];
			var stationProperties = stationFeature.GetProperty("properties");
			var stationId = GetString(stationProperties, "stationIdentifier");
			if (string.IsNullOrWhiteSpace(stationId))
			{
				var stationUrl = GetString(stationFeature, "id");
				stationId = stationUrl?.TrimEnd('/').Split('/').LastOrDefault();
			}

			if (string.IsNullOrWhiteSpace(stationId))
				return null;

			using var observationDoc = await GetJsonAsync($"{NwsBaseUrl}/stations/{Uri.EscapeDataString(stationId)}/observations/latest", cancellationToken);
			var properties = observationDoc.RootElement.GetProperty("properties");

			return new IncidentWeatherObservation
			{
				StationId = stationId,
				Description = GetString(properties, "textDescription"),
				ObservedAtUtc = GetDate(properties, "timestamp"),
				TemperatureCelsius = ReadTemperatureCelsius(properties, "temperature"),
				RelativeHumidityPercent = ReadValue(properties, "relativeHumidity"),
				WindSpeedKph = ReadSpeedKph(properties, "windSpeed"),
				WindGustKph = ReadSpeedKph(properties, "windGust"),
				WindDirectionDegrees = ReadValue(properties, "windDirection"),
				BarometricPressureHpa = ReadPressureHpa(properties, "barometricPressure"),
				VisibilityMeters = ReadValue(properties, "visibility")
			};
		}

		private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/geo+json"));
			request.Headers.UserAgent.ParseAdd("Resgrid/1.0 (team@resgrid.com)");

			using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();
			var json = await response.Content.ReadAsStringAsync(cancellationToken);
			return JsonDocument.Parse(json);
		}

		private static IncidentWeatherForecastPeriod ParseForecastPeriod(JsonElement period)
		{
			var temperature = GetDecimal(period, "temperature");
			var unit = GetString(period, "temperatureUnit");
			if (temperature.HasValue && string.Equals(unit, "F", StringComparison.OrdinalIgnoreCase))
				temperature = Math.Round((temperature.Value - 32m) * 5m / 9m, 1);

			int? precipitation = null;
			if (period.TryGetProperty("probabilityOfPrecipitation", out var pop))
				precipitation = GetInt(pop, "value");

			return new IncidentWeatherForecastPeriod
			{
				Number = GetInt(period, "number") ?? 0,
				Name = GetString(period, "name"),
				StartsAtUtc = GetDate(period, "startTime") ?? DateTime.MinValue,
				EndsAtUtc = GetDate(period, "endTime") ?? DateTime.MinValue,
				IsDaytime = GetBool(period, "isDaytime"),
				TemperatureCelsius = temperature,
				PrecipitationProbabilityPercent = precipitation,
				WindSpeed = GetString(period, "windSpeed"),
				WindDirection = GetString(period, "windDirection"),
				ShortForecast = GetString(period, "shortForecast"),
				DetailedForecast = GetString(period, "detailedForecast"),
				IconUrl = GetString(period, "icon")
			};
		}

		private static IncidentWeatherOverlay CreateRadarOverlay()
		{
			return new IncidentWeatherOverlay
			{
				Id = "noaa-mrms-base-reflectivity",
				Name = "Weather Radar",
				OverlayType = "ArcGisMapServerExport",
				ServiceUrl = RadarServiceUrl,
				LayerIds = "3",
				ExportUrlTemplate = RadarServiceUrl + "/export?bbox={west},{south},{east},{north}&bboxSR=3857&imageSR=3857&size={width},{height}&format=png32&transparent=true&layers=show:3&f=image",
				DefaultOpacity = 0.70m,
				RefreshSeconds = 300,
				Attribution = "NOAA / National Weather Service MRMS"
			};
		}

		private static decimal? ReadTemperatureCelsius(JsonElement properties, string propertyName)
		{
			if (!TryGetQuantitativeValue(properties, propertyName, out var value, out var unit))
				return null;
			if (unit?.IndexOf("degF", StringComparison.OrdinalIgnoreCase) >= 0)
				return Math.Round((value - 32m) * 5m / 9m, 1);
			return value;
		}

		private static decimal? ReadSpeedKph(JsonElement properties, string propertyName)
		{
			if (!TryGetQuantitativeValue(properties, propertyName, out var value, out var unit))
				return null;
			if (unit?.IndexOf("m_s-1", StringComparison.OrdinalIgnoreCase) >= 0)
				return Math.Round(value * 3.6m, 1);
			return value;
		}

		private static decimal? ReadPressureHpa(JsonElement properties, string propertyName)
		{
			if (!TryGetQuantitativeValue(properties, propertyName, out var value, out var unit))
				return null;
			if (unit?.EndsWith(":Pa", StringComparison.OrdinalIgnoreCase) == true)
				return Math.Round(value / 100m, 1, MidpointRounding.AwayFromZero);
			return value;
		}

		private static decimal? ReadValue(JsonElement properties, string propertyName)
			=> TryGetQuantitativeValue(properties, propertyName, out var value, out _) ? value : null;

		private static bool TryGetQuantitativeValue(JsonElement properties, string propertyName, out decimal value, out string unit)
		{
			value = 0;
			unit = null;
			if (!properties.TryGetProperty(propertyName, out var quantity) || quantity.ValueKind != JsonValueKind.Object)
				return false;
			unit = GetString(quantity, "unitCode");
			var parsed = GetDecimal(quantity, "value");
			if (!parsed.HasValue)
				return false;
			value = parsed.Value;
			return true;
		}

		private static string GetString(JsonElement element, string propertyName)
			=> element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

		private static decimal? GetDecimal(JsonElement element, string propertyName)
			=> element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result) ? result : null;

		private static int? GetInt(JsonElement element, string propertyName)
			=> element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;

		private static bool GetBool(JsonElement element, string propertyName)
			=> element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

		private static DateTime? GetDate(JsonElement element, string propertyName)
		{
			var value = GetString(element, propertyName);
			return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
				? parsed.UtcDateTime
				: (DateTime?)null;
		}

		private static void ValidateCoordinates(decimal latitude, decimal longitude)
		{
			if (latitude < -90 || latitude > 90)
				throw new ArgumentOutOfRangeException(nameof(latitude));
			if (longitude < -180 || longitude > 180)
				throw new ArgumentOutOfRangeException(nameof(longitude));
		}

		private static HttpClient CreateHttpClient()
		{
			return new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
		}

		private sealed class CacheEntry
		{
			public CacheEntry(IncidentWeather weather, DateTime expiresAtUtc)
			{
				Weather = weather;
				ExpiresAtUtc = expiresAtUtc;
			}

			public IncidentWeather Weather { get; }
			public DateTime ExpiresAtUtc { get; }
		}
	}
}
