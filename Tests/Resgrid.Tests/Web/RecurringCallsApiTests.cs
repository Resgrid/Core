using System;
using FluentAssertions;
using NUnit.Framework;
using Api = Resgrid.Web.Services.Controllers.v4.RecurringCallsController;

namespace Resgrid.Tests.Web
{
	/// <summary>The v4 recurring calls API's wire formats: department-local dates, ISO-8601 UTC instants and "lat,lon".</summary>
	[TestFixture]
	public class RecurringCallsApiTests
	{
		[Test]
		public void Dates_are_plain_local_dates()
		{
			Api.ParseDate("2026-11-17").Should().Be(new DateTime(2026, 11, 17));
			Api.ParseDate("2026-11-17").Value.Kind.Should().Be(DateTimeKind.Unspecified);
			Api.ParseDate("17/11/2026").Should().BeNull();
			Api.ParseDate(null).Should().BeNull();
		}

		[TestCase("2026-11-18T16:00:00Z")]
		[TestCase("2026-11-18T08:00:00-08:00")]
		[TestCase("2026-11-18T16:00:00")]
		public void An_occurrence_is_an_instant_in_utc(string value)
		{
			Api.TryParseInstant(value, out var utc).Should().BeTrue();
			utc.Should().Be(new DateTime(2026, 11, 18, 16, 0, 0, DateTimeKind.Utc));
			utc.Kind.Should().Be(DateTimeKind.Utc);
			Api.Iso(utc).Should().Be("2026-11-18T16:00:00Z");
		}

		[TestCase("tomorrow")]
		[TestCase("08:00")]
		[TestCase("16:00:00Z")]
		[TestCase("2026-11-18")]
		[TestCase("")]
		[TestCase(null)]
		public void A_bad_occurrence_is_refused(string value)
		{
			// A time alone would take today's date from the server clock: the request must name the occurrence's date and time.
			Api.TryParseInstant(value, out _).Should().BeFalse();
		}

		[TestCase("39.7392,-104.9903", "39.7392,-104.9903")]
		[TestCase(" 39.7392 , -104.9903 ", "39.7392,-104.9903")]
		[TestCase("91,0", null)]
		[TestCase("39.7", null)]
		public void Coordinates_are_lat_lon_in_range(string value, string expected)
		{
			Api.ParseCoordinates(value).Should().Be(expected);
		}
	}
}
