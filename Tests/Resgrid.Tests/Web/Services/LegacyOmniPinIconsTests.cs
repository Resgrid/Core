using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Mapping;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// TEMP (2026-11-11): Omni store builds that draw a marker's image at its pixel size get the 32 px icon rendition; legacy
	/// store apps, fixed Omni builds (fullSizeIcons) and clients that send no version keep the 128 px icon.
	/// </summary>
	[TestFixture]
	public class LegacyOmniPinIconsTests
	{
		[TestCase("unit", "8.25")]
		[TestCase("Unit", "8.25")]
		[TestCase("responder", "11.25")]
		[TestCase("ic", "2.25")]
		[TestCase("dispatch", "2.25")]
		[TestCase("bigboard", "4.25")]
		[TestCase("unit", "9")]
		public void Omni_store_builds_get_the_map_size_icon(string client, string version)
		{
			LegacyOmniPinIcons.Applies(client, version, fullSizeIcons: false).Should().BeTrue();
		}

		[TestCase("unit", "7.655", false, TestName = "Legacy Unit")]
		[TestCase("responder", "10.315", false, TestName = "Legacy Responder")]
		[TestCase("ic", "1.188", false, TestName = "Legacy IC")]
		[TestCase("dispatch", "1.232", false, TestName = "Legacy Dispatch")]
		[TestCase("bigboard", "1.137", false, TestName = "Legacy BigBoard")]
		[TestCase("unit", "8.30", true, TestName = "Fixed Omni build")]
		[TestCase("unit", null, false, TestName = "No app version (web, Electron)")]
		[TestCase("unit", "", false, TestName = "Blank app version")]
		[TestCase("unit", "v8", false, TestName = "Unreadable app version")]
		[TestCase(null, "8.25", false, TestName = "No client")]
		[TestCase("web", "8.25", false, TestName = "Web app")]
		[TestCase("mcp", "99.1", false, TestName = "Unknown client")]
		public void Everything_else_keeps_the_full_size_icon(string client, string version, bool fullSizeIcons)
		{
			LegacyOmniPinIcons.Applies(client, version, fullSizeIcons).Should().BeFalse();
		}

		[Test]
		public void Apply_points_uploaded_icons_at_the_32_px_rendition_and_leaves_markers_without_one()
		{
			var plain = new MapMakerInfoData { IconUrl = "https://api.example.test/api/v4/TypeIcons/abc" };
			var badged = new MapMakerInfoData { IconUrl = "https://api.example.test/api/v4/TypeIcons/abc?s=responding&v=1" };
			var none = new MapMakerInfoData { IconUrl = null };

			LegacyOmniPinIcons.Apply(new List<MapMakerInfoData> { plain, badged, none, null });

			plain.IconUrl.Should().Be("https://api.example.test/api/v4/TypeIcons/abc?px=32");
			badged.IconUrl.Should().Be("https://api.example.test/api/v4/TypeIcons/abc?s=responding&v=1&px=32");
			none.IconUrl.Should().BeNull();
			LegacyOmniPinIcons.Apply(null);
		}

		[Test]
		public void AtMapSize_keeps_blank_urls()
		{
			TypeIcons.AtMapSize(null).Should().BeNull();
			TypeIcons.AtMapSize("").Should().Be("");
			TypeIcons.AtMapSize(TypeIcons.ApiIconUrl("https://api.example.test", "k")).Should().EndWith($"/k?{TypeIcons.SizeQuery}={TypeIcons.MapSize}");
		}
	}
}
