using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class StatusTimestampHelperTests
	{
		private static readonly DateTime Now = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc);

		[Test]
		public void an_offline_status_keeps_the_time_it_was_set()
		{
			var tapped = Now.AddMinutes(-40);

			StatusTimestampHelper.ResolveStatusTimeUtc(tapped, null, Now).Should().Be(tapped);
		}

		[Test]
		public void an_unzoned_utc_field_is_read_as_utc()
		{
			var tapped = DateTime.SpecifyKind(Now.AddMinutes(-10), DateTimeKind.Unspecified);

			var result = StatusTimestampHelper.ResolveStatusTimeUtc(tapped, null, Now);

			result.Should().Be(Now.AddMinutes(-10));
			result.Kind.Should().Be(DateTimeKind.Utc);
		}

		[Test]
		public void a_zoned_device_timestamp_is_used_when_there_is_no_utc_field()
		{
			var tapped = Now.AddMinutes(-5);

			StatusTimestampHelper.ResolveStatusTimeUtc(null, tapped, Now).Should().Be(tapped);
		}

		[Test]
		public void an_unzoned_device_timestamp_is_ignored()
		{
			var local = DateTime.SpecifyKind(Now.AddHours(-7), DateTimeKind.Unspecified);

			StatusTimestampHelper.ResolveStatusTimeUtc(null, local, Now).Should().Be(Now);
		}

		[Test]
		public void missing_or_implausible_times_fall_back_to_now()
		{
			StatusTimestampHelper.ResolveStatusTimeUtc(null, null, Now).Should().Be(Now);
			StatusTimestampHelper.ResolveStatusTimeUtc(DateTime.MinValue, null, Now).Should().Be(Now);
			StatusTimestampHelper.ResolveStatusTimeUtc(Now.AddDays(-8), null, Now).Should().Be(Now);
			StatusTimestampHelper.ResolveStatusTimeUtc(Now.AddHours(1), null, Now).Should().Be(Now);
		}

		[Test]
		public void small_clock_skew_ahead_is_clamped_to_now()
		{
			StatusTimestampHelper.ResolveStatusTimeUtc(Now.AddMinutes(2), null, Now).Should().Be(Now);
		}
	}
}
