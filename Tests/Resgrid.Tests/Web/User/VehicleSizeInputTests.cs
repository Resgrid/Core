using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Units;

namespace Resgrid.Tests.Web.User
{
	/// <summary>Vehicle size fields of the unit type and unit pages: entered in either unit system, stored metric.</summary>
	[TestFixture]
	public class VehicleSizeInputTests
	{
		[Test]
		public void imperial_values_are_stored_metric()
		{
			var input = new VehicleSizeInput { Metric = false, Height = 11.5, Width = 8.5, Length = 40, GrossWeight = 60000, AxleLoad = 20000, AxleCount = 3, Hazmat = "true" };
			var type = new UnitType();

			input.Validate().Should().BeEmpty();
			input.ApplyTo(type);

			type.VehicleHeightMeters.Should().BeApproximately(3.505, 0.001);
			type.VehicleWidthMeters.Should().BeApproximately(2.591, 0.001);
			type.VehicleLengthMeters.Should().BeApproximately(12.192, 0.001);
			type.VehicleGrossWeightKg.Should().BeApproximately(27215.5, 0.1);
			type.VehicleAxleCount.Should().Be(3);
			type.VehicleHazmat.Should().BeTrue();
		}

		[Test]
		public void blank_fields_store_null_and_a_unit_can_override_hazmat_off()
		{
			var unit = new Unit { VehicleHeightMeters = 3.2 };

			new VehicleSizeInput { Metric = true, Hazmat = "false" }.ApplyTo(unit);

			unit.VehicleHeightMeters.Should().BeNull();
			unit.VehicleHazmat.Should().BeFalse();

			new VehicleSizeInput { Metric = true, Hazmat = "" }.ApplyTo(unit);
			unit.VehicleHazmat.Should().BeNull();
		}

		[Test]
		public void out_of_range_values_are_reported_in_the_entered_units()
		{
			var errors = new VehicleSizeInput { Metric = true, Height = 9, AxleCount = 20, GrossWeight = 100 }.Validate();

			errors.Select(e => e.Field).Should().BeEquivalentTo(new[] { "Height", "GrossWeight", "AxleCount" });
			errors.First(e => e.Field == "Height").High.Should().Be("6 m");

			var imperial = new VehicleSizeInput { Metric = false, Height = 30 }.Validate();
			imperial.Single().High.Should().EndWith(" ft");
		}

		[Test]
		public void stored_values_round_trip_through_the_form()
		{
			var type = new UnitType { VehicleHeightMeters = 3.6, VehicleGrossWeightKg = 18000 };

			var shown = VehicleSizeInput.From(type, metric: false);
			shown.Height.Should().BeApproximately(11.81, 0.01);
			shown.GrossWeight.Should().Be(39683);

			var back = new UnitType();
			shown.ApplyTo(back);
			back.VehicleHeightMeters.Should().BeApproximately(3.6, 0.01);
			back.VehicleGrossWeightKg.Should().BeApproximately(18000, 1);
		}

		[Test]
		public void provider_limits_follow_the_routing_provider()
		{
			VehicleSizeInput.LimitsFor("valhalla").Should().Be("all");
			VehicleSizeInput.LimitsFor("Mapbox").Should().Be("size");
			VehicleSizeInput.LimitsFor("google").Should().Be("none");
			VehicleSizeInput.LimitsFor("osrm").Should().Be("none");
		}
	}
}
