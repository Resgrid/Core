using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// POI markers used to be sent with a null ImagePath, and every client then fell through to its
	/// default marker -- the call icon, which is a flame. Customers saw hospitals drawn as structure
	/// fires. These guard the mapping that replaced that behaviour.
	/// </summary>
	[TestFixture]
	public class PoiIconHelperTests
	{
		[TestCase("map-icon-hospital", "hospital")]
		[TestCase("map-icon-fire-station", "station")]
		[TestCase("map-icon-airport", "aircraft")]
		[TestCase("map-icon-bus-station", "bus")]
		[TestCase("map-icon-campground", "camper")]
		[TestCase("map-icon-pharmacy", "firstaid")]
		[TestCase("map-icon-car-repair", "car")]
		[TestCase("map-icon-plumber", "tools")]
		public void ResolveIconName_maps_known_poi_classes(string poiTypeImage, string expected)
		{
			PoiIconHelper.ResolveIconName(poiTypeImage).Should().Be(expected);
		}

		[Test]
		public void ResolveIconName_accepts_a_bare_name_without_the_prefix()
		{
			PoiIconHelper.ResolveIconName("hospital").Should().Be("hospital");
		}

		[Test]
		public void ResolveIconName_is_case_insensitive()
		{
			PoiIconHelper.ResolveIconName("MAP-ICON-HOSPITAL").Should().Be("hospital");
		}

		[TestCase("")]
		[TestCase("   ")]
		[TestCase(null)]
		[TestCase("map-icon-")]
		[TestCase("map-icon-something-we-have-no-asset-for")]
		public void ResolveIconName_falls_back_to_a_neutral_pin(string poiTypeImage)
		{
			PoiIconHelper.ResolveIconName(poiTypeImage).Should().Be(PoiIconHelper.DefaultIconName);
		}

		[Test]
		public void ResolveIconName_never_returns_the_call_icon()
		{
			// The whole point: a POI must never be drawn with the flame used for active calls.
			PoiIconHelper.DefaultIconName.Should().NotBe("call");
			PoiIconHelper.ResolveIconName("map-icon-unknown-thing").Should().NotBe("call");
		}
	}
}
