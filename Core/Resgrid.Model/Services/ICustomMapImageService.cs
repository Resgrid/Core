using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Handles storing, retrieving, and removing background images for custom map floors/layers.
	/// Two storage strategies are supported:
	///   • DatabaseBlob   – raw bytes saved in the Files table (≤ 10 MB uploads)
	///   • TiledPyramid   – 256×256 PNG tile pyramid written to the filesystem (> 10 MB uploads)
	/// </summary>
	public interface ICustomMapImageService
	{
		/// <summary>
		/// Upload threshold in bytes above which tiled storage is used instead of DB blob.
		/// Defaults to 10 MB.
		/// </summary>
		long TiledThresholdBytes { get; }

		/// <summary>
		/// Saves the uploaded image for a floor, choosing the correct storage strategy
		/// based on file size, and updates the supplied <paramref name="floor"/> entity in place
		/// (sets StorageType, ImageUrl / TileBaseUrl, ImageFileId, ImageWidthPx, ImageHeightPx, TileZoomLevels).
		/// </summary>
		/// <param name="floor">The floor entity to update.</param>
		/// <param name="upload">The uploaded file from the HTTP request.</param>
		/// <param name="departmentId">Owning department (used when saving to Files table).</param>
		/// <param name="tileBasePath">
		/// Physical root folder for tile storage, e.g. <c>wwwroot/custommaps</c>.
		/// Ignored when the file is stored as a DB blob.
		/// </param>
		/// <param name="tileBaseUrlTemplate">
		/// URL template for tile requests, e.g. <c>/User/Mapping/GetFloorTile?floorId={floorId}&amp;z={z}&amp;x={x}&amp;y={y}</c>.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task SaveFloorImageAsync(
			CustomMapFloor floor,
			IFormFile upload,
			int departmentId,
			string tileBasePath,
			string tileBaseUrlTemplate,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns the raw bytes of a stored floor image (DatabaseBlob strategy).
		/// Returns <c>null</c> when the floor uses TiledPyramid or has no image.
		/// </summary>
		Task<(byte[] Data, string ContentType)?> GetFloorImageAsync(CustomMapFloor floor);

		/// <summary>
		/// Returns the physical path of a single tile file for a TiledPyramid floor.
		/// Returns <c>null</c> when the tile does not exist.
		/// </summary>
		/// <param name="tileBasePath">Physical root folder for tile storage.</param>
		string GetTilePath(string tileBasePath, string floorId, int z, int x, int y);

		/// <summary>
		/// Deletes all stored image data for a floor (DB row and/or tile files on disk).
		/// </summary>
		Task DeleteFloorImageAsync(
			CustomMapFloor floor,
			string tileBasePath,
			CancellationToken cancellationToken = default);
	}
}

