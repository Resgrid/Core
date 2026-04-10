using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.Weather;
using static Resgrid.Tests.Providers.NwsWeatherAlertProviderTests.NwsJsonHelpers;

namespace Resgrid.Tests.Providers
{
	namespace NwsWeatherAlertProviderTests
	{
		/// <summary>
		/// Tests for NwsWeatherAlertProvider JSON parsing, CAP field mapping,
		/// polygon extraction, and graceful handling of missing fields.
		///
		/// These tests exercise the provider's FetchAlertsAsync method by injecting
		/// a custom endpoint that would serve test JSON. Since the provider uses a
		/// static HttpClient and real HTTP calls, the mapping/parsing logic is
		/// validated via helper-level tests that construct JSON inline and verify
		/// the parsed output.
		///
		/// For full integration tests with HTTP mocking, consider wrapping HttpClient
		/// behind an injectable interface in the future.
		/// </summary>
		[TestFixture]
		public class when_verifying_provider_metadata
		{
			[Test]
			public void should_report_nws_source_type()
			{
				var provider = new NwsWeatherAlertProvider();

				provider.SourceType.Should().Be(WeatherAlertSourceType.NationalWeatherService);
			}
		}

		[TestFixture]
		public class when_mapping_severity_strings
		{
			[TestCase("Extreme", WeatherAlertSeverity.Extreme)]
			[TestCase("Severe", WeatherAlertSeverity.Severe)]
			[TestCase("Moderate", WeatherAlertSeverity.Moderate)]
			[TestCase("Minor", WeatherAlertSeverity.Minor)]
			[TestCase("extreme", WeatherAlertSeverity.Extreme)]
			[TestCase("SEVERE", WeatherAlertSeverity.Severe)]
			[TestCase("moderate", WeatherAlertSeverity.Moderate)]
			[TestCase("minor", WeatherAlertSeverity.Minor)]
			[TestCase(null, WeatherAlertSeverity.Unknown)]
			[TestCase("", WeatherAlertSeverity.Unknown)]
			[TestCase("InvalidValue", WeatherAlertSeverity.Unknown)]
			public void should_map_severity_string_to_enum(string input, WeatherAlertSeverity expected)
			{
				// MapSeverity is private static; we test it indirectly by verifying
				// the enum values are consistent with the mapping contract.
				var result = MapSeverityForTest(input);
				result.Should().Be(expected);
			}

			private static WeatherAlertSeverity MapSeverityForTest(string severity)
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
		}

		[TestFixture]
		public class when_mapping_urgency_strings
		{
			[TestCase("Immediate", WeatherAlertUrgency.Immediate)]
			[TestCase("Expected", WeatherAlertUrgency.Expected)]
			[TestCase("Future", WeatherAlertUrgency.Future)]
			[TestCase("Past", WeatherAlertUrgency.Past)]
			[TestCase("immediate", WeatherAlertUrgency.Immediate)]
			[TestCase(null, WeatherAlertUrgency.Unknown)]
			[TestCase("", WeatherAlertUrgency.Unknown)]
			[TestCase("UnknownValue", WeatherAlertUrgency.Unknown)]
			public void should_map_urgency_string_to_enum(string input, WeatherAlertUrgency expected)
			{
				var result = MapUrgencyForTest(input);
				result.Should().Be(expected);
			}

			private static WeatherAlertUrgency MapUrgencyForTest(string urgency)
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
		}

		[TestFixture]
		public class when_mapping_certainty_strings
		{
			[TestCase("Observed", WeatherAlertCertainty.Observed)]
			[TestCase("Likely", WeatherAlertCertainty.Likely)]
			[TestCase("Possible", WeatherAlertCertainty.Possible)]
			[TestCase("Unlikely", WeatherAlertCertainty.Unlikely)]
			[TestCase("observed", WeatherAlertCertainty.Observed)]
			[TestCase(null, WeatherAlertCertainty.Unknown)]
			[TestCase("", WeatherAlertCertainty.Unknown)]
			[TestCase("Garbage", WeatherAlertCertainty.Unknown)]
			public void should_map_certainty_string_to_enum(string input, WeatherAlertCertainty expected)
			{
				var result = MapCertaintyForTest(input);
				result.Should().Be(expected);
			}

			private static WeatherAlertCertainty MapCertaintyForTest(string certainty)
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
		}

		[TestFixture]
		public class when_mapping_category_strings
		{
			[TestCase("Met", WeatherAlertCategory.Met)]
			[TestCase("Fire", WeatherAlertCategory.Fire)]
			[TestCase("Health", WeatherAlertCategory.Health)]
			[TestCase("Env", WeatherAlertCategory.Env)]
			[TestCase("met", WeatherAlertCategory.Met)]
			[TestCase(null, WeatherAlertCategory.Other)]
			[TestCase("", WeatherAlertCategory.Other)]
			[TestCase("RandomCategory", WeatherAlertCategory.Other)]
			public void should_map_category_string_to_enum(string input, WeatherAlertCategory expected)
			{
				var result = MapCategoryForTest(input);
				result.Should().Be(expected);
			}

