using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Framework
{
	/// <summary>
	/// An unset non-nullable date (a blank form field binds as DateTime.MinValue) cannot be shifted into a zone on the far side
	/// of UTC: the result would fall before year 1. Every shared time-zone conversion passes those sentinels through instead of
	/// throwing, so a blank date can never take down a page, a save or a worker.
	/// </summary>
	[TestFixture]
	public class DateTimeHelpersZoneShiftTests
	{
		private static readonly string[] Zones = { "Pacific Standard Time", "Eastern Standard Time", "UTC", "W. Europe Standard Time", "Tokyo Standard Time", "New Zealand Standard Time" };

		[Test]
		public void Sentinels_are_the_values_within_a_day_of_the_DateTime_range_ends()
		{
			DateTimeHelpers.IsZoneShiftSentinel(DateTime.MinValue).Should().BeTrue();
			DateTimeHelpers.IsZoneShiftSentinel(default(DateTime).AddHours(12)).Should().BeTrue();
			DateTimeHelpers.IsZoneShiftSentinel(DateTime.MaxValue).Should().BeTrue();
			DateTimeHelpers.IsZoneShiftSentinel(new DateTime(1900, 1, 1)).Should().BeFalse();
			DateTimeHelpers.IsZoneShiftSentinel(new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc)).Should().BeFalse();
		}

		[TestCaseSource(nameof(Zones))]
		public void Unset_dates_pass_through_every_shared_conversion(string zone)
		{
			var department = new Department { TimeZone = zone };

			DateTimeHelpers.ConvertToUtc(DateTime.MinValue, zone).Should().Be(DateTime.MinValue);
			DateTimeHelpers.ConvertToUtc(DateTime.MinValue, zone, lenient: true).Should().Be(DateTime.MinValue);
			DateTimeHelpers.GetLocalDateTime(DateTime.MinValue, zone).Should().Be(DateTime.MinValue);
			DateTime.MinValue.TimeConverter(department).Should().Be(DateTime.MinValue);
			DateTime.MinValue.DepartmentLocalToUtc(department).Should().Be(DateTime.MinValue);

			FluentActions.Invoking(() => DateTimeHelpers.ConvertToUtc(DateTime.MaxValue, zone)).Should().NotThrow();
			FluentActions.Invoking(() => DateTimeHelpers.GetLocalDateTime(DateTime.MaxValue, zone)).Should().NotThrow();
			FluentActions.Invoking(() => DateTime.MaxValue.TimeConverter(department)).Should().NotThrow();
			FluentActions.Invoking(() => DateTime.MaxValue.DepartmentLocalToUtc(department)).Should().NotThrow();
		}

		[Test]
		public void Real_dates_still_convert_in_the_department_zone()
		{
			var department = new Department { TimeZone = "Pacific Standard Time" };
			var utc = new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc);
			var local = new DateTime(2026, 10, 27, 12, 30, 0, DateTimeKind.Unspecified);

			utc.TimeConverter(department).Should().Be(local);
			local.DepartmentLocalToUtc(department).Should().Be(utc);
			DateTimeHelpers.GetLocalDateTime(utc, "Pacific Standard Time").Should().Be(local);
			DateTimeHelpers.ConvertToUtc(local, "Pacific Standard Time").Should().Be(utc);
		}
	}
}
