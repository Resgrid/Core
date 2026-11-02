using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Small map images (thumbnails, settings previews) for the department's style
	/// (maplibre-mapping-routing-navigation-plan.md §6.6): Mapbox Static Images on Mapbox installs, tiles stitched from
	/// the install's raster tile server on self-hosted ones.
	/// </summary>
	public interface IStaticMapService
	{
		/// <param name="zoom">GL zoom (MapLibre/Mapbox scale).</param>
		/// <param name="scale">1 or 2 (high-density screens).</param>
		Task<StaticMapImage> GetStaticMapAsync(int departmentId, string appKey, double latitude, double longitude, double zoom, int width, int height,
			int scale, bool night, bool marker, CancellationToken cancellationToken = default);

		/// <summary>
		/// A preview of a style preset as this install renders it (Mapping Settings): Mapbox Static Images on Mapbox,
		/// TileServer GL static images for a self-hosted style that has them, else stitched raster tiles. No marker.
		/// </summary>
		Task<StaticMapImage> GetStylePreviewAsync(MapStyleTypes style, bool night, double latitude, double longitude, double zoom, int width, int height,
			int scale, CancellationToken cancellationToken = default);

		/// <summary>Signed URL template with the placeholders Omni fills: {lat} {lon} {zoom} {width} {height} {scale} {scheme}.</summary>
		string GetSignedTemplate(int departmentId, string appKey);
	}

	public class StaticMapImage
	{
		public byte[] Bytes { get; set; }

		public string ContentType { get; set; }
	}
}
