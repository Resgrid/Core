using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class TimeZoneTests
	{
		[Test]
		public void TestAllTimeZones()
		{
			foreach (var timeZone in TimeZones.Zones)
			{
				var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Key);
				timeZoneInfo.Should().NotBeNull();
			}
		}
	}
}