using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// Pins UnitStateTelemetryLimits: device telemetry the SQL Server UnitStates columns can't hold used to fail the
	/// insert with "Arithmetic overflow error converting numeric to data type numeric" (Sentry, POST v4/UnitStatus/
	/// SaveUnitStatus from Edge on Windows) and lose the status — the old cap set Accuracy to 100000 into a decimal(6,2).
	/// </summary>
	[TestFixture]
	public class UnitStateTelemetryLimitsTests
	{
		private static UnitState Valid() => new UnitState
		{
			Latitude = 50.8503396m,
			Longitude = 4.3517103m,
			Accuracy = 12.5m,
			Altitude = 56m,
			AltitudeAccuracy = 3m,
			Speed = 13.4m,
			Heading = 271.25m
		};

		[Test]
		public void Values_that_fit_are_left_as_sent()
		{
			var state = Valid();
			state.Speed = -1m; // platform "unknown" sentinel: storable, so not this helper's business

			UnitStateTelemetryLimits.FitToColumns(state);

			state.Latitude.Should().Be(50.8503396m);
			state.Longitude.Should().Be(4.3517103m);
			state.Accuracy.Should().Be(12.5m);
			state.Altitude.Should().Be(56m);
			state.AltitudeAccuracy.Should().Be(3m);
			state.Speed.Should().Be(-1m);
			state.Heading.Should().Be(271.25m);
		}

		[Test]
		public void Nulls_stay_null()
		{
			var state = new UnitState();

			UnitStateTelemetryLimits.FitToColumns(state);

			state.Latitude.Should().BeNull();
			state.Accuracy.Should().BeNull();
			state.Speed.Should().BeNull();
		}

		[TestCase("15000")]
		[TestCase("100000")]
		[TestCase("9999.995")] // rounds to 10000.00 in SQL Server
		public void A_huge_accuracy_is_capped_at_the_column_maximum(string reported)
		{
			var state = Valid();
			state.Accuracy = decimal.Parse(reported, System.Globalization.CultureInfo.InvariantCulture);
			state.AltitudeAccuracy = state.Accuracy;

			UnitStateTelemetryLimits.FitToColumns(state);

			state.Accuracy.Should().Be(9999.99m);
			state.AltitudeAccuracy.Should().Be(9999.99m);
			state.Latitude.Should().Be(50.8503396m, "the position itself is still the device's reading");
		}

		[Test]
		public void An_accuracy_just_under_the_rounding_edge_is_kept()
		{
			var state = Valid();
			state.Accuracy = 9999.994m;

			UnitStateTelemetryLimits.FitToColumns(state);

			state.Accuracy.Should().Be(9999.994m);
		}

		[Test]
		public void A_huge_negative_accuracy_is_dropped()
		{
			var state = Valid();
			state.Accuracy = -50000m;

			UnitStateTelemetryLimits.FitToColumns(state);

			state.Accuracy.Should().BeNull();
		}

		[Test]
		public void Unstorable_altitude_speed_and_heading_are_dropped()
		{
			var state = Valid();
			state.Altitude = 123456m;
			state.Speed = 1000m;
			state.Heading = -1000m;

			UnitStateTelemetryLimits.FitToColumns(state);

			state.Altitude.Should().BeNull();
			state.Speed.Should().BeNull();
			state.Heading.Should().BeNull();
			state.Accuracy.Should().Be(12.5m);
		}

		[Test]
		public void An_unstorable_coordinate_drops_the_pair()
		{
			var state = Valid();
			state.Longitude = 4351710.3m; // e.g. microdegrees sent as degrees

			UnitStateTelemetryLimits.FitToColumns(state);

			state.Latitude.Should().BeNull();
			state.Longitude.Should().BeNull();
		}

		[Test]
		public void Null_state_is_ignored()
		{
			FluentActions.Invoking(() => UnitStateTelemetryLimits.FitToColumns(null)).Should().NotThrow();
		}

		private static readonly Dictionary<string, decimal> LimitByProperty = new Dictionary<string, decimal>
		{
			[nameof(UnitState.Latitude)] = UnitStateTelemetryLimits.MaxCoordinate,
			[nameof(UnitState.Longitude)] = UnitStateTelemetryLimits.MaxCoordinate,
			[nameof(UnitState.Accuracy)] = UnitStateTelemetryLimits.MaxAccuracy,
			[nameof(UnitState.AltitudeAccuracy)] = UnitStateTelemetryLimits.MaxAccuracy,
			[nameof(UnitState.Altitude)] = UnitStateTelemetryLimits.MaxAltitude,
			[nameof(UnitState.Speed)] = UnitStateTelemetryLimits.MaxSpeedOrHeading,
			[nameof(UnitState.Heading)] = UnitStateTelemetryLimits.MaxSpeedOrHeading
		};

		[Test]
		public void Every_decimal_column_on_UnitState_has_a_limit()
		{
			var declared = typeof(UnitState).GetProperties()
				.Where(p => p.GetCustomAttributes<DecimalPrecisionAttribute>().Any())
				.Select(p => p.Name);

			LimitByProperty.Keys.Should().BeEquivalentTo(declared,
				"a new decimal telemetry column on UnitStates needs a limit in UnitStateTelemetryLimits.FitToColumns");
		}

		[TestCase(nameof(UnitState.Latitude))]
		[TestCase(nameof(UnitState.Longitude))]
		[TestCase(nameof(UnitState.Accuracy))]
		[TestCase(nameof(UnitState.AltitudeAccuracy))]
		[TestCase(nameof(UnitState.Altitude))]
		[TestCase(nameof(UnitState.Speed))]
		[TestCase(nameof(UnitState.Heading))]
		public void Limits_match_the_declared_column_precision(string property)
		{
			var limit = LimitByProperty[property];
			var precision = typeof(UnitState).GetProperty(property).GetCustomAttributes<DecimalPrecisionAttribute>().Single();

			var integerDigits = precision.Precision - precision.Scale;
			var expected = (decimal)System.Math.Pow(10, integerDigits) - 1m / (decimal)System.Math.Pow(10, precision.Scale);

			limit.Should().Be(expected, $"{property} is decimal({precision.Precision},{precision.Scale})");
		}
	}
}
