using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>The driven track stored when a route instance ends (§10.4, RD6).</summary>
	[TestFixture]
	public class RouteTrackBuilderTests
	{
		private static readonly DateTime Start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

		[Test]
		public void fewer_than_two_distinct_points_give_no_track()
		{
			var track = RouteTrackBuilder.Build(new[] { (39.5, -119.8, Start), (39.50001, -119.80001, Start.AddSeconds(5)), (0.0, 0.0, Start.AddSeconds(9)) });

			track.GeometryGeoJson.Should().BeNull();
			track.DistanceMeters.Should().Be(0);
		}

		[Test]
		public void points_are_ordered_by_time_and_jitter_is_dropped()
		{
			var track = RouteTrackBuilder.Build(new[]
			{
				(39.51, -119.80, Start.AddMinutes(2)),
				(39.50, -119.80, Start),
				(39.50002, -119.80, Start.AddSeconds(30))
			});

			track.PointCount.Should().Be(2);
			track.DistanceMeters.Should().BeApproximately(1112, 2);
			track.GeometryGeoJson.Should().Be("{\"type\":\"LineString\",\"coordinates\":[[-119.8,39.5],[-119.8,39.51]]}");
		}

		[Test]
		public void a_straight_drive_simplifies_to_its_ends_and_corners_survive()
		{
			var straight = Enumerable.Range(0, 50).Select(i => (39.5 + i * 0.0005, -119.8)).ToList();
			RouteTrackBuilder.Simplify(straight, 8).Should().HaveCount(2);

			var corner = new List<(double, double)> { (39.50, -119.80), (39.51, -119.80), (39.51, -119.79) };
			RouteTrackBuilder.Simplify(corner, 8).Should().HaveCount(3);
		}
	}
}
