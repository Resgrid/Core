using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Providers.GeoLocationProvider.Routing;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>
	/// Valhalla behind a gateway or as a hosted service (SimpleRouting: Bearer key, North America and Europe only):
	/// how the key is sent, which requests are skipped as outside the server's coverage, and which server replies are
	/// per-request rejections rather than provider failures.
	/// </summary>
	[TestFixture]
	public class ValhallaRoutingProviderTests
	{
		private const string NorthAmericaAndEurope = "-170,14,-50,84;-32,27,45,82";

		private string _url;
		private string _apiKey;
		private string _scheme;
		private string _coverage;
		private int _maxWaypoints;
		private int _maxExcludePoints;

		[SetUp]
		public void SetUp()
		{
			_url = MappingConfig.ValhallaUrl;
			_apiKey = MappingConfig.ValhallaApiKey;
			_scheme = MappingConfig.ValhallaAuthScheme;
			_coverage = MappingConfig.ValhallaCoverage;
			_maxWaypoints = MappingConfig.ValhallaMaxWaypoints;
			_maxExcludePoints = MappingConfig.ValhallaMaxExcludePoints;
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.ValhallaUrl = _url;
			MappingConfig.ValhallaApiKey = _apiKey;
			MappingConfig.ValhallaAuthScheme = _scheme;
			MappingConfig.ValhallaCoverage = _coverage;
			MappingConfig.ValhallaMaxWaypoints = _maxWaypoints;
			MappingConfig.ValhallaMaxExcludePoints = _maxExcludePoints;
		}

		private static HttpRequestMessage AuthorizedRequest()
		{
			var request = new HttpRequestMessage(HttpMethod.Post, "https://api.simplerouting.io/valhalla/route");
			ValhallaRoutingProvider.Authorize(request);
			return request;
		}

		[TestCase("bearer")]
		[TestCase("Bearer")]
		[TestCase(" BEARER ")]
		public void bearer_scheme_sends_the_key_as_an_authorization_header(string scheme)
		{
			MappingConfig.ValhallaApiKey = "sr_test_key";
			MappingConfig.ValhallaAuthScheme = scheme;

			using var request = AuthorizedRequest();

			request.Headers.Authorization.Should().NotBeNull();
			request.Headers.Authorization.Scheme.Should().Be("Bearer");
			request.Headers.Authorization.Parameter.Should().Be("sr_test_key");
			request.Headers.Contains("X-Api-Key").Should().BeFalse();
		}

		[Test]
		public void a_key_pasted_with_its_scheme_is_sent_once()
		{
			MappingConfig.ValhallaApiKey = "Bearer sr_test_key";
			MappingConfig.ValhallaAuthScheme = MappingConfig.ValhallaBearerAuthScheme;

			using var request = AuthorizedRequest();

			request.Headers.Authorization.ToString().Should().Be("Bearer sr_test_key");
		}

		[TestCase("apikey")]
		[TestCase("")]
		[TestCase(null)]
		public void the_default_scheme_sends_an_x_api_key_header(string scheme)
		{
			MappingConfig.ValhallaApiKey = "gateway-key";
			MappingConfig.ValhallaAuthScheme = scheme;

			using var request = AuthorizedRequest();

			request.Headers.GetValues("X-Api-Key").Should().Equal("gateway-key");
			request.Headers.Authorization.Should().BeNull();
		}

		[Test]
		public void no_key_sends_no_auth_header()
		{
			MappingConfig.ValhallaApiKey = "";
			MappingConfig.ValhallaAuthScheme = MappingConfig.ValhallaBearerAuthScheme;

			using var request = AuthorizedRequest();

			request.Headers.Authorization.Should().BeNull();
			request.Headers.Contains("X-Api-Key").Should().BeFalse();
		}

		[Test]
		public void no_coverage_setting_covers_everywhere()
		{
			MappingConfig.ValhallaCoverage = "";

			ValhallaRoutingProvider.IsCovered(new[] { new RoutingWaypoint(-33.8688, 151.2093) }).Should().BeTrue();
		}

		[TestCase(39.53, -119.81, true)]    // Reno
		[TestCase(45.4215, -75.6972, true)] // Ottawa
		[TestCase(19.4326, -99.1332, true)] // Mexico City
		[TestCase(48.8566, 2.3522, true)]   // Paris
		[TestCase(50.8503, 4.3517, true)]   // Brussels
		[TestCase(28.1235, -15.4363, true)] // Las Palmas
		[TestCase(-33.8688, 151.2093, false)] // Sydney
		[TestCase(-23.5505, -46.6333, false)] // Sao Paulo
		[TestCase(35.6762, 139.6503, false)]  // Tokyo
		[TestCase(-26.2041, 28.0473, false)]  // Johannesburg
		public void north_america_and_europe_coverage(double latitude, double longitude, bool covered)
		{
			MappingConfig.ValhallaCoverage = NorthAmericaAndEurope;

			ValhallaRoutingProvider.IsCovered(new[] { new RoutingWaypoint(latitude, longitude) }).Should().Be(covered);
		}

		[Test]
		public void one_point_outside_coverage_leaves_the_request_uncovered()
		{
			MappingConfig.ValhallaCoverage = NorthAmericaAndEurope;

			ValhallaRoutingProvider.IsCovered(new[] { new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(-33.8688, 151.2093) })
				.Should().BeFalse();
		}

		[TestCase(55, 175, true)]
		[TestCase(55, -175, true)]
		[TestCase(55, 0, false)]
		public void a_box_can_cross_the_antimeridian(double latitude, double longitude, bool covered)
		{
			MappingConfig.ValhallaCoverage = "170,50,-170,60";

			ValhallaRoutingProvider.IsCovered(new[] { new RoutingWaypoint(latitude, longitude) }).Should().Be(covered);
		}

		[Test]
		public void malformed_boxes_are_ignored()
		{
			MappingConfig.ValhallaCoverage = "junk;1,2,3;-170,14,-50,84;0,90,10,-90";

			ValhallaRoutingProvider.IsCovered(new[] { new RoutingWaypoint(39.53, -119.81) }).Should().BeTrue();
			ValhallaRoutingProvider.IsCovered(new[] { new RoutingWaypoint(48.8566, 2.3522) }).Should().BeFalse();
		}

		[Test]
		public void only_malformed_boxes_cover_everywhere()
		{
			MappingConfig.ValhallaCoverage = "junk;1,2,3";

			ValhallaRoutingProvider.IsCovered(new[] { new RoutingWaypoint(-33.8688, 151.2093) }).Should().BeTrue();
		}

		[Test]
		public async Task requests_outside_coverage_are_rejected_without_calling_the_server()
		{
			// Nothing listens on port 1: any HTTP call would throw instead of returning a rejection.
			MappingConfig.ValhallaUrl = "http://127.0.0.1:1";
			MappingConfig.ValhallaCoverage = NorthAmericaAndEurope;
			var provider = new ValhallaRoutingProvider();
			var reno = new RoutingWaypoint(39.53, -119.81);
			var sydney = new RoutingWaypoint(-33.8688, 151.2093);

			var route = await provider.GetRouteAsync(new RouteRequest { Waypoints = new List<RoutingWaypoint> { reno, sydney } }, CancellationToken.None);
			var matrix = await provider.GetMatrixAsync(new RouteMatrixRequest
			{
				Origins = new List<RoutingWaypoint> { reno, sydney },
				Destinations = new List<RoutingWaypoint> { reno }
			}, CancellationToken.None);
			var optimized = await provider.OptimizeAsync(new RouteOptimizationRequest
			{
				Waypoints = new List<RoutingWaypoint> { reno, sydney, reno },
				KeepFirst = true,
				KeepLast = true
			}, CancellationToken.None);

			route.Successful.Should().BeFalse();
			route.RequestRejected.Should().BeTrue();
			matrix.Successful.Should().BeFalse();
			matrix.RequestRejected.Should().BeTrue();
			optimized.Successful.Should().BeFalse();
			optimized.RequestRejected.Should().BeTrue();
		}

		[Test]
		public void limits_follow_the_server_settings()
		{
			MappingConfig.ValhallaMaxWaypoints = 50;
			MappingConfig.ValhallaMaxExcludePoints = 200;

			var capabilities = new ValhallaRoutingProvider().Capabilities;

			capabilities.MaxWaypoints.Should().Be(50);
			capabilities.MaxExcludePoints.Should().Be(200);
			capabilities.Navigation.Should().BeTrue();
			capabilities.ExcludeAreas.Should().BeTrue();

			MappingConfig.ValhallaMaxWaypoints = 20;
			MappingConfig.ValhallaMaxExcludePoints = 50;

			capabilities = new ValhallaRoutingProvider().Capabilities;
			capabilities.MaxWaypoints.Should().Be(20);
			capabilities.MaxExcludePoints.Should().Be(50);
		}

		// Bodies as Valhalla 3.9 returns them (captured from a local server): JSON errors for matrix and optimize, OSRM-format
		// errors for routes requested with format=osrm.
		[TestCase(400, "{\"error_code\":154,\"error\":\"Path distance exceeds the max distance limit: 400000 meters\",\"status_code\":400,\"status\":\"Bad Request\"}", true)]
		[TestCase(400, "{\"error_code\":171,\"error\":\"No suitable edges near location\",\"status_code\":400,\"status\":\"Bad Request\"}", true)]
		[TestCase(400, "{\"code\":\"NoSegment\",\"message\":\"One of the supplied input coordinates could not snap to street segment.\"}", true)]
		[TestCase(400, "{\"code\":\"InvalidValue\",\"message\":\"The successfully parsed query parameters are invalid.\"}", true)]
		[TestCase(400, "{\"code\":\"Ok\"}", false)]
		[TestCase(400, "[]", false)]
		[TestCase(400, null, false)]
		[TestCase(401, "{\"error\":\"Unauthorized\"}", false)]
		[TestCase(403, "{\"error_code\":100,\"error\":\"Forbidden\"}", false)]
		[TestCase(429, "{\"error\":\"Too many requests\"}", false)]
		[TestCase(500, "{\"error_code\":499,\"error\":\"Unknown\"}", false)]
		public void only_a_valhalla_400_is_a_request_rejection(int status, string body, bool rejected)
		{
			using var document = body == null ? null : JsonDocument.Parse(body);

			ValhallaRoutingProvider.IsRejection(status, document).Should().Be(rejected);
		}
	}
}
