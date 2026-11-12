using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>Travel modes (how a unit gets to an incident) and road closure shapes and times (M0314).</summary>
	[TestFixture]
	public class TravelModeAndClosureModelTests
	{
		// A straight east-west route through Ghent at latitude 51.05, 3.70 to 3.74 (about 2.8 km).
		private static readonly List<GeoMath.GeoPoint> EastWestRoute = new List<GeoMath.GeoPoint>
		{
			new GeoMath.GeoPoint(51.05, 3.70),
			new GeoMath.GeoPoint(51.05, 3.74)
		};

		// 0.01 degrees of longitude at latitude 51.05 is about 700 m.
		private const double SpanMeters = 700;

		private const string BlockPolygon = "{\"type\":\"Polygon\",\"coordinates\":[[[3.715,51.049],[3.725,51.049],[3.725,51.051],[3.715,51.051],[3.715,51.049]]]}";
		private const string AlongRouteLine = "{\"type\":\"LineString\",\"coordinates\":[[3.715,51.05],[3.725,51.05]]}";
		private const string CrossStreetLine = "{\"type\":\"LineString\",\"coordinates\":[[3.72,51.04],[3.72,51.06]]}";

		#region Travel profile

		[Test]
		public void a_unit_uses_its_types_mode_and_speed_unless_it_sets_its_own()
		{
			var walkingType = new UnitType { Type = "BLS Team", TravelMode = (int)UnitTravelModes.Walking, TravelSpeedKmh = 4 };

			var inherited = UnitTravelProfile.Resolve(new Unit(), walkingType);
			inherited.Mode.Should().Be(UnitTravelModes.Walking);
			inherited.SpeedKmh.Should().Be(4);

			var ownSpeed = UnitTravelProfile.Resolve(new Unit { TravelSpeedKmh = 6 }, walkingType);
			ownSpeed.Mode.Should().Be(UnitTravelModes.Walking);
			ownSpeed.SpeedKmh.Should().Be(6);

			// A unit that changes its mode doesn't inherit the type's walking speed for cycling.
			var cycling = UnitTravelProfile.Resolve(new Unit { TravelMode = (int)UnitTravelModes.Cycling }, walkingType);
			cycling.Mode.Should().Be(UnitTravelModes.Cycling);
			cycling.SpeedKmh.Should().Be(UnitTravelProfile.DefaultCyclingSpeedKmh);
		}

		[Test]
		public void no_mode_anywhere_is_driving_at_the_old_estimate_speed()
		{
			var profile = UnitTravelProfile.Resolve(new Unit(), new UnitType());

			profile.Should().BeSameAs(UnitTravelProfile.Default);
			profile.EstimateSeconds(10000).Should().Be(UnitResponseOrigin.EstimateTravelSeconds(10000), "driving units rank exactly as before travel modes");
			profile.RoutingProfile.Should().BeNull("driving keeps picking plain, traffic or truck routing");
		}

		[Test]
		public void walking_and_cycling_estimates_follow_the_road_factor_and_air_flies_the_straight_line()
		{
			new UnitTravelProfile(UnitTravelModes.Walking, null).EstimateSeconds(1000).Should().Be(Math.Round(1300 / (5000d / 3600d)));
			new UnitTravelProfile(UnitTravelModes.Cycling, null).EstimateSeconds(1000).Should().Be(Math.Round(1300 / (15000d / 3600d)));
			new UnitTravelProfile(UnitTravelModes.Air, 200).EstimateSeconds(10000).Should().Be(180, "10 km at 200 km/h, no road factor");

			new UnitTravelProfile(UnitTravelModes.Walking, null).RoutingProfile.Should().Be(RoutingProfile.Walking);
			new UnitTravelProfile(UnitTravelModes.Cycling, null).RoutingProfile.Should().Be(RoutingProfile.Cycling);
			new UnitTravelProfile(UnitTravelModes.Air, null).IsAir.Should().BeTrue();
		}

		[Test]
		public void an_out_of_range_speed_is_clamped_and_an_unknown_mode_is_driving()
		{
			new UnitTravelProfile(UnitTravelModes.Air, 50000).SpeedKmh.Should().Be(UnitTravelProfile.MaximumSpeedKmh);
			new UnitTravelProfile(UnitTravelModes.Walking, -3).SpeedKmh.Should().Be(UnitTravelProfile.DefaultWalkingSpeedKmh);
			UnitTravelProfile.ToMode(42).Should().Be(UnitTravelModes.Driving);
		}

		[Test]
		public void lookup_keys_separate_units_that_travel_differently()
		{
			var walking = new TravelLookup { Travel = new UnitTravelProfile(UnitTravelModes.Walking, null) };
			var driving = new TravelLookup { Travel = UnitTravelProfile.Default };
			var truck = new TravelLookup { Travel = UnitTravelProfile.Default, Vehicle = new VehicleProfile { HeightMeters = 3.5 } };

			new[] { walking.Key(), driving.Key(), truck.Key() }.Should().OnlyHaveUniqueItems();
		}

		#endregion

		#region Closure shapes

		[Test]
		public void shapes_parse_from_geometries_features_and_collections()
		{
			RoadClosureShape.TryParse(BlockPolygon, out var polygon, out _).Should().BeTrue();
			polygon.Polygons.Should().ContainSingle().Which.Should().HaveCount(4, "the closing point is dropped");

			var collection = "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{},\"geometry\":" + BlockPolygon + "}," +
				"{\"type\":\"Feature\",\"properties\":{},\"geometry\":" + AlongRouteLine + "},{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[3.7,51.0]}}]}";
			RoadClosureShape.TryParse(collection, out var shape, out _).Should().BeTrue();
			shape.Polygons.Should().HaveCount(1);
			shape.Lines.Should().HaveCount(1, "points carry no closure and are ignored");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not json")]
		[TestCase("{\"type\":\"Point\",\"coordinates\":[3.7,51.0]}")]
		[TestCase("{\"type\":\"LineString\",\"coordinates\":[[3.7,51.0]]}")]
		[TestCase("{\"type\":\"LineString\",\"coordinates\":[[3.7,95.0],[3.8,51.0]]}")]
		[TestCase("{\"type\":\"Polygon\",\"coordinates\":[[[3.7,51.0],[3.8,51.0],[3.7,51.0]]]}")]
		public void unusable_shapes_are_refused(string geoJson)
		{
			RoadClosureShape.TryParse(geoJson, out var shape, out var error).Should().BeFalse();
			shape.Should().BeNull();
			error.Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public void a_route_through_an_area_counts_every_metre_inside_it()
		{
			var measure = RoadClosureShape.ParseOrNull(BlockPolygon).Measure(EastWestRoute);

			measure.Crosses.Should().BeTrue();
			measure.MetersInside.Should().BeApproximately(SpanMeters, 30);
			measure.EntryPoints.Should().ContainSingle().Which.Longitude.Should().BeApproximately(3.715, 0.0005);
		}

		[Test]
		public void a_route_along_a_closed_road_line_counts_but_crossing_it_at_a_junction_does_not()
		{
			RoadClosureShape.ParseOrNull(AlongRouteLine).Measure(EastWestRoute).MetersInside.Should().BeApproximately(SpanMeters + 2 * RoadClosureShape.LineBufferMeters, 30);

			RoadClosureShape.ParseOrNull(CrossStreetLine).Measure(EastWestRoute).Crosses
				.Should().BeFalse("crossing a closed road leaves the route on its own road");
		}

		[Test]
		public void a_route_that_stays_clear_is_not_measured_inside()
		{
			var northern = new List<GeoMath.GeoPoint> { new GeoMath.GeoPoint(51.06, 3.70), new GeoMath.GeoPoint(51.06, 3.74) };

			RoadClosureShape.ParseOrNull(BlockPolygon).Measure(northern).Crosses.Should().BeFalse();
		}

		[Test]
		public void exclusion_points_lie_along_the_line_between_its_vertices_and_respect_the_cap()
		{
			var shape = RoadClosureShape.ParseOrNull(AlongRouteLine);

			var points = shape.LineExclusionPoints(50);
			points.Should().NotBeEmpty();
			points.Should().OnlyContain(p => Math.Abs(p.Latitude - 51.05) < 1e-9 && p.Longitude > 3.715 && p.Longitude < 3.725);

			shape.LineExclusionPoints(3).Should().HaveCount(3);
		}

		[Test]
		public void contains_covers_areas_and_a_buffer_around_lines()
		{
			RoadClosureShape.ParseOrNull(BlockPolygon).Contains(51.05, 3.72).Should().BeTrue();
			RoadClosureShape.ParseOrNull(BlockPolygon).Contains(51.06, 3.72).Should().BeFalse();
			RoadClosureShape.ParseOrNull(AlongRouteLine).Contains(51.05005, 3.72).Should().BeTrue("about 6 m from the line");
			RoadClosureShape.ParseOrNull(AlongRouteLine).Contains(51.0505, 3.72).Should().BeFalse("about 55 m from the line");
		}

		[Test]
		public void normalize_writes_a_feature_collection_that_parses_back_to_the_same_shape()
		{
			var normalized = RoadClosureShape.Normalize("{\"type\":\"GeometryCollection\",\"geometries\":[" + BlockPolygon + "," + AlongRouteLine + "]}");

			normalized.Should().StartWith("{\"type\":\"FeatureCollection\"");
			RoadClosureShape.TryParse(normalized, out var shape, out _).Should().BeTrue();
			shape.Polygons.Should().HaveCount(1);
			shape.Lines.Should().HaveCount(1);
		}

		#endregion

		#region Closure times

		[Test]
		public void a_closure_is_in_effect_only_during_its_windows_and_never_when_switched_off()
		{
			var now = new DateTime(2026, 11, 12, 23, 0, 0, DateTimeKind.Utc);
			var nightly = new List<RoadClosureWindow>
			{
				new RoadClosureWindow { StartsOn = now.AddHours(-1), EndsOn = now.AddHours(7) },
				new RoadClosureWindow { StartsOn = now.AddHours(23), EndsOn = now.AddHours(31) }
			};

			RoadClosureWindows.StateAt(true, nightly, now).Should().Be(RoadClosureStates.Active);
			RoadClosureWindows.StateAt(true, nightly, now.AddHours(10)).Should().Be(RoadClosureStates.Scheduled);
			RoadClosureWindows.StateAt(true, nightly, now.AddDays(3)).Should().Be(RoadClosureStates.Ended);
			RoadClosureWindows.StateAt(false, nightly, now).Should().Be(RoadClosureStates.Disabled);
			RoadClosureWindows.StateAt(true, new List<RoadClosureWindow>(), now).Should().Be(RoadClosureStates.Active, "no times = until switched off");
			RoadClosureWindows.StateAt(true, new List<RoadClosureWindow> { new RoadClosureWindow { StartsOn = now.AddDays(-1) } }, now.AddYears(1))
				.Should().Be(RoadClosureStates.Active, "no end = until further notice");

			RoadClosureWindows.CurrentOrNext(nightly, now.AddHours(10)).StartsOn.Should().Be(now.AddHours(23));
		}

		[Test]
		public void repeat_builds_a_nightly_closure_for_a_week_that_runs_past_midnight()
		{
			var windows = RoadClosureWindows.Repeat(new DateTime(2026, 11, 16), new DateTime(2026, 11, 22), TimeSpan.FromHours(22), TimeSpan.FromHours(6),
				null, local => local.AddHours(-1));

			windows.Should().HaveCount(7);
			windows[0].StartsOn.Should().Be(new DateTime(2026, 11, 16, 21, 0, 0, DateTimeKind.Utc));
			windows[0].EndsOn.Should().Be(new DateTime(2026, 11, 17, 5, 0, 0, DateTimeKind.Utc));
			windows.Should().OnlyContain(w => w.StartsOn.Kind == DateTimeKind.Utc);
		}

		[Test]
		public void repeat_keeps_only_the_chosen_weekdays()
		{
			var weekends = RoadClosureWindows.Repeat(new DateTime(2026, 11, 16), new DateTime(2026, 11, 29), TimeSpan.FromHours(8), TimeSpan.FromHours(18),
				new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }, null);

			weekends.Should().HaveCount(4);
			weekends.Should().OnlyContain(w => w.StartsOn.DayOfWeek == DayOfWeek.Saturday || w.StartsOn.DayOfWeek == DayOfWeek.Sunday);
		}

		[Test]
		public void stored_windows_are_sorted_deduplicated_and_drop_backwards_ones()
		{
			var start = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
			var closure = new RoadClosure
			{
				Windows = new List<RoadClosureWindow>
				{
					new RoadClosureWindow { StartsOn = start.AddDays(1), EndsOn = start.AddDays(1).AddHours(2) },
					new RoadClosureWindow { StartsOn = start, EndsOn = start.AddHours(2) },
					new RoadClosureWindow { StartsOn = start, EndsOn = start.AddHours(2) },
					new RoadClosureWindow { StartsOn = start, EndsOn = start.AddHours(-2) }
				}
			};

			closure.Windows.Select(w => w.StartsOn).Should().Equal(start, start.AddDays(1));
		}

		[Test]
		public void a_closure_applies_only_to_the_travel_modes_it_stops_and_never_to_air()
		{
			var closure = new RoadClosure { IsEnabled = true, AppliesTo = (int)(RoadClosureTravelModes.Driving | RoadClosureTravelModes.Cycling) };
			var now = DateTime.UtcNow;

			closure.AppliesAt(now, UnitTravelModes.Driving).Should().BeTrue();
			closure.AppliesAt(now, UnitTravelModes.Cycling).Should().BeTrue();
			closure.AppliesAt(now, UnitTravelModes.Walking).Should().BeFalse("a marathon course stops vehicles, not foot teams");
			closure.AppliesAt(now, UnitTravelModes.Air).Should().BeFalse();
		}

		#endregion
	}
}
