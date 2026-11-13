using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Configs;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Fills the Config/GetConfig traffic fields from the department's traffic layer. Mapbox Traffic v1 vector tiles go in
	/// TrafficTileUrl (with TrafficAvailable); a department's own image tiles (e.g. TomTom flow) go only in
	/// TrafficRasterTileUrl, because app builds that predate that field read TrafficTileUrl as vector tiles and would try
	/// to parse the images as vector data. Those builds show no traffic for such a department instead.
	/// </summary>
	public static class ConfigTrafficFields
	{
		public static void Apply(GetConfigResultData data, TrafficLayerSource traffic)
		{
			var url = string.IsNullOrWhiteSpace(traffic?.Url) ? null : traffic.Url;
			var raster = url != null && traffic.Type == TrafficLayerSource.RasterType;

			data.TrafficAvailable = url != null && !raster;
			data.TrafficTileUrl = raster ? null : url;
			data.TrafficRasterTileUrl = raster ? url : null;
		}
	}
}
