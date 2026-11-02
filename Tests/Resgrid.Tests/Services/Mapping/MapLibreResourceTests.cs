using System;
using System.Text.Json.Nodes;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Mapping
{
	/// <summary>Mapbox styles made MapLibre-ready on the server (plan D6), the same rules as Omni's client fallback.</summary>
	[TestFixture]
	public class MapboxStyleTransformerTests
	{
		private const string Token = "pk.test.token";

		[Test]
		public void rewrites_every_mapbox_url_with_the_token()
		{
			MapboxStyleTransformer.RewriteUrl("mapbox://styles/mapbox/streets-v12", Token, MapboxStyleTransformer.ResourceKind.Style)
				.Should().Be("https://api.mapbox.com/styles/v1/mapbox/streets-v12?access_token=pk.test.token");
			MapboxStyleTransformer.RewriteUrl("mapbox://sprites/mapbox/streets-v12", Token, MapboxStyleTransformer.ResourceKind.Sprite)
				.Should().Be("https://api.mapbox.com/styles/v1/mapbox/streets-v12/sprite?access_token=pk.test.token");
			MapboxStyleTransformer.RewriteUrl("mapbox://fonts/mapbox/{fontstack}/{range}.pbf", Token, MapboxStyleTransformer.ResourceKind.Glyphs)
				.Should().Be("https://api.mapbox.com/fonts/v1/mapbox/{fontstack}/{range}.pbf?access_token=pk.test.token");
			MapboxStyleTransformer.RewriteUrl("mapbox://mapbox.mapbox-streets-v8", Token, MapboxStyleTransformer.ResourceKind.Source)
				.Should().Be("https://api.mapbox.com/v4/mapbox.mapbox-streets-v8.json?secure&access_token=pk.test.token");
		}

		[Test]
		public void leaves_non_mapbox_urls_alone()
		{
			MapboxStyleTransformer.RewriteUrl("https://tiles.example.org/{z}/{x}/{y}.png", Token, MapboxStyleTransformer.ResourceKind.Tiles)
				.Should().Be("https://tiles.example.org/{z}/{x}/{y}.png");
		}

		[Test]
		public void strips_mapbox_only_roots_layers_sources_properties_and_slot()
		{
			var style = JsonNode.Parse(@"{
				""version"": 8, ""name"": ""Streets"", ""fog"": {}, ""imports"": [], ""sprite"": ""mapbox://sprites/mapbox/streets-v12"",
				""glyphs"": ""mapbox://fonts/mapbox/{fontstack}/{range}.pbf"",
				""sources"": {
					""composite"": { ""type"": ""vector"", ""url"": ""mapbox://mapbox.mapbox-streets-v8"" },
					""model"": { ""type"": ""batched-model"" }
				},
				""layers"": [
					{ ""id"": ""land"", ""type"": ""background"", ""paint"": { ""background-color"": ""#fff"", ""background-emissive-strength"": 1 } },
					{ ""id"": ""roads"", ""type"": ""line"", ""source"": ""composite"", ""slot"": ""middle"", ""paint"": { ""line-color"": ""#000"", ""line-occlusion-opacity"": 0.5 } },
					{ ""id"": ""trees"", ""type"": ""model"", ""source"": ""model"" },
					{ ""id"": ""orphan"", ""type"": ""fill"", ""source"": ""model"" }
				]
			}")!.AsObject();

			var result = MapboxStyleTransformer.Transform(style, Token, "© Mapbox");

			result.ContainsKey("fog").Should().BeFalse();
			result.ContainsKey("imports").Should().BeFalse();
			result["sources"]!.AsObject().ContainsKey("model").Should().BeFalse();
			result["sources"]!["composite"]!["attribution"]!.GetValue<string>().Should().Be("© Mapbox");
			result["sprite"]!.GetValue<string>().Should().StartWith("https://api.mapbox.com/styles/v1/mapbox/streets-v12/sprite");

			var layers = result["layers"]!.AsArray();
			layers.Should().HaveCount(2);
			layers[0]!["paint"]!.AsObject().ContainsKey("background-emissive-strength").Should().BeFalse();
			layers[1]!.AsObject().ContainsKey("slot").Should().BeFalse();
			layers[1]!["paint"]!.AsObject().ContainsKey("line-occlusion-opacity").Should().BeFalse();
		}

		[Test]
		public void raster_style_expands_subdomains()
		{
			var json = JsonNode.Parse(MapStyleService.BuildRasterStyle("https://{s}.tile.example.org/{z}/{x}/{y}.png", "© OpenStreetMap contributors"))!;

			json["sources"]!["base"]!["tiles"]!.AsArray().Should().HaveCount(3);
			json["sources"]!["base"]!["attribution"]!.GetValue<string>().Should().Be("© OpenStreetMap contributors");
		}
	}

	/// <summary>Signed map resource URLs (plan D12).</summary>
	[TestFixture]
	public class MapResourceUrlsTests
	{
		private string _passphrase;

		[SetUp]
		public void SetUp()
		{
			_passphrase = SystemBehaviorConfig.ExternalLinkUrlParamPassphrase;
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "test-passphrase";
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = _passphrase;
		}

		private static (string D, string E, string Sig) Parse(string query)
		{
			string d = null, e = null, sig = null;
			foreach (var part in query.Split('&'))
			{
				var pair = part.Split('=', 2);
				if (pair[0] == "d") d = pair[1];
				if (pair[0] == "e") e = pair[1];
				if (pair[0] == "sig") sig = pair[1];
			}

			return (d, e, sig);
		}

		[Test]
		public void a_signed_scope_validates_for_its_department()
		{
			var (d, e, sig) = Parse(MapResourceUrls.SignQuery(42, MapResourceUrls.CustomMapScope("layer-1")));

			MapResourceUrls.TryValidate(d, e, sig, MapResourceUrls.CustomMapScope("layer-1"), out var departmentId).Should().BeTrue();
			departmentId.Should().Be(42);
		}

		[Test]
		public void another_resource_or_department_does_not_validate()
		{
			var (d, e, sig) = Parse(MapResourceUrls.SignQuery(42, MapResourceUrls.CustomMapScope("layer-1")));

			MapResourceUrls.TryValidate(d, e, sig, MapResourceUrls.CustomMapScope("layer-2"), out _).Should().BeFalse();
			MapResourceUrls.TryValidate("43", e, sig, MapResourceUrls.CustomMapScope("layer-1"), out _).Should().BeFalse();
		}

		[Test]
		public void expired_signatures_fail()
		{
			var (d, e, sig) = Parse(MapResourceUrls.SignQuery(42, MapResourceUrls.StaticMapScope, TimeSpan.FromSeconds(-5)));

			MapResourceUrls.TryValidate(d, e, sig, MapResourceUrls.StaticMapScope, out _).Should().BeFalse();
		}

		[Test]
		public void tile_templates_keep_their_placeholders()
		{
			var url = MapResourceUrls.Sign("https://api.example.org/api/v4/Mapping/GetCustomMapTile/layer-1/{z}/{x}/{y}", 42, MapResourceUrls.CustomMapScope("layer-1"));

			url.Should().StartWith("https://api.example.org/api/v4/Mapping/GetCustomMapTile/layer-1/{z}/{x}/{y}?d=42&e=");
		}
	}

	[TestFixture]
	public class WeatherAlertGeometryTests
	{
		[Test]
		public void cap_polygon_becomes_a_closed_geojson_polygon_lon_first()
		{
			var json = WeatherAlertGeometry.ToGeoJson("45.1,-75.2 45.3,-75.2 45.3,-75.0");

			json.Should().Be("{\"type\":\"Polygon\",\"coordinates\":[[[-75.2,45.1],[-75.2,45.3],[-75,45.3],[-75.2,45.1]]]}");
		}

		[Test]
		public void several_cap_polygons_become_a_multipolygon()
		{
			var json = WeatherAlertGeometry.ToGeoJson("45.1,-75.2 45.3,-75.2 45.3,-75.0|46.1,-75.2 46.3,-75.2 46.3,-75.0");

			json.Should().StartWith("{\"type\":\"MultiPolygon\",\"coordinates\":[[[[");
		}

		[Test]
		public void geojson_passes_through_and_empty_is_null()
		{
			WeatherAlertGeometry.ToGeoJson("{\"type\":\"Polygon\",\"coordinates\":[]}").Should().Be("{\"type\":\"Polygon\",\"coordinates\":[]}");
			WeatherAlertGeometry.ToGeoJson(" ").Should().BeNull();
		}
	}
}
