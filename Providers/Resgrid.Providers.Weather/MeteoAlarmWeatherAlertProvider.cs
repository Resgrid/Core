using System;
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
	public class MeteoAlarmWeatherAlertProvider : IWeatherAlertProvider
	{
		private static readonly HttpClient _httpClient = new HttpClient();
		private const string DefaultBaseUrl = "https://feeds.meteoalarm.org/api/v1/warnings/feeds-fullcap";

		static MeteoAlarmWeatherAlertProvider()
		{
			_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			_httpClient.Timeout = TimeSpan.FromSeconds(30);
		}

		public WeatherAlertSourceType SourceType => WeatherAlertSourceType.MeteoAlarm;

		public async Task<List<WeatherAlert>> FetchAlertsAsync(WeatherAlertSource source, CancellationToken ct = default)
		{
			// Check shared cache first
			if (WeatherAlertResponseCache.TryGet(SourceType, source.AreaFilter, out var cachedAlerts))
			{
				return CloneAlertsForSource(cachedAlerts, source);
			}

			var alerts = new List<WeatherAlert>();
			var baseUrl = !string.IsNullOrEmpty(source.CustomEndpoint) ? source.CustomEndpoint : DefaultBaseUrl;

			// AreaFilter is expected to be an ISO 3166-1 alpha-2 country code (e.g., "DE", "FR", "IT")
			// or comma-separated list of country codes
			var countries = ParseAreaFilter(source.AreaFilter);

			if (countries.Length == 0)
				countries = new[] { "" }; // Fetch all if no filter

			foreach (var country in countries)
			{
				try
				{
					var url = baseUrl;
					if (!string.IsNullOrEmpty(country))
						url += $"/{country.ToUpperInvariant()}";

					var countryAlerts = await FetchAlertsFromApiAsync(url, source, ct);
					alerts.AddRange(countryAlerts);
				}
				catch (Exception)
				{
					// Skip errors for individual countries, continue with others
					continue;
				}
			}

			// Deduplicate by ExternalId
			alerts = alerts
				.GroupBy(a => a.ExternalId)
				.Select(g => g.First())
				.ToList();

			// Store in shared cache
			WeatherAlertResponseCache.Set(SourceType, source.AreaFilter, alerts);

			return alerts;
		}

		private async Task<List<WeatherAlert>> FetchAlertsFromApiAsync(string url, WeatherAlertSource source, CancellationToken ct)
		{
			var alerts = new List<WeatherAlert>();

			var request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.UserAgent.ParseAdd("Resgrid/1.0 (weather-alerts)");

			if (!string.IsNullOrEmpty(source.ApiKey))
				request.Headers.Add("X-API-Key", source.ApiKey);

			if (!string.IsNullOrEmpty(source.LastETag))
				request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(source.LastETag));

			HttpResponseMessage response;
			try
			{
				response = await _httpClient.SendAsync(request, ct);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					$"MeteoAlarm HTTP request failed for URL '{url}': {ex.Message}", ex);
			}

			if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
				return alerts;

			response.EnsureSuccessStatusCode();

			if (response.Headers.ETag != null)
				source.LastETag = response.Headers.ETag.Tag;

			var json = await response.Content.ReadAsStringAsync();

			var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
			if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
			{
				var snippet = json.Length > 200 ? json.Substring(0, 200) : json;
				throw new InvalidOperationException(
					$"MeteoAlarm API returned non-JSON response (Content-Type: '{contentType}') for URL '{url}'. " +
					$"Response body starts with: {snippet}");
			}

			JsonDocument doc;
			try
			{
				doc = JsonDocument.Parse(json);
			}
			catch (JsonException ex)
			{
				var snippet = json.Length > 200 ? json.Substring(0, 200) : json;
				throw new InvalidOperationException(
					$"Failed to parse MeteoAlarm JSON response for URL '{url}'. " +
					$"Response body starts with: {snippet}", ex);
			}

			using (doc)
			{
				var root = doc.RootElement;

				// MeteoAlarm feeds-fullcap returns GeoJSON FeatureCollection
				if (!root.TryGetProperty("features", out var features))
					return alerts;

				foreach (var feature in features.EnumerateArray())
				{
					try
					{
						var props = feature.GetProperty("properties");

						// The CAP data may be nested under "cap" or directly in properties
						var capElement = props.TryGetProperty("cap", out var capProp) ? capProp : props;

						var alert = new WeatherAlert
						{
							DepartmentId = source.DepartmentId,
							WeatherAlertSourceId = source.WeatherAlertSourceId,
							ExternalId = GetStringProp(props, "identifier") ?? GetStringProp(props, "id"),
							Sender = GetStringProp(capElement, "sender") ?? GetStringProp(capElement, "senderName"),
							Event = GetStringProp(capElement, "event"),
							AlertCategory = MapCategory(GetStringProp(capElement, "category")),
							Severity = (int)MapSeverity(GetStringProp(capElement, "severity")),
							Urgency = (int)MapUrgency(GetStringProp(capElement, "urgency")),
							Certainty = (int)MapCertainty(GetStringProp(capElement, "certainty")),
							Status = MapStatus(GetStringProp(capElement, "msgType") ?? GetStringProp(props, "msgType")),
							Headline = GetStringProp(capElement, "headline"),
							Description = GetStringProp(capElement, "description"),
							Instruction = GetStringProp(capElement, "instruction"),
							AreaDescription = GetStringProp(capElement, "areaDesc"),
							EffectiveUtc = GetDateProp(capElement, "effective") ?? DateTime.UtcNow,
							OnsetUtc = GetDateProp(capElement, "onset"),
							ExpiresUtc = GetDateProp(capElement, "expires"),
							SentUtc = GetDateProp(capElement, "sent") ?? GetDateProp(props, "sent"),
							FirstSeenUtc = DateTime.UtcNow,
							LastUpdatedUtc = DateTime.UtcNow,
							NotificationSent = false
						};

						// Extract references
						var references = GetStringProp(capElement, "references");
						if (!string.IsNullOrEmpty(references))
							alert.ReferencesExternalId = references;

						// Extract geocodes
						if (capElement.TryGetProperty("geocode", out var geocode))
							alert.Geocodes = geocode.GetRawText();

						// Extract polygon from GeoJSON geometry
						if (feature.TryGetProperty("geometry", out var geometry) && geometry.ValueKind != JsonValueKind.Null)
						{
							alert.Polygon = geometry.GetRawText();
							alert.CenterGeoLocation = ComputeCenterFromGeoJson(geometry);
						}

						alerts.Add(alert);
					}
					catch (Exception)
					{
						// Skip malformed alerts, continue with others
						continue;
					}
				}
			}

			return alerts;
		}

		private static string ComputeCenterFromGeoJson(JsonElement geometry)
		{
			try
			{
				if (!geometry.TryGetProperty("coordinates", out var coords) || coords.GetArrayLength() == 0)
					return null;

				var geometryType = GetStringProp(geometry, "type") ?? "";

				// Handle Polygon: coordinates is [[[lng,lat], ...]]
				// Handle MultiPolygon: coordinates is [[[[lng,lat], ...]], ...]
				JsonElement ring;
				if (geometryType.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase))
					ring = coords[0][0]; // First polygon, outer ring
				else
					ring = coords[0]; // Outer ring

				double totalLat = 0, totalLng = 0;
				int count = 0;

				foreach (var point in ring.EnumerateArray())
				{
					totalLng += point[0].GetDouble();
					totalLat += point[1].GetDouble();
					count++;
				}

				if (count > 0)
					return $"{(totalLat / count).ToString(CultureInfo.InvariantCulture)},{(totalLng / count).ToString(CultureInfo.InvariantCulture)}";
			}
			catch { }

			return null;
		}

		private static List<WeatherAlert> CloneAlertsForSource(List<WeatherAlert> cachedAlerts, WeatherAlertSource source)
		{
			return cachedAlerts.Select(a => new WeatherAlert
			{
				DepartmentId = source.DepartmentId,
				WeatherAlertSourceId = source.WeatherAlertSourceId,
				ExternalId = a.ExternalId,
				Sender = a.Sender,
				Event = a.Event,
				AlertCategory = a.AlertCategory,
				Severity = a.Severity,
				Urgency = a.Urgency,
				Certainty = a.Certainty,
				Status = a.Status,
				Headline = a.Headline,
				Description = a.Description,
				Instruction = a.Instruction,
				AreaDescription = a.AreaDescription,
				EffectiveUtc = a.EffectiveUtc,
				OnsetUtc = a.OnsetUtc,
				ExpiresUtc = a.ExpiresUtc,
				SentUtc = a.SentUtc,
				FirstSeenUtc = a.FirstSeenUtc,
				LastUpdatedUtc = a.LastUpdatedUtc,
				NotificationSent = false,
				ReferencesExternalId = a.ReferencesExternalId,
				Geocodes = a.Geocodes,
				Polygon = a.Polygon,
				CenterGeoLocation = a.CenterGeoLocation
			}).ToList();
		}

		private static string GetStringProp(JsonElement element, string name)
		{
			if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
				return prop.GetString();
			return null;
		}

		private static DateTime? GetDateProp(JsonElement element, string name)
		{
			var value = GetStringProp(element, name);
			if (!string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
				return dto.UtcDateTime;
			return null;
		}

		private static int MapCategory(string category)
		{
			return category?.ToLowerInvariant() switch
			{
				"met" => (int)WeatherAlertCategory.Met,
				"fire" => (int)WeatherAlertCategory.Fire,
				"health" => (int)WeatherAlertCategory.Health,
				"env" => (int)WeatherAlertCategory.Env,
				_ => (int)WeatherAlertCategory.Other
			};
		}

		private static WeatherAlertSeverity MapSeverity(string severity)
		{
			return severity?.ToLowerInvariant() switch
			{
				"extreme" => WeatherAlertSeverity.Extreme,
				"severe" => WeatherAlertSeverity.Severe,
				"moderate" => WeatherAlertSeverity.Moderate,
				"minor" => WeatherAlertSeverity.Minor,
				_ => WeatherAlertSeverity.Unknown
			};
		}

		private static WeatherAlertUrgency MapUrgency(string urgency)
		{
			return urgency?.ToLowerInvariant() switch
			{
				"immediate" => WeatherAlertUrgency.Immediate,
				"expected" => WeatherAlertUrgency.Expected,
				"future" => WeatherAlertUrgency.Future,
				"past" => WeatherAlertUrgency.Past,
				_ => WeatherAlertUrgency.Unknown
			};
		}

		private static WeatherAlertCertainty MapCertainty(string certainty)
		{
			return certainty?.ToLowerInvariant() switch
			{
				"observed" => WeatherAlertCertainty.Observed,
				"likely" => WeatherAlertCertainty.Likely,
				"possible" => WeatherAlertCertainty.Possible,
				"unlikely" => WeatherAlertCertainty.Unlikely,
				_ => WeatherAlertCertainty.Unknown
			};
		}

		private static int MapStatus(string msgType)
		{
			return msgType?.ToLowerInvariant() switch
			{
				"alert" => (int)WeatherAlertStatus.Active,
				"update" => (int)WeatherAlertStatus.Updated,
				"cancel" => (int)WeatherAlertStatus.Cancelled,
				_ => (int)WeatherAlertStatus.Active
			};
		}

		private static string[] ParseAreaFilter(string areaFilter)
		{
			if (string.IsNullOrWhiteSpace(areaFilter))
				return Array.Empty<string>();

			var trimmed = areaFilter.Trim();

			if (trimmed.StartsWith("["))
			{
				try
				{
					var parsed = JsonSerializer.Deserialize<string[]>(trimmed);
					if (parsed != null && parsed.Length > 0)
						return parsed;
				}
				catch { }
			}

			return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}
	}
}
