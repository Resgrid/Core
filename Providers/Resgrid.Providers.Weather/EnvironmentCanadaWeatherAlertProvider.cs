using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Weather
{
	public class EnvironmentCanadaWeatherAlertProvider : IWeatherAlertProvider
	{
		private static readonly HttpClient _httpClient = new HttpClient();
		private const string DefaultBaseUrl = "https://dd.weather.gc.ca/alerts/cap";

		// CAP XML namespace
		private static readonly XNamespace Cap = "urn:oasis:names:tc:emergency:cap:1.2";

		static EnvironmentCanadaWeatherAlertProvider()
		{
			_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
			_httpClient.Timeout = TimeSpan.FromSeconds(30);
		}

		public WeatherAlertSourceType SourceType => WeatherAlertSourceType.EnvironmentCanada;

		public async Task<List<WeatherAlert>> FetchAlertsAsync(WeatherAlertSource source, CancellationToken ct = default)
		{
			// Check shared cache first
			if (WeatherAlertResponseCache.TryGet(SourceType, source.AreaFilter, out var cachedAlerts))
			{
				return CloneAlertsForSource(cachedAlerts, source);
			}

			var alerts = new List<WeatherAlert>();
			var baseUrl = !string.IsNullOrEmpty(source.CustomEndpoint) ? source.CustomEndpoint : DefaultBaseUrl;

			// Environment Canada organizes alerts by date and province
			// AreaFilter is expected to be a province code (e.g., "ON", "BC", "AB") or comma-separated list
			var provinces = ParseAreaFilter(source.AreaFilter);

			// Build the feed URL - Environment Canada provides an ATOM feed at /alerts/cap/{YYYYMMDD}/{province}/
			var today = DateTime.UtcNow.ToString("yyyyMMdd");
			var feedUrls = new List<string>();

			if (provinces.Length > 0)
			{
				foreach (var province in provinces)
				{
					feedUrls.Add($"{baseUrl}/{today}/{province.ToUpperInvariant()}/");
				}
			}
			else
			{
				// If no area filter, fetch the top-level ATOM feed
				feedUrls.Add($"{baseUrl}/{today}/");
			}

			foreach (var feedUrl in feedUrls)
			{
				try
				{
					var capUrls = await FetchCapUrlsFromFeedAsync(feedUrl, ct);

					foreach (var capUrl in capUrls)
					{
						try
						{
							var capAlerts = await FetchAndParseCapDocumentAsync(capUrl, source, ct);
							alerts.AddRange(capAlerts);
						}
						catch (Exception)
						{
							// Skip malformed CAP documents
							continue;
						}
					}
				}
				catch (Exception)
				{
					// Skip feed errors, continue with other provinces
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

		private async Task<List<string>> FetchCapUrlsFromFeedAsync(string feedUrl, CancellationToken ct)
		{
			var capUrls = new List<string>();

			var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
			request.Headers.UserAgent.ParseAdd("Resgrid/1.0 (weather-alerts)");

			var response = await _httpClient.SendAsync(request, ct);
			response.EnsureSuccessStatusCode();

			var content = await response.Content.ReadAsStringAsync();

			// Environment Canada directory listing contains links to CAP XML files
			// Parse as HTML-like content looking for .cap file links
			var lines = content.Split('\n');
			foreach (var line in lines)
			{
				// Look for href links pointing to .cap files
				var hrefStart = line.IndexOf("href=\"", StringComparison.OrdinalIgnoreCase);
				if (hrefStart < 0) continue;

				hrefStart += 6;
				var hrefEnd = line.IndexOf("\"", hrefStart, StringComparison.Ordinal);
				if (hrefEnd < 0) continue;

				var href = line.Substring(hrefStart, hrefEnd - hrefStart);
				if (href.EndsWith(".cap", StringComparison.OrdinalIgnoreCase))
				{
					// Build absolute URL
					var absoluteUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
						? href
						: feedUrl.TrimEnd('/') + "/" + href.TrimStart('/');

					capUrls.Add(absoluteUrl);
				}
			}

			return capUrls;
		}

		private async Task<List<WeatherAlert>> FetchAndParseCapDocumentAsync(string capUrl, WeatherAlertSource source, CancellationToken ct)
		{
			var alerts = new List<WeatherAlert>();

			var request = new HttpRequestMessage(HttpMethod.Get, capUrl);
			request.Headers.UserAgent.ParseAdd("Resgrid/1.0 (weather-alerts)");

			var response = await _httpClient.SendAsync(request, ct);
			response.EnsureSuccessStatusCode();

			var xml = await response.Content.ReadAsStringAsync();
			var doc = XDocument.Parse(xml);
			var root = doc.Root;

			if (root == null)
				return alerts;

			var identifier = root.Element(Cap + "identifier")?.Value;
			var sender = root.Element(Cap + "sender")?.Value;
			var sent = ParseCapDateTime(root.Element(Cap + "sent")?.Value);
			var status = root.Element(Cap + "status")?.Value;

			// Skip test and draft messages
			if (string.Equals(status, "Test", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase))
				return alerts;

			var references = root.Element(Cap + "references")?.Value;

			// Process each <info> block
			foreach (var info in root.Elements(Cap + "info"))
			{
				// Prefer English language block; skip French duplicates
				var language = info.Element(Cap + "language")?.Value ?? "en-CA";
				if (!language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
					continue;

				var alert = new WeatherAlert
				{
					DepartmentId = source.DepartmentId,
					WeatherAlertSourceId = source.WeatherAlertSourceId,
					ExternalId = identifier,
					Sender = sender ?? info.Element(Cap + "senderName")?.Value,
					Event = info.Element(Cap + "event")?.Value,
					AlertCategory = MapCategory(info.Element(Cap + "category")?.Value),
					Severity = (int)MapSeverity(info.Element(Cap + "severity")?.Value),
					Urgency = (int)MapUrgency(info.Element(Cap + "urgency")?.Value),
					Certainty = (int)MapCertainty(info.Element(Cap + "certainty")?.Value),
					Status = MapStatus(root.Element(Cap + "msgType")?.Value),
					Headline = info.Element(Cap + "headline")?.Value,
					Description = info.Element(Cap + "description")?.Value,
					Instruction = info.Element(Cap + "instruction")?.Value,
					EffectiveUtc = ParseCapDateTime(info.Element(Cap + "effective")?.Value) ?? sent ?? DateTime.UtcNow,
					OnsetUtc = ParseCapDateTime(info.Element(Cap + "onset")?.Value),
					ExpiresUtc = ParseCapDateTime(info.Element(Cap + "expires")?.Value),
					SentUtc = sent,
					FirstSeenUtc = DateTime.UtcNow,
					LastUpdatedUtc = DateTime.UtcNow,
					NotificationSent = false,
					ReferencesExternalId = references
				};

				// Process <area> blocks
				var areas = info.Elements(Cap + "area").ToList();
				if (areas.Any())
				{
					alert.AreaDescription = string.Join("; ", areas.Select(a => a.Element(Cap + "areaDesc")?.Value).Where(v => v != null));

					// Extract polygon
					var polygon = areas
						.SelectMany(a => a.Elements(Cap + "polygon"))
						.Select(p => p.Value)
						.FirstOrDefault();

					if (!string.IsNullOrEmpty(polygon))
					{
						alert.Polygon = polygon;
						alert.CenterGeoLocation = ComputeCenterFromCapPolygon(polygon);
					}

					// Extract geocodes
					var geocodes = areas
						.SelectMany(a => a.Elements(Cap + "geocode"))
						.Select(g => new
						{
							Name = g.Element(Cap + "valueName")?.Value,
							Value = g.Element(Cap + "value")?.Value
						})
						.Where(g => g.Name != null && g.Value != null)
						.ToList();

					if (geocodes.Any())
					{
						alert.Geocodes = System.Text.Json.JsonSerializer.Serialize(
							geocodes.GroupBy(g => g.Name).ToDictionary(g => g.Key, g => g.Last().Value));
					}
				}

				alerts.Add(alert);
			}

			return alerts;
		}

		private static DateTime? ParseCapDateTime(string value)
		{
			if (string.IsNullOrEmpty(value))
				return null;

			if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
				return dto.UtcDateTime;

			return null;
		}

		private static string ComputeCenterFromCapPolygon(string polygon)
		{
			// CAP polygon format: "lat,lng lat,lng lat,lng ..."
			try
			{
				var points = polygon.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
				double totalLat = 0, totalLng = 0;
				int count = 0;

				foreach (var point in points)
				{
					var coords = point.Split(',');
					if (coords.Length >= 2 &&
						double.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
						double.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
					{
						totalLat += lat;
						totalLng += lng;
						count++;
					}
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
					var parsed = System.Text.Json.JsonSerializer.Deserialize<string[]>(trimmed);
					if (parsed != null && parsed.Length > 0)
						return parsed;
				}
				catch { }
			}

			return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}
	}
}
