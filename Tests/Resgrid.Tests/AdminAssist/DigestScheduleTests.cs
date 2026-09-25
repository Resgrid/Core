using System;
using NUnit.Framework;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class DigestScheduleTests
	{
		[TestCase(7, true)] [TestCase(8, false)] [TestCase(19, false)] [TestCase(20, true)]
		public void Overnight_quiet_period_includes_start_and_excludes_end(int hour, bool quiet) =>
			Assert.That(AdminAssistDigestSchedule.IsQuiet(new DateTime(2026, 9, 24, hour, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc, 20, 8), Is.EqualTo(quiet));
		[Test]
		public void Repeated_DST_hour_stays_quiet_and_uses_one_weekly_dedup_key()
		{
			var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
			var first = new DateTime(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc);
			var second = first.AddHours(1);
			Assert.That(AdminAssistDigestSchedule.IsQuiet(first, zone, 20, 8), Is.True);
			Assert.That(AdminAssistDigestSchedule.IsQuiet(second, zone, 20, 8), Is.True);
			Assert.That(AdminAssistDigestSchedule.Week(first, zone), Is.EqualTo("2026-10-26"));
			Assert.That(AdminAssistDigestSchedule.Week(second, zone), Is.EqualTo(AdminAssistDigestSchedule.Week(first, zone)));
		}
		[Test]
		public void Local_week_does_not_roll_over_at_UTC_midnight()
		{
			var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
			Assert.That(AdminAssistDigestSchedule.Week(new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc), zone), Is.EqualTo("2026-09-21"));
		}
	}
}
