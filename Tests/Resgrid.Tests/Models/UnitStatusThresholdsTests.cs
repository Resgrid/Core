using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// Drives the Big Board's "this unit has been sitting here too long" highlighting. The invariant
	/// that matters: an unconfigured department highlights nothing, exactly as before the feature.
	/// </summary>
	[TestFixture]
	public class UnitStatusThresholdsTests
	{
		private static UnitStatusThresholds With(params UnitStatusThreshold[] thresholds) =>
			new UnitStatusThresholds { Thresholds = new List<UnitStatusThreshold>(thresholds) };

		[Test]
		public void An_unconfigured_department_has_no_thresholds()
		{
			var thresholds = new UnitStatusThresholds();

			thresholds.IsEmpty.Should().BeTrue();
			thresholds.Find((int)ActionBaseTypes.Dispatched).Should().BeNull();
		}

		[Test]
		public void Finds_a_threshold_by_base_type()
		{
			// The customer case: dispatched for more than four minutes without reporting departed.
			var thresholds = With(new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = 240, AlertSeconds = 480 });

			var found = thresholds.Find((int)ActionBaseTypes.Dispatched);

			found.Should().NotBeNull();
			found.WarnSeconds.Should().Be(240);
			found.AlertSeconds.Should().Be(480);
		}

		[Test]
		public void Normalize_drops_rows_that_would_never_fire()
		{
			var thresholds = With(
				new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.OnScene, WarnSeconds = 0, AlertSeconds = 0 },
				new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = 240 });

			thresholds.Normalize();

			thresholds.Thresholds.Should().HaveCount(1);
			thresholds.Thresholds[0].BaseType.Should().Be((int)ActionBaseTypes.Dispatched);
		}

		[Test]
		public void Normalize_clamps_negative_values_to_zero()
		{
			var thresholds = With(new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = -60, AlertSeconds = 300 });

			thresholds.Normalize();

			thresholds.Thresholds[0].WarnSeconds.Should().Be(0);
			thresholds.Thresholds[0].AlertSeconds.Should().Be(300);
		}

		[Test]
		public void An_alert_that_is_not_later_than_the_warning_becomes_alert_only()
		{
			// Showing both levels at the same moment makes the escalation meaningless; keeping the
			// alert is the safer of the two readings.
			var thresholds = With(new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = 300, AlertSeconds = 300 });

			thresholds.Normalize();

			thresholds.Thresholds[0].WarnSeconds.Should().Be(0);
			thresholds.Thresholds[0].AlertSeconds.Should().Be(300);
		}

		[Test]
		public void Normalize_keeps_the_last_row_when_a_base_type_is_duplicated()
		{
			var thresholds = With(
				new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = 60 },
				new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = 240 });

			thresholds.Normalize();

			thresholds.Thresholds.Should().HaveCount(1);
			thresholds.Thresholds[0].WarnSeconds.Should().Be(240);
		}

		[Test]
		public void Normalize_of_an_all_empty_set_leaves_it_empty()
		{
			var thresholds = With(new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Responding, WarnSeconds = 0, AlertSeconds = 0 });

			thresholds.Normalize();

			thresholds.IsEmpty.Should().BeTrue();
		}
	}
}
