using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Configs;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Config/GetConfig traffic fields: vector tiles in TrafficTileUrl, a department's own image tiles only in
	/// TrafficRasterTileUrl, so app builds that only know the vector overlay never load images as vector tiles.
	/// </summary>
	[TestFixture]
	public class ConfigTrafficFieldsTests
	{
		private const string VectorUrl = "https://api.mapbox.com/v4/mapbox.mapbox-traffic-v1/{z}/{x}/{y}.vector.pbf?access_token=pk.test";
		private const string RasterUrl = "https://api.tomtom.com/traffic/map/4/tile/flow/relative0/{z}/{x}/{y}.png?key=test";

		[Test]
		public void Vector_traffic_fills_the_fields_older_builds_read()
		{
			var data = new GetConfigResultData();

			ConfigTrafficFields.Apply(data, new TrafficLayerSource { Url = VectorUrl, Type = TrafficLayerSource.VectorType });

			data.TrafficAvailable.Should().BeTrue();
			data.TrafficTileUrl.Should().Be(VectorUrl);
			data.TrafficRasterTileUrl.Should().BeNull();
		}

		[Test]
		public void Department_image_tiles_go_only_in_the_raster_field()
		{
			var data = new GetConfigResultData();

			ConfigTrafficFields.Apply(data, new TrafficLayerSource { Url = RasterUrl, Type = TrafficLayerSource.RasterType, IsDepartment = true });

			data.TrafficAvailable.Should().BeFalse();
			data.TrafficTileUrl.Should().BeNull();
			data.TrafficRasterTileUrl.Should().Be(RasterUrl);
		}

		[TestCase(null)]
		[TestCase("")]
		public void No_traffic_leaves_every_field_empty(string url)
		{
			var data = new GetConfigResultData();

			ConfigTrafficFields.Apply(data, url == null ? null : new TrafficLayerSource { Url = url, Type = TrafficLayerSource.RasterType });

			data.TrafficAvailable.Should().BeFalse();
			data.TrafficTileUrl.Should().BeNull();
			data.TrafficRasterTileUrl.Should().BeNull();
		}
	}
}