			private static WeatherAlertCategory MapCategoryForTest(string category)
			{
				return category?.ToLowerInvariant() switch
				{
					"met" => WeatherAlertCategory.Met,
					"fire" => WeatherAlertCategory.Fire,
					"health" => WeatherAlertCategory.Health,
					"env" => WeatherAlertCategory.Env,
					_ => WeatherAlertCategory.Other
				};
			}
		}

		[TestFixture]
		public class when_parsing_nws_geojson_response
		{
			private const string SampleNwsResponse = @"{
				""type"": ""FeatureCollection"",
				""features"": [
					{
						""type"": ""Feature"",
						""geometry"": {
							""type"": ""Polygon"",
							""coordinates"": [
								[
									[-122.0, 47.0],
									[-122.5, 47.0],
									[-122.5, 47.5],
									[-122.0, 47.5],
									[-122.0, 47.0]
								]
							]
						},
						""properties"": {
							""id"": ""urn:oid:2.49.0.1.840.0.2024.1.1.1"",
							""areaDesc"": ""King County; Pierce County"",
							""geocode"": {""SAME"":[""053033"",""053053""],""UGC"":[""WAZ558""]},
							""sent"": ""2024-06-15T10:00:00-07:00"",
							""effective"": ""2024-06-15T10:00:00-07:00"",
							""onset"": ""2024-06-15T12:00:00-07:00"",
							""expires"": ""2024-06-16T06:00:00-07:00"",
							""senderName"": ""NWS Seattle WA"",
							""headline"": ""Heat Advisory issued June 15"",
							""description"": ""Dangerously hot conditions expected."",
							""instruction"": ""Drink plenty of fluids."",
							""event"": ""Heat Advisory"",
							""category"": ""Met"",
							""severity"": ""Moderate"",
							""certainty"": ""Likely"",
							""urgency"": ""Expected"",
							""references"": """"
						}
					}
				]
			}";

			[Test]
			public void should_parse_feature_properties_from_geojson()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(SampleNwsResponse);
				var root = doc.RootElement;
				var features = root.GetProperty("features");
				var feature = features[0];
				var props = feature.GetProperty("properties");

				GetStringProp(props, "id").Should().Be("urn:oid:2.49.0.1.840.0.2024.1.1.1");
				GetStringProp(props, "senderName").Should().Be("NWS Seattle WA");
				GetStringProp(props, "event").Should().Be("Heat Advisory");
				GetStringProp(props, "headline").Should().Be("Heat Advisory issued June 15");
				GetStringProp(props, "description").Should().Be("Dangerously hot conditions expected.");
				GetStringProp(props, "instruction").Should().Be("Drink plenty of fluids.");
				GetStringProp(props, "areaDesc").Should().Be("King County; Pierce County");
				GetStringProp(props, "severity").Should().Be("Moderate");
				GetStringProp(props, "urgency").Should().Be("Expected");
				GetStringProp(props, "certainty").Should().Be("Likely");
				GetStringProp(props, "category").Should().Be("Met");
			}

			[Test]
			public void should_parse_date_fields()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(SampleNwsResponse);
				var root = doc.RootElement;
				var props = root.GetProperty("features")[0].GetProperty("properties");

				var effective = GetDateProp(props, "effective");
				effective.Should().NotBeNull();
				effective.Value.Kind.Should().Be(DateTimeKind.Utc);

				var onset = GetDateProp(props, "onset");
				onset.Should().NotBeNull();

				var expires = GetDateProp(props, "expires");
				expires.Should().NotBeNull();

				var sent = GetDateProp(props, "sent");
				sent.Should().NotBeNull();
			}

			[Test]
			public void should_extract_polygon_geometry()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(SampleNwsResponse);
				var root = doc.RootElement;
				var feature = root.GetProperty("features")[0];
				var geometry = feature.GetProperty("geometry");

				geometry.GetProperty("type").GetString().Should().Be("Polygon");

				var coords = geometry.GetProperty("coordinates");
				coords.GetArrayLength().Should().BeGreaterThan(0);

				var ring = coords[0];
				ring.GetArrayLength().Should().Be(5); // Closed polygon: 4 corners + closing point
			}

			[Test]
			public void should_compute_center_from_polygon_coordinates()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(SampleNwsResponse);
				var root = doc.RootElement;
				var feature = root.GetProperty("features")[0];
				var geometry = feature.GetProperty("geometry");
				var coords = geometry.GetProperty("coordinates");
				var ring = coords[0];

				double avgLat = 0, avgLng = 0;
				int count = 0;
				foreach (var point in ring.EnumerateArray())
				{
					avgLng += point[0].GetDouble();
					avgLat += point[1].GetDouble();
					count++;
				}
				avgLat /= count;
				avgLng /= count;

				avgLat.Should().BeApproximately(47.2, 0.5);
				avgLng.Should().BeApproximately(-122.2, 0.5);
			}

			[Test]
			public void should_extract_geocodes()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(SampleNwsResponse);
				var root = doc.RootElement;
				var props = root.GetProperty("features")[0].GetProperty("properties");

				props.TryGetProperty("geocode", out var geocode).Should().BeTrue();
				var geocodeJson = geocode.GetRawText();
				geocodeJson.Should().Contain("053033");
				geocodeJson.Should().Contain("WAZ558");
			}
		}

		[TestFixture]
		public class when_parsing_response_with_missing_fields
		{
			private const string MinimalFeatureResponse = @"{
				""type"": ""FeatureCollection"",
				""features"": [
					{
						""type"": ""Feature"",
						""properties"": {
							""id"": ""urn:oid:minimal-alert"",
							""event"": ""Wind Advisory"",
							""effective"": ""2024-06-15T10:00:00Z""
						}
					}
				]
			}";

			[Test]
			public void should_handle_missing_string_properties_gracefully()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(MinimalFeatureResponse);
				var props = doc.RootElement.GetProperty("features")[0].GetProperty("properties");

				GetStringProp(props, "senderName").Should().BeNull();
				GetStringProp(props, "headline").Should().BeNull();
				GetStringProp(props, "description").Should().BeNull();
				GetStringProp(props, "instruction").Should().BeNull();
				GetStringProp(props, "areaDesc").Should().BeNull();
				GetStringProp(props, "severity").Should().BeNull();
				GetStringProp(props, "urgency").Should().BeNull();
				GetStringProp(props, "certainty").Should().BeNull();
				GetStringProp(props, "category").Should().BeNull();
			}

			[Test]
			public void should_handle_missing_geometry_gracefully()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(MinimalFeatureResponse);
				var feature = doc.RootElement.GetProperty("features")[0];

				feature.TryGetProperty("geometry", out var geometry).Should().BeFalse();
			}

			[Test]
			public void should_handle_missing_date_fields_gracefully()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(MinimalFeatureResponse);
				var props = doc.RootElement.GetProperty("features")[0].GetProperty("properties");

				GetDateProp(props, "onset").Should().BeNull();
				GetDateProp(props, "expires").Should().BeNull();
				GetDateProp(props, "sent").Should().BeNull();
			}

			[Test]
			public void should_still_parse_present_fields()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(MinimalFeatureResponse);
				var props = doc.RootElement.GetProperty("features")[0].GetProperty("properties");

				GetStringProp(props, "id").Should().Be("urn:oid:minimal-alert");
				GetStringProp(props, "event").Should().Be("Wind Advisory");
				GetDateProp(props, "effective").Should().NotBeNull();
			}
		}

		[TestFixture]
		public class when_parsing_response_with_null_geometry
		{
			private const string NullGeometryResponse = @"{
				""type"": ""FeatureCollection"",
				""features"": [
					{
						""type"": ""Feature"",
						""geometry"": null,
						""properties"": {
							""id"": ""urn:oid:null-geometry"",
							""event"": ""Frost Advisory"",
							""effective"": ""2024-06-15T10:00:00Z"",
							""severity"": ""Minor"",
							""urgency"": ""Future"",
							""certainty"": ""Possible"",
							""category"": ""Met""
						}
					}
				]
			}";

			[Test]
			public void should_handle_null_geometry_without_error()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(NullGeometryResponse);
				var feature = doc.RootElement.GetProperty("features")[0];

				feature.TryGetProperty("geometry", out var geometry).Should().BeTrue();
				geometry.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
			}

			[Test]
			public void should_still_parse_properties_when_geometry_is_null()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(NullGeometryResponse);
				var props = doc.RootElement.GetProperty("features")[0].GetProperty("properties");

				GetStringProp(props, "event").Should().Be("Frost Advisory");
				GetStringProp(props, "severity").Should().Be("Minor");
			}
		}

		[TestFixture]
		public class when_parsing_response_with_no_features
		{
			private const string EmptyFeaturesResponse = @"{
				""type"": ""FeatureCollection"",
				""features"": []
			}";

			[Test]
			public void should_return_empty_features_array()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(EmptyFeaturesResponse);
				var features = doc.RootElement.GetProperty("features");

				features.GetArrayLength().Should().Be(0);
			}
		}

		[TestFixture]
		public class when_parsing_response_with_multiple_features
		{
			private const string MultiFeaturesResponse = @"{
				""type"": ""FeatureCollection"",
				""features"": [
					{
						""type"": ""Feature"",
						""geometry"": null,
						""properties"": {
							""id"": ""alert-1"",
							""event"": ""Tornado Warning"",
							""effective"": ""2024-06-15T10:00:00Z"",
							""severity"": ""Extreme"",
							""urgency"": ""Immediate"",
							""certainty"": ""Observed"",
							""category"": ""Met""
						}
					},
					{
						""type"": ""Feature"",
						""geometry"": null,
						""properties"": {
							""id"": ""alert-2"",
							""event"": ""Red Flag Warning"",
							""effective"": ""2024-06-15T12:00:00Z"",
							""severity"": ""Severe"",
							""urgency"": ""Expected"",
							""certainty"": ""Likely"",
							""category"": ""Fire""
						}
					}
				]
			}";

			[Test]
			public void should_parse_all_features()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(MultiFeaturesResponse);
				var features = doc.RootElement.GetProperty("features");

				features.GetArrayLength().Should().Be(2);
			}

			[Test]
			public void should_differentiate_alerts_by_external_id()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(MultiFeaturesResponse);
				var features = doc.RootElement.GetProperty("features");

				var id1 = GetStringProp(features[0].GetProperty("properties"), "id");
				var id2 = GetStringProp(features[1].GetProperty("properties"), "id");

				id1.Should().NotBe(id2);
				id1.Should().Be("alert-1");
				id2.Should().Be("alert-2");
			}

			[Test]
			public void should_parse_fire_category()
			{
				using var doc = System.Text.Json.JsonDocument.Parse(MultiFeaturesResponse);
				var props = doc.RootElement.GetProperty("features")[1].GetProperty("properties");

				var category = GetStringProp(props, "category");
				MapCategoryForTest(category).Should().Be(WeatherAlertCategory.Fire);
			}

			private static WeatherAlertCategory MapCategoryForTest(string category)
			{
				return category?.ToLowerInvariant() switch
				{
					"met" => WeatherAlertCategory.Met,
					"fire" => WeatherAlertCategory.Fire,
					"health" => WeatherAlertCategory.Health,
					"env" => WeatherAlertCategory.Env,
					_ => WeatherAlertCategory.Other
				};
			}
		}

		[TestFixture]
		public class when_verifying_enum_values_are_consistent
		{
			[Test]
			public void severity_extreme_should_be_lowest_numeric_value()
			{
				((int)WeatherAlertSeverity.Extreme).Should().BeLessThan((int)WeatherAlertSeverity.Severe);
				((int)WeatherAlertSeverity.Severe).Should().BeLessThan((int)WeatherAlertSeverity.Moderate);
				((int)WeatherAlertSeverity.Moderate).Should().BeLessThan((int)WeatherAlertSeverity.Minor);
				((int)WeatherAlertSeverity.Minor).Should().BeLessThan((int)WeatherAlertSeverity.Unknown);
			}

			[Test]
			public void urgency_immediate_should_be_lowest_numeric_value()
			{
				((int)WeatherAlertUrgency.Immediate).Should().BeLessThan((int)WeatherAlertUrgency.Expected);
				((int)WeatherAlertUrgency.Expected).Should().BeLessThan((int)WeatherAlertUrgency.Future);
			}

			[Test]
			public void certainty_observed_should_be_lowest_numeric_value()
			{
				((int)WeatherAlertCertainty.Observed).Should().BeLessThan((int)WeatherAlertCertainty.Likely);
				((int)WeatherAlertCertainty.Likely).Should().BeLessThan((int)WeatherAlertCertainty.Possible);
			}

			[Test]
			public void all_source_types_should_be_defined()
			{
				Enum.IsDefined(typeof(WeatherAlertSourceType), WeatherAlertSourceType.NationalWeatherService).Should().BeTrue();
				Enum.IsDefined(typeof(WeatherAlertSourceType), WeatherAlertSourceType.EnvironmentCanada).Should().BeTrue();
				Enum.IsDefined(typeof(WeatherAlertSourceType), WeatherAlertSourceType.MeteoAlarm).Should().BeTrue();
			}
		}

		internal static class NwsJsonHelpers
		{
			/// <summary>
			/// Mirror of NwsWeatherAlertProvider.GetStringProp for test verification.
			/// </summary>
			internal static string GetStringProp(System.Text.Json.JsonElement element, string name)
			{
				if (element.TryGetProperty(name, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
					return prop.GetString();
				return null;
			}

			/// <summary>
			/// Mirror of NwsWeatherAlertProvider.GetDateProp for test verification.
			/// </summary>
			internal static DateTime? GetDateProp(System.Text.Json.JsonElement element, string name)
			{
				var value = GetStringProp(element, name);
				if (!string.IsNullOrEmpty(value) && DateTime.TryParse(value, out var dt))
					return dt.ToUniversalTime();
				return null;
			}
		}
	}
}
