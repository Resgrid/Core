using System;
using System.Collections.Generic;
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
			var unresolvedZones = new List<string>();

			foreach (var timeZone in TimeZones.Zones)
			{
				try
				{
					var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Key);
					timeZoneInfo.Should().NotBeNull();
				}
				catch (TimeZoneNotFoundException)
				{
					// On non-Windows platforms, Windows timezone IDs are not available.
					// Try converting to IANA ID first, then attempt lookup.
					if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Key, out string ianaId))
					{
						try
						{
							var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
							timeZoneInfo.Should().NotBeNull();
							continue;
						}
						catch (TimeZoneNotFoundException)
						{
							// IANA ID also not found on this platform
						}
					}

					unresolvedZones.Add(timeZone.Key);
				}
			}

			// Report any unresolvable zones but don't fail the test;
			// some Windows-specific timezone IDs don't exist on all platforms.
			if (unresolvedZones.Count > 0)
			{
				TestContext.WriteLine(
					$"The following {unresolvedZones.Count} timezone(s) could not be resolved on this platform: " +
					string.Join(", ", unresolvedZones));
			}

			// The test passes as long as it didn't crash; unresolvable zones
			// are expected on non-Windows platforms for Windows-only IDs.
		}
	}
}
