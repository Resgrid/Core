using System;
using System.Globalization;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
    [TestFixture]
    public class DepartmentTimeTests
    {
        [TestCase("Pacific Standard Time", "2026-01-15T18:30:00Z", "2026-01-15T10:30:00.000")]
        [TestCase("Pacific Standard Time", "2026-07-15T18:30:00Z", "2026-07-15T11:30:00.000")]
        [TestCase("America/New_York", "2026-07-15T02:30:00Z", "2026-07-14T22:30:00.000")]
        [TestCase("India Standard Time", "2026-07-15T23:30:00Z", "2026-07-16T05:00:00.000")]
        [TestCase(null, "2026-07-15T18:30:00Z", "2026-07-15T11:30:00.000")]
        public void Stored_UTC_instants_and_local_forms_round_trip_in_the_department_zone(string zone, string stored, string local)
        {
            var time = new DepartmentTime(new Department { TimeZone = zone });
            var utc = DateTime.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            time.Input(utc).Should().Be(local);
            // SQL timestamps commonly arrive with Kind.Unspecified.
            time.Input(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)).Should().Be(local);
            time.Parse(local).Should().Be(utc);
            time.Parse(local).Value.Kind.Should().Be(DateTimeKind.Utc);
            time.Parse(stored).Should().Be(utc);
        }

        [Test]
        public void Display_respects_department_clock_preference_without_a_UTC_suffix()
        {
            var department = new Department { TimeZone = "Pacific Standard Time", Use24HourTime = false };
            var utc = new DateTime(2026, 7, 15, 20, 5, 6, DateTimeKind.Utc);
            new DepartmentTime(department).Format(utc).Should().Be("07/15/2026 1:05:06 PM");
            department.Use24HourTime = true;
            new DepartmentTime(department).Format(utc).Should().Be("07/15/2026 13:05:06");
        }

        [Test]
        public void Explicit_offset_and_hidden_UTC_values_are_not_converted_twice()
        {
            var time = new DepartmentTime(new Department { TimeZone = "Pacific Standard Time" });
            var expected = new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc).AddTicks(1234567);
            time.Parse("2026-11-01T01:30:00.1234567-08:00").Should().Be(expected);
            time.Parse(expected.ToString("O")).Should().Be(expected);
            time.ToUtc(expected).Should().Be(expected);
        }

        [TestCase("2026-03-08T02:30:00", "2026-03-08T10:30:00Z")]
        [TestCase("2026-11-01T01:30:00", "2026-11-01T08:30:00Z")]
        public void DST_gap_and_overlap_follow_existing_lenient_department_conversion(string local, string expected)
        {
            var time = new DepartmentTime(new Department { TimeZone = "Pacific Standard Time" });
            time.Parse(local).Should().Be(DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        [TestCase(3, 8, 23)]
        [TestCase(11, 1, 25)]
        public void Local_day_filters_cover_the_whole_DST_day(int month, int day, int hours)
        {
            var time = new DepartmentTime(new Department { TimeZone = "Pacific Standard Time" });
            var start = new DateTime(2026, month, day);
            (time.ToUtc(start.AddDays(1)) - time.ToUtc(start)).TotalHours.Should().Be(hours);
        }

        [Test]
        public void Readiness_preview_formats_instants_without_mutating_the_UTC_manifest()
        {
            var time = new DepartmentTime(new Department { TimeZone = "Pacific Standard Time", Use24HourTime = true });
            var at = new DateTime(2026, 7, 15, 2, 0, 0, DateTimeKind.Utc);
            var packet = new ReadinessEvidenceManifestV1 { CoverageStartUtc = at, CoverageEndUtc = at.AddHours(1) };
            var html = ChecklistReportDocuments.Packet(packet, time.Format);
            html.Should().Contain("07/14/2026 19:00:00").And.NotContain("2026-07-15 02:00:00Z");
            packet.CoverageStartUtc.Should().Be(at);
            packet.CoverageStartUtc.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void Definition_datetime_fields_use_department_offset_and_leave_calendar_dates_and_posted_text_intact()
        {
            var time = new DepartmentTime(new Department { TimeZone = "Pacific Standard Time" });
            var schema = new RecordDefinitionSchema
            {
                Sections = new() { new() { Key = "event", Fields = new()
                {
                    new() { Key = "at", Type = RmsFieldType.DateTime },
                    new() { Key = "birthday", Type = RmsFieldType.Date }
                } } }
            };
            var input = new RecordValueInput { FieldKey = "at", Value = "2026-07-15T10:00:00", OffsetMinutes = 120 };
            var converted = time.RecordInput(input, schema);
            converted.Value.Should().Be("2026-07-15T17:00:00.0000000Z");
            converted.OffsetMinutes.Should().Be(-420, "the browser timezone must not change department-local input");
            input.Value.Should().Be("2026-07-15T10:00:00", "validation redisplays the submitted clock value");
            var birthday = new RecordValueInput { FieldKey = "birthday", Value = "1990-07-15" };
            time.RecordInput(birthday, schema).Value.Should().Be("1990-07-15");
        }

        [TestCase("America/Los_Angeles")]
        [TestCase("Pacific Standard Time")]
        public void Deployment_time_entries_accept_department_zone_and_preserve_the_entered_calendar_day(string zone)
        {
            var parse = typeof(Resgrid.Web.Areas.User.Controllers.DeploymentsController).GetMethod("TryParseLocal",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var args = new object[] { new DateTime(2026, 7, 14), "2026-07-15T23:30:00.125", zone, default(DateTime) };
            ((bool)parse.Invoke(null, args)).Should().BeTrue();
            ((DateTime)args[3]).Should().Be(new DateTime(2026, 7, 16, 6, 30, 0, 125, DateTimeKind.Utc));
        }
    }
}
