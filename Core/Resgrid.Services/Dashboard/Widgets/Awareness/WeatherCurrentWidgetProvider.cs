using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Awareness
{
	/// <summary>
	/// weather.current (plan §7.4): a server-side proxy of the OpenWeather source Big Board uses (current weather + the
	/// 5-day/3-hour forecast folded into days), for the department center, cached per department and unit system for
	/// 15 minutes in the string cache. The API key only ever appears in the outbound request: it is not in the DTO, the
	/// cache entry, an exception message or a log line. Gated by DashboardAccessChecks.WeatherConfigured.
	/// </summary>
	[DashboardWidget("weather.current")]
	public class WeatherCurrentWidgetProvider : IDashboardWidgetDataProvider
	{
		public const string OpenWeatherBaseUrl = "https://api.openweathermap.org/data/2.5/";
		public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
		private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

		/// <summary>Today and the next three days.</summary>
		public const int ForecastDays = 4;

		private readonly HttpClient _httpClient;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IGeoLocationProvider _geoLocationProvider;

		public WeatherCurrentWidgetProvider(ICacheProvider cacheProvider, IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService, IGeoLocationProvider geoLocationProvider)
			: this(SharedHttpClient, cacheProvider, departmentSettingsService, departmentsService, geoLocationProvider)
		{
		}

		/// <summary>For tests with a stubbed HTTP handler (the NwsIncidentWeatherProvider pattern).</summary>
		public WeatherCurrentWidgetProvider(HttpClient httpClient, ICacheProvider cacheProvider, IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService, IGeoLocationProvider geoLocationProvider)
		{
			_httpClient = httpClient ?? SharedHttpClient;
			_cacheProvider = cacheProvider;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_geoLocationProvider = geoLocationProvider;
		}

		/// <summary>The Big Board key first (the widget mirrors Big Board), else the Dispatch app key.</summary>
		public static string ApiKey =>
			!string.IsNullOrWhiteSpace(Config.MappingConfig.BigBoardOpenWeatherApiKey) ? Config.MappingConfig.BigBoardOpenWeatherApiKey.Trim()
			: Config.MappingConfig.DispatchOpenWeatherApiKey?.Trim();

		public static string CacheKey(int departmentId, string units) => $"DashboardWeather_{departmentId}_{units}";

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var units = context.GetString("units", "imperial") == "metric" ? "metric" : "imperial";
			var apiKey = ApiKey;

			// The access check hides the widget without a key; this keeps a forged or stale call from going out keyless.
			if (string.IsNullOrWhiteSpace(apiKey))
				return new WeatherCurrentWidgetData { Available = false, Units = units };

			var cacheKey = CacheKey(context.DepartmentId, units);
			var useCache = Config.SystemBehaviorConfig.CacheEnabled && _cacheProvider != null;
			if (useCache)
			{
				var cached = TryDeserialize(await _cacheProvider.GetStringAsync(cacheKey));
				if (cached != null)
					return cached;
			}

			var center = await DashboardDepartmentCenter.ResolveAsync(_departmentSettingsService, _geoLocationProvider, _departmentsService, context.DepartmentId);
			if (!center.HasValue)
				return new WeatherCurrentWidgetData { Available = false, Units = units };

			var data = await FetchAsync(apiKey, center.Value.Latitude, center.Value.Longitude, units, context.NowUtc, cancellationToken);

			if (useCache)
				await _cacheProvider.SetStringAsync(cacheKey, JsonConvert.SerializeObject(data), CacheDuration);

			return data;
		}

		private async Task<WeatherCurrentWidgetData> FetchAsync(string apiKey, double latitude, double longitude, string units, DateTime nowUtc,
			CancellationToken cancellationToken)
		{
			var query = string.Format(CultureInfo.InvariantCulture, "lat={0:0.####}&lon={1:0.####}&units={2}&appid={3}",
				latitude, longitude, units, Uri.EscapeDataString(apiKey));

			using var current = await GetJsonAsync("weather", query, cancellationToken);
			using var forecast = await GetJsonAsync("forecast", query, cancellationToken);

			var root = current.RootElement;
			var data = new WeatherCurrentWidgetData
			{
				Available = true,
				Units = units,
				Latitude = latitude,
				Longitude = longitude,
				LocationName = GetString(root, "name"),
				Current = ReadConditions(root),
				ObservedUtc = root.TryGetProperty("dt", out var dt) && dt.TryGetInt64(out var seconds)
					? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : nowUtc,
				FetchedUtc = nowUtc
			};

			// OpenWeather reports the place's own UTC offset; days are folded on the place's clock, not the server's.
			var offsetSeconds = forecast.RootElement.TryGetProperty("city", out var city) && city.TryGetProperty("timezone", out var tz) && tz.TryGetInt32(out var tzSeconds)
				? tzSeconds
				: root.TryGetProperty("timezone", out var currentTz) && currentTz.TryGetInt32(out var currentTzSeconds) ? currentTzSeconds : 0;

			data.Forecast = FoldForecast(forecast.RootElement, TimeSpan.FromSeconds(offsetSeconds), nowUtc);
			return data;
		}

		private async Task<JsonDocument> GetJsonAsync(string endpoint, string query, CancellationToken cancellationToken)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(RequestTimeout);

			HttpResponseMessage response;
			try
			{
				response = await _httpClient.GetAsync(OpenWeatherBaseUrl + endpoint + "?" + query, timeout.Token);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException)
			{
				// Never rethrow the original: some handlers put the request URI (and so the key) in the message.
				throw new InvalidOperationException($"OpenWeather {endpoint} request failed ({ex.GetType().Name}).");
			}

			using (response)
			{
				if (!response.IsSuccessStatusCode)
					throw new InvalidOperationException($"OpenWeather {endpoint} request failed ({(int)response.StatusCode}).");

				var body = await response.Content.ReadAsStringAsync(cancellationToken);
				try
				{
					return JsonDocument.Parse(body);
				}
				catch (System.Text.Json.JsonException)
				{
					throw new InvalidOperationException($"OpenWeather {endpoint} returned an unreadable body.");
				}
			}
		}

		internal static WeatherConditions ReadConditions(JsonElement root)
		{
			var conditions = new WeatherConditions();

			if (root.TryGetProperty("main", out var main))
			{
				conditions.Temperature = RoundNumber(main, "temp");
				conditions.FeelsLike = RoundNumber(main, "feels_like");
				conditions.Humidity = RoundNumber(main, "humidity");
			}

			if (root.TryGetProperty("wind", out var wind))
			{
				conditions.WindSpeed = RoundNumber(wind, "speed");
				if (wind.TryGetProperty("deg", out var deg) && deg.ValueKind == JsonValueKind.Number)
					conditions.WindDirection = (int)Math.Round(deg.GetDouble());
			}

			var weather = FirstWeather(root);
			conditions.Condition = weather.HasValue ? GetString(weather.Value, "main") ?? "Unknown" : "Unknown";
			conditions.Description = weather.HasValue ? GetString(weather.Value, "description") : null;
			conditions.Icon = weather.HasValue ? GetString(weather.Value, "icon") ?? "01d" : "01d";

			return conditions;
		}

		/// <summary>3-hour slots grouped by the place's local date: high/low over the day, the condition of the slot nearest noon.</summary>
		internal static List<WeatherForecastDay> FoldForecast(JsonElement root, TimeSpan utcOffset, DateTime nowUtc)
		{
			var result = new List<WeatherForecastDay>();
			if (!root.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
				return result;

			var today = (nowUtc + utcOffset).Date;
			var slots = new List<(DateTime Local, JsonElement Item)>();
			foreach (var item in list.EnumerateArray())
			{
				if (!item.TryGetProperty("dt", out var dt) || !dt.TryGetInt64(out var seconds))
					continue;

				slots.Add((DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime + utcOffset, item));
			}

			foreach (var day in slots.Where(s => s.Local.Date >= today).GroupBy(s => s.Local.Date).OrderBy(g => g.Key).Take(ForecastDays))
			{
				var items = day.ToList();
				var highs = items.Select(s => Number(s.Item, "main", "temp_max") ?? Number(s.Item, "main", "temp")).Where(v => v.HasValue).Select(v => v.Value).ToList();
				var lows = items.Select(s => Number(s.Item, "main", "temp_min") ?? Number(s.Item, "main", "temp")).Where(v => v.HasValue).Select(v => v.Value).ToList();
				var midday = items.OrderBy(s => Math.Abs((s.Local - s.Local.Date.AddHours(12)).TotalMinutes)).First().Item;
				var weather = FirstWeather(midday);
				var pop = items.Select(s => s.Item.TryGetProperty("pop", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0d).DefaultIfEmpty(0d).Max();

				result.Add(new WeatherForecastDay
				{
					Day = day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
					DayOfWeek = (int)day.Key.DayOfWeek,
					High = highs.Count > 0 ? (int)Math.Round(highs.Max()) : 0,
					Low = lows.Count > 0 ? (int)Math.Round(lows.Min()) : 0,
					Condition = weather.HasValue ? GetString(weather.Value, "main") ?? "Unknown" : "Unknown",
					Icon = weather.HasValue ? GetString(weather.Value, "icon") ?? "01d" : "01d",
					PrecipitationChance = (int)Math.Round(Math.Clamp(pop, 0d, 1d) * 100)
				});
			}

			return result;
		}

		private static WeatherCurrentWidgetData TryDeserialize(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				// A blank or partial entry (cache poisoning) is a miss, never a weatherless "available" widget.
				var data = JsonConvert.DeserializeObject<WeatherCurrentWidgetData>(json);
				return data != null && data.Available && data.Current != null ? data : null;
			}
			catch (Newtonsoft.Json.JsonException)
			{
				return null;
			}
		}

		private static JsonElement? FirstWeather(JsonElement element)
		{
			if (element.TryGetProperty("weather", out var weather) && weather.ValueKind == JsonValueKind.Array && weather.GetArrayLength() > 0)
				return weather[0];

			return null;
		}

		private static string GetString(JsonElement element, string name) =>
			element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

		private static int RoundNumber(JsonElement element, string name) =>
			element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? (int)Math.Round(value.GetDouble()) : 0;

		private static double? Number(JsonElement element, string parent, string name) =>
			element.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
				? value.GetDouble()
				: (double?)null;
	}
}
