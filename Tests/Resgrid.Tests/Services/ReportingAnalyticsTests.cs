using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ReportingAnalyticsTests
	{
		[Test]
		public void Summarize_returns_zeros_for_empty_input()
		{
			var summary = ReportingMath.Summarize(new List<double>());

			summary.Count.Should().Be(0);
			summary.Mean.Should().Be(0d);
			summary.P90.Should().Be(0d);
		}

		[Test]
		public void Summarize_computes_interpolated_percentiles()
		{
			var samples = new List<double> { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

			var summary = ReportingMath.Summarize(samples);

			summary.Count.Should().Be(10);
			summary.Min.Should().Be(10d);
			summary.Max.Should().Be(100d);
			summary.Mean.Should().BeApproximately(55d, 0.0001);
			summary.P50.Should().BeApproximately(55d, 0.0001); // rank 4.5 -> 50 + (60-50)*0.5
			summary.P90.Should().BeApproximately(91d, 0.0001); // rank 8.1 -> 90 + (100-90)*0.1
		}

		[Test]
		public void Summarize_handles_single_sample()
		{
			var summary = ReportingMath.Summarize(new List<double> { 42 });

			summary.Count.Should().Be(1);
			summary.P50.Should().Be(42d);
			summary.P90.Should().Be(42d);
			summary.Mean.Should().Be(42d);
		}

		[Test]
		public void UtilizationSeconds_computes_committed_vs_total()
		{
			var baseTime = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
			var states = new List<(DateTime, bool)>
			{
				(baseTime, false),                 // 09:00 available
				(baseTime.AddHours(1), true),      // 10:00 committed
				(baseTime.AddHours(2), false),     // 11:00 available
			};
			var windowEnd = baseTime.AddHours(3);  // 12:00

			var (committed, total) = ReportingMath.UtilizationSeconds(states, windowEnd);

			committed.Should().BeApproximately(3600d, 0.0001);   // 10:00 -> 11:00
			total.Should().BeApproximately(10800d, 0.0001);      // 09:00 -> 12:00
		}

		[Test]
		public void UtilizationSeconds_handles_empty_and_single_state()
		{
			var (c0, t0) = ReportingMath.UtilizationSeconds(new List<(DateTime, bool)>(), DateTime.UtcNow);
			c0.Should().Be(0d);
			t0.Should().Be(0d);

			var start = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
			var (c1, t1) = ReportingMath.UtilizationSeconds(new List<(DateTime, bool)> { (start, true) }, start.AddHours(2));
			c1.Should().BeApproximately(7200d, 0.0001);
			t1.Should().BeApproximately(7200d, 0.0001);
		}

		[Test]
		public void AvailabilityMatrix_maps_personnel_base_types()
		{
			AvailabilityMatrix.ForPersonnelBaseType((int)ActionBaseTypes.Available).Should().Be(AvailabilityClass.Available);
			AvailabilityMatrix.ForPersonnelBaseType((int)ActionBaseTypes.Responding).Should().Be(AvailabilityClass.Committed);
			AvailabilityMatrix.ForPersonnelBaseType((int)ActionBaseTypes.Unavailable).Should().Be(AvailabilityClass.Unavailable);
			AvailabilityMatrix.ForPersonnelBaseType((int)ActionBaseTypes.None).Should().Be(AvailabilityClass.Unknown);
		}

		[Test]
		public void AvailabilityMatrix_maps_builtin_personnel_action_types()
		{
			AvailabilityMatrix.ForBuiltInPersonnelActionType((int)ActionTypes.StandingBy).Should().Be(AvailabilityClass.Available);
			AvailabilityMatrix.ForBuiltInPersonnelActionType((int)ActionTypes.Responding).Should().Be(AvailabilityClass.Committed);
			AvailabilityMatrix.ForBuiltInPersonnelActionType((int)ActionTypes.NotResponding).Should().Be(AvailabilityClass.Unavailable);
		}

		[Test]
		public void AvailabilityMatrix_maps_unit_state_types()
		{
			AvailabilityMatrix.ForUnitStateType((int)UnitStateTypes.Available).Should().Be(AvailabilityClass.Available);
			AvailabilityMatrix.ForUnitStateType((int)UnitStateTypes.Committed).Should().Be(AvailabilityClass.Committed);
			AvailabilityMatrix.ForUnitStateType((int)UnitStateTypes.Delayed).Should().Be(AvailabilityClass.Delayed);
			AvailabilityMatrix.ForUnitStateType((int)UnitStateTypes.OutOfService).Should().Be(AvailabilityClass.Unavailable);
		}

		[Test]
		public void AvailabilityMatrix_returns_unknown_for_unmapped_value()
		{
			AvailabilityMatrix.ForUnitStateType(9999).Should().Be(AvailabilityClass.Unknown);
			AvailabilityMatrix.ForBuiltInPersonnelActionType(9999).Should().Be(AvailabilityClass.Unknown);
		}
	}
}
