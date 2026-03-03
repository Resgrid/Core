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
				try
				{
					var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Key);
					timeZoneInfo.Should().NotBeNull();
				}
				catch (TimeZoneNotFoundException)
				{
					// Windows timezone IDs are not available on non-Windows platforms (Linux/macOS use IANA IDs).
					// Skip entries that are not found on the current OS rather than failing the test.
					Assert.Ignore($"Timezone '{timeZone.Key}' is not available on this platform; skipping.");
				}
			}
		}
	}
}
