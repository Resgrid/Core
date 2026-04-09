using System;
using System.Collections.Generic;
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
	public class NwsWeatherAlertProvider : IWeatherAlertProvider
	{
		private static readonly HttpClient _httpClient = new HttpClient();
		private const string DefaultBaseUrl = "https://api.weather.gov/alerts/active";

		static NwsWeatherAlertProvider()
		{
			_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/geo+json"));
		}

		public WeatherAlertSourceType SourceType => WeatherAlertSourceType.NationalWeatherService;

		public async Task<List<WeatherAlert>> FetchAlertsAsync(WeatherAlertSource source, CancellationToken ct = default)
		{
			// Check shared cache first
			if (WeatherAlertResponseCache.TryGet(SourceType, source.AreaFilter, out var cachedAlerts))
			{
				return CloneAlertsForSource(cachedAlerts, source);
			}

			var alerts = new List<WeatherAlert>();
			var baseUrl = !string.IsNullOrEmpty(source.CustomEndpoint) ? source.CustomEndpoint : DefaultBaseUrl;

			var url = baseUrl;
			if (!string.IsNullOrEmpty(source.AreaFilter))
			{
				var zones = ParseAreaFilter(source.AreaFilter);
				if (zones.Length > 0)
				{
					// If codes look like state abbreviations (2 chars), use area parameter
					if (zones[0].Length == 2)
						url += $"?area={string.Join(",", zones)}";
					else
						url += $"?zone={string.Join(",", zones)}";
				}
			}

			var request = new HttpRequestMessage(HttpMethod.Get, url);

			// Set User-Agent with department admin contact email per NWS requirements
			var contactEmail = !string.IsNullOrEmpty(source.ContactEmail) ? source.ContactEmail : "noreply@resgrid.com";
			request.Headers.UserAgent.ParseAdd($"Resgrid/1.0 ({contactEmail})");

			// Identify requests as coming from Resgrid
			request.Headers.Add("X-Resgrid-Source", "Resgrid Weather Alert System");

			// If an API key is provided, include it as a custom header
			if (!string.IsNullOrEmpty(source.ApiKey))
				request.Headers.Add("X-Api-Key", source.ApiKey);

			// Use ETag for conditional requests
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
					$"NWS weather alert HTTP request failed for URL '{url}': {ex.Message}", ex);
			}

			if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
				return alerts; // No changes since last poll

			response.EnsureSuccessStatusCode();

			// Update ETag on source
			if (response.Headers.ETag != null)
				source.LastETag = response.Headers.ETag.Tag;

			var json = await response.Content.ReadAsStringAsync();

			// Validate response content-type is JSON before parsing
			var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
			if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
			{
				var snippet = json.Length > 200 ? json.Substring(0, 200) : json;
				throw new InvalidOperationException(
					$"NWS API returned non-JSON response (Content-Type: '{contentType}') for URL '{url}'. " +
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
					$"Failed to parse NWS JSON response for URL '{url}'. " +
					$"Response body starts with: {snippet}", ex);
			}

			using (doc)
			{
				var root = doc.RootElement;

				if (!root.TryGetProperty("features", out var features))
					return alerts;

				foreach (var feature in features.EnumerateArray())
				{
					try
					{
						var props = feature.GetProperty("properties");
						var alert = new WeatherAlert
						{
							DepartmentId = source.DepartmentId,
							WeatherAlertSourceId = source.WeatherAlertSourceId,
							ExternalId = GetStringProp(props, "id"),
							Sender = GetStringProp(props, "senderName"),
							Event = GetStringProp(props, "event"),
							AlertCategory = MapCategory(GetStringProp(props, "category")),
							Severity = (int)MapSeverity(GetStringProp(props, "severity")),
							Urgency = (int)MapUrgency(GetStringProp(props, "urgency")),
							Certainty = (int)MapCertainty(GetStringProp(props, "certainty")),
							Status = (int)WeatherAlertStatus.Active,
							Headline = GetStringProp(props, "headline"),
							Description = GetStringProp(props, "description"),
							Instruction = GetStringProp(props, "instruction"),
							AreaDescription = GetStringProp(props, "areaDesc"),
							EffectiveUtc = GetDateProp(props, "effective") ?? DateTime.UtcNow,
							OnsetUtc = GetDateProp(props, "onset"),
							ExpiresUtc = GetDateProp(props, "expires"),
							SentUtc = GetDateProp(props, "sent"),
							FirstSeenUtc = DateTime.UtcNow,
							LastUpdatedUtc = DateTime.UtcNow,
							NotificationSent = false
						};

						// Extract references (for update/cancel chains)
						var references = GetStringProp(props, "references");
						if (!string.IsNullOrEmpty(references))
							alert.ReferencesExternalId = references;

						// Extract geocodes
						if (props.TryGetProperty("geocode", out var geocode))
							alert.Geocodes = geocode.GetRawText();

						// Extract polygon from geometry
						if (feature.TryGetProperty("geometry", out var geometry) && geometry.ValueKind != JsonValueKind.Null)
						{
							alert.Polygon = geometry.GetRawText();

							// Try to extract center point from polygon
							if (geometry.TryGetProperty("coordinates", out var coords) && coords.GetArrayLength() > 0)
							{
								try
								{
									var ring = coords[0];
									double avgLat = 0, avgLng = 0;
									int count = 0;
									foreach (var point in ring.EnumerateArray())
									{
										avgLng += point[0].GetDouble();
										avgLat += point[1].GetDouble();
										count++;
									}
									if (count > 0)
										alert.CenterGeoLocation = $"{avgLat / count},{avgLng / count}";
								}
								catch { }
							}
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

			// Store in shared cache
			WeatherAlertResponseCache.Set(SourceType, source.AreaFilter, alerts);

			return alerts;
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
			if (!string.IsNullOrEmpty(value) && DateTime.TryParse(value, out var dt))
				return dt.ToUniversalTime();
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

		/// <summary>
		/// Parses AreaFilter which may be a JSON array (["NV","CA"]) or a raw
		/// comma-separated string (NV, CA) or a single value (NV).
		/// </summary>
		private static string[] ParseAreaFilter(string areaFilter)
		{
			if (string.IsNullOrWhiteSpace(areaFilter))
				return Array.Empty<string>();

			var trimmed = areaFilter.Trim();

			// Try JSON array first
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

			// Fall back to comma-separated string
			return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}
	}
}
