using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// Where a unit is measured from for dispatch ranking, and the turnout that goes with it. Shared by the run card
	/// engine and the nearest-unit board.
	/// </summary>
	[TestFixture]
	public class UnitResponseOriginTests
	{
		private static readonly GeoMath.GeoPoint Gps = new GeoMath.GeoPoint(50.85, 4.35);
		private static readonly GeoMath.GeoPoint Station = new GeoMath.GeoPoint(50.80, 4.40);

		private static DispatchRecommendationConfig Config() =>
			new DispatchRecommendationConfig { InQuartersTurnoutSeconds = 120, MobileTurnoutSeconds = 30 };

		[Test]
		public void an_in_quarters_unit_is_measured_from_its_station_whatever_its_gps_says()
		{
			var origin = UnitResponseOrigin.Resolve(true, Gps, Station, Config());

			origin.Source.Should().Be(UnitPositionSources.Station);
			origin.Point.Value.Latitude.Should().Be(Station.Latitude);
			origin.TurnoutSeconds.Should().Be(120);
		}

		[Test]
		public void a_unit_that_is_out_is_measured_from_its_gps_with_the_mobile_turnout()
		{
			var origin = UnitResponseOrigin.Resolve(false, Gps, Station, Config());

			origin.Source.Should().Be(UnitPositionSources.Live);
			origin.Point.Value.Latitude.Should().Be(Gps.Latitude);
			origin.TurnoutSeconds.Should().Be(30);
		}

		[Test]
		public void a_unit_with_no_gps_is_taken_to_be_at_its_station()
		{
			var origin = UnitResponseOrigin.Resolve(false, null, Station, Config());

			origin.Source.Should().Be(UnitPositionSources.Station);
			origin.TurnoutSeconds.Should().Be(120);
		}

		[Test]
		public void an_in_quarters_unit_without_a_located_station_falls_back_to_its_gps()
		{
			var origin = UnitResponseOrigin.Resolve(true, Gps, null, Config());

			origin.Source.Should().Be(UnitPositionSources.Live);
			origin.TurnoutSeconds.Should().Be(30);
		}

		[Test]
		public void a_unit_with_neither_gps_nor_a_located_station_has_no_position_and_no_turnout()
		{
			var origin = UnitResponseOrigin.Resolve(true, null, null, Config());

			origin.Source.Should().Be(UnitPositionSources.None);
			origin.Point.Should().BeNull();
			origin.TurnoutSeconds.Should().Be(0);
		}

		[Test]
		public void without_turnouts_configured_nothing_is_added()
		{
			UnitResponseOrigin.Resolve(true, Gps, Station, new DispatchRecommendationConfig()).TurnoutSeconds.Should().Be(0);
			UnitResponseOrigin.Resolve(false, Gps, Station, null).TurnoutSeconds.Should().Be(0);
		}

		[Test]
		public void only_a_custom_status_with_the_in_quarters_base_type_is_in_quarters()
		{
			var details = new Dictionary<int, CustomStateDetail>
			{
				{ 900, new CustomStateDetail { CustomStateDetailId = 900, ButtonText = "In Kazerne", BaseType = (int)ActionBaseTypes.InQuarters } },
				{ 901, new CustomStateDetail { CustomStateDetailId = 901, ButtonText = "Radiofonisch", BaseType = (int)ActionBaseTypes.Available } }
			};

			UnitResponseOrigin.IsInQuarters(900, details).Should().BeTrue();
			UnitResponseOrigin.IsInQuarters(901, details).Should().BeFalse();
			UnitResponseOrigin.IsInQuarters((int)UnitStateTypes.Available, details).Should().BeFalse();
			UnitResponseOrigin.IsInQuarters(900, null).Should().BeFalse();
		}

		[Test]
		public void in_quarters_is_an_available_status_that_puts_a_unit_back_in_service()
		{
			AvailabilityMatrix.ForCustomBaseType((int)ActionBaseTypes.InQuarters).Should().Be(AvailabilityClass.Available);

			var baseTypes = new Dictionary<int, int> { { 900, (int)ActionBaseTypes.InQuarters } };
			CallStatusLinkage.ResolveUnitStateKind(900, baseTypes).Should().Be(UnitStateTypes.Available);
			CallStatusLinkage.IsClearingPersonnelStatus(900, baseTypes).Should().BeTrue();
		}

		[Test]
		public void travel_is_estimated_over_a_road_adjusted_distance_at_40_kmh()
		{
			// 1 km straight line = 1.3 km of road at 40 km/h = 117 s.
			UnitResponseOrigin.EstimateTravelSeconds(1000).Should().Be(117);
		}
	}
}
