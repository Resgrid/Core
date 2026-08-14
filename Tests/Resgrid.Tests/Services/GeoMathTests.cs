using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	namespace GeoMathTests
	{
		[TestFixture]
		public class when_parsing_geofences
		{
			[Test]
			public void should_parse_current_lat_lng_format()
			{
				var json = "[{\"lat\":39.7,\"lng\":-104.9},{\"lat\":39.8,\"lng\":-104.9},{\"lat\":39.8,\"lng\":-104.8}]";

				var polygon = GeoMath.ParseGeofence(json);

				polygon.Should().NotBeNull();
				polygon.Count.Should().Be(3);
				polygon[0].Latitude.Should().BeApproximately(39.7, 0.0001);
				polygon[0].Longitude.Should().BeApproximately(-104.9, 0.0001);
			}

			[Test]
			public void should_parse_legacy_k_A_format()
			{
				var json = "[{\"k\":39.7,\"A\":-104.9},{\"k\":39.8,\"A\":-104.9},{\"k\":39.8,\"A\":-104.8}]";

				var polygon = GeoMath.ParseGeofence(json);

				polygon.Should().NotBeNull();
				polygon.Count.Should().Be(3);
				polygon[1].Latitude.Should().BeApproximately(39.8, 0.0001);
			}

			[Test]
			public void should_parse_string_encoded_numbers()
			{
				var json = "[{\"lat\":\"39.7\",\"lng\":\"-104.9\"},{\"lat\":\"39.8\",\"lng\":\"-104.9\"},{\"lat\":\"39.8\",\"lng\":\"-104.8\"}]";

				GeoMath.ParseGeofence(json).Should().NotBeNull();
			}

			[Test]
			public void should_return_null_for_null_empty_or_garbage()
			{
				GeoMath.ParseGeofence(null).Should().BeNull();
				GeoMath.ParseGeofence("").Should().BeNull();
				GeoMath.ParseGeofence("   ").Should().BeNull();
				GeoMath.ParseGeofence("not json").Should().BeNull();
				GeoMath.ParseGeofence("{\"lat\":1}").Should().BeNull();
				GeoMath.ParseGeofence("[{\"foo\":1,\"bar\":2},{\"foo\":1,\"bar\":2},{\"foo\":1,\"bar\":2}]").Should().BeNull();
			}

			[Test]
			public void should_return_null_for_non_finite_or_out_of_range_vertices()
			{
				GeoMath.ParseGeofence("[{\"lat\":NaN,\"lng\":-104.9},{\"lat\":39.8,\"lng\":-104.9},{\"lat\":39.8,\"lng\":-104.8}]").Should().BeNull();
				GeoMath.ParseGeofence("[{\"lat\":\"NaN\",\"lng\":\"-104.9\"},{\"lat\":\"39.8\",\"lng\":\"-104.9\"},{\"lat\":\"39.8\",\"lng\":\"-104.8\"}]").Should().BeNull();
				GeoMath.ParseGeofence("[{\"lat\":500,\"lng\":-104.9},{\"lat\":39.8,\"lng\":-104.9},{\"lat\":39.8,\"lng\":-104.8}]").Should().BeNull();
				GeoMath.ParseGeofence("[{\"lat\":39.7,\"lng\":-500},{\"lat\":39.8,\"lng\":-104.9},{\"lat\":39.8,\"lng\":-104.8}]").Should().BeNull();
			}

			[Test]
			public void should_return_null_for_degenerate_polygons()
			{
				GeoMath.ParseGeofence("[]").Should().BeNull();
				GeoMath.ParseGeofence("[{\"lat\":39.7,\"lng\":-104.9}]").Should().BeNull();
				GeoMath.ParseGeofence("[{\"lat\":39.7,\"lng\":-104.9},{\"lat\":39.8,\"lng\":-104.9}]").Should().BeNull();
			}
		}

		[TestFixture]
		public class when_testing_point_in_polygon
		{
			// A simple square around downtown Denver.
			private static readonly List<GeoMath.GeoPoint> Square = new List<GeoMath.GeoPoint>
			{
				new GeoMath.GeoPoint(39.70, -105.00),
				new GeoMath.GeoPoint(39.80, -105.00),
				new GeoMath.GeoPoint(39.80, -104.90),
				new GeoMath.GeoPoint(39.70, -104.90)
			};

			[Test]
			public void should_detect_point_inside()
			{
				GeoMath.IsPointInPolygon(39.75, -104.95, Square).Should().BeTrue();
			}

			[Test]
			public void should_detect_point_outside()
			{
				GeoMath.IsPointInPolygon(39.85, -104.95, Square).Should().BeFalse();
				GeoMath.IsPointInPolygon(39.75, -104.85, Square).Should().BeFalse();
				GeoMath.IsPointInPolygon(0, 0, Square).Should().BeFalse();
			}

			[Test]
			public void should_handle_concave_polygons()
			{
				// A "U" shape: the notch between the arms is outside.
				var u = new List<GeoMath.GeoPoint>
				{
					new GeoMath.GeoPoint(0, 0),
					new GeoMath.GeoPoint(0, 10),
					new GeoMath.GeoPoint(10, 10),
					new GeoMath.GeoPoint(10, 7),
					new GeoMath.GeoPoint(2, 7),
					new GeoMath.GeoPoint(2, 3),
					new GeoMath.GeoPoint(10, 3),
					new GeoMath.GeoPoint(10, 0)
				};

				GeoMath.IsPointInPolygon(1, 5, u).Should().BeTrue();   // bottom of the U
				GeoMath.IsPointInPolygon(5, 5, u).Should().BeFalse();  // inside the notch
				GeoMath.IsPointInPolygon(5, 8, u).Should().BeTrue();   // right arm
			}

			[Test]
			public void should_return_false_for_missing_or_degenerate_polygon()
			{
				GeoMath.IsPointInPolygon(39.75, -104.95, null).Should().BeFalse();
				GeoMath.IsPointInPolygon(39.75, -104.95, new List<GeoMath.GeoPoint>()).Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_computing_centroid_and_distance
		{
			[Test]
			public void centroid_of_square_is_its_center()
			{
				var square = new List<GeoMath.GeoPoint>
				{
					new GeoMath.GeoPoint(0, 0),
					new GeoMath.GeoPoint(0, 10),
					new GeoMath.GeoPoint(10, 10),
					new GeoMath.GeoPoint(10, 0)
				};

				var centroid = GeoMath.Centroid(square);

				centroid.Latitude.Should().BeApproximately(5, 0.0001);
				centroid.Longitude.Should().BeApproximately(5, 0.0001);
			}

			[Test]
			public void haversine_matches_known_distance()
			{
				// Denver (39.7392, -104.9903) to Colorado Springs (38.8339, -104.8214) ≈ 101.6 km.
				var meters = GeoMath.HaversineMeters(39.7392, -104.9903, 38.8339, -104.8214);

				meters.Should().BeInRange(99000, 104000);
			}

			[Test]
			public void haversine_is_zero_for_identical_points()
			{
				GeoMath.HaversineMeters(39.7392, -104.9903, 39.7392, -104.9903).Should().BeApproximately(0, 0.001);
			}
		}

		[TestFixture]
		public class when_parsing_coordinate_strings
		{
			[Test]
			public void should_parse_valid_pairs_and_lat_lon_blobs()
			{
				var pair = GeoMath.ParseCoordinatePair("39.7392", "-104.9903");
				pair.Should().NotBeNull();
				pair.Value.Latitude.Should().BeApproximately(39.7392, 0.0001);

				var blob = GeoMath.ParseLatLonString("39.7392,-104.9903");
				blob.Should().NotBeNull();
				blob.Value.Longitude.Should().BeApproximately(-104.9903, 0.0001);
			}

			[Test]
			public void should_reject_non_finite_and_out_of_range_coordinates()
			{
				GeoMath.ParseCoordinatePair("NaN", "-104.9").Should().BeNull();
				GeoMath.ParseCoordinatePair("39.7", "NaN").Should().BeNull();
				GeoMath.ParseCoordinatePair("Infinity", "-104.9").Should().BeNull();
				GeoMath.ParseCoordinatePair("39.7", "-Infinity").Should().BeNull();
				GeoMath.ParseCoordinatePair("90.1", "-104.9").Should().BeNull();
				GeoMath.ParseCoordinatePair("-90.1", "-104.9").Should().BeNull();
				GeoMath.ParseCoordinatePair("39.7", "180.1").Should().BeNull();
				GeoMath.ParseCoordinatePair("39.7", "-180.1").Should().BeNull();
				GeoMath.ParseLatLonString("NaN,-104.9").Should().BeNull();
			}

			[Test]
			public void should_accept_coordinates_on_the_range_boundaries()
			{
				GeoMath.ParseCoordinatePair("90", "180").Should().NotBeNull();
				GeoMath.ParseCoordinatePair("-90", "-180").Should().NotBeNull();
			}

			[Test]
			public void should_reject_missing_zero_or_garbage_input()
			{
				GeoMath.ParseCoordinatePair(null, "-104.9").Should().BeNull();
				GeoMath.ParseCoordinatePair("39.7", "").Should().BeNull();
				GeoMath.ParseCoordinatePair("abc", "def").Should().BeNull();
				GeoMath.ParseCoordinatePair("0", "0").Should().BeNull();
				GeoMath.ParseLatLonString(null).Should().BeNull();
				GeoMath.ParseLatLonString("39.7392").Should().BeNull();
				GeoMath.ParseLatLonString("a,b").Should().BeNull();
			}
		}
	}
}
