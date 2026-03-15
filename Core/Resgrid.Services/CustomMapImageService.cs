using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Resgrid.Services
{
	/// <summary>
	/// Stores background images for custom map floors/layers.
	///
	/// Strategy selection (based on upload size):
	///   ≤ TiledThresholdBytes → DatabaseBlob  : raw bytes stored in the Files table.
	///   > TiledThresholdBytes → TiledPyramid  : 256×256 PNG tiles written to disk under
	///                                            {tileBasePath}/{floorId}/{z}/{x}/{y}.png
	///
	/// The tile pyramid is compatible with Leaflet's L.tileLayer when CRS.Simple is used
	/// or as an image overlay divided into tiles when geographic bounds are available.
	/// </summary>
	public sealed class CustomMapImageService : ICustomMapImageService
	{
		private const int TileSize = 256;

		private readonly IFileService _fileService;

		public long TiledThresholdBytes { get; } = 10 * 1024 * 1024; // 10 MB

		public CustomMapImageService(IFileService fileService)
		{
			_fileService = fileService;
		}

		// ── SaveFloorImageAsync ──────────────────────────────────────────────────

		public async Task SaveFloorImageAsync(
			CustomMapFloor floor,
			IFormFile upload,
			int departmentId,
			string tileBasePath,
			string tileBaseUrlTemplate,
			CancellationToken cancellationToken = default)
		{
			if (floor == null) throw new ArgumentNullException(nameof(floor));
			if (upload == null || upload.Length == 0) throw new ArgumentException("Upload is empty.", nameof(upload));

			using var stream = upload.OpenReadStream();
			using var image = await Image.LoadAsync(stream, cancellationToken);

			floor.ImageWidthPx = image.Width;
			floor.ImageHeightPx = image.Height;

			if (upload.Length <= TiledThresholdBytes)
			{
				await SaveAsDatabaseBlobAsync(floor, upload, image, departmentId, cancellationToken);
			}
			else
			{
				await SaveAsTilePyramidAsync(floor, image, tileBasePath, tileBaseUrlTemplate, cancellationToken);
			}
		}

		// ── GetFloorImageAsync ───────────────────────────────────────────────────

		public async Task<(byte[] Data, string ContentType)?> GetFloorImageAsync(CustomMapFloor floor)
		{
			if (floor == null) return null;

			var storageType = (CustomMapFloorStorageType)floor.StorageType;

			if (storageType == CustomMapFloorStorageType.DatabaseBlob && floor.ImageFileId.HasValue)
			{
				var file = await _fileService.GetFileByIdAsync(floor.ImageFileId.Value);
				if (file?.Data == null) return null;
				return (file.Data, file.FileType ?? "image/png");
			}

			return null; // TiledPyramid tiles are served individually via GetTilePath
		}

		// ── GetTilePath ──────────────────────────────────────────────────────────

		public string GetTilePath(string tileBasePath, string floorId, int z, int x, int y)
		{
			var path = Path.Combine(tileBasePath, floorId, z.ToString(), x.ToString(), $"{y}.png");
			return System.IO.File.Exists(path) ? path : null;
		}

		// ── DeleteFloorImageAsync ────────────────────────────────────────────────

		public async Task DeleteFloorImageAsync(
			CustomMapFloor floor,
			string tileBasePath,
			CancellationToken cancellationToken = default)
		{
			if (floor == null) return;

			var storageType = (CustomMapFloorStorageType)floor.StorageType;

			if (storageType == CustomMapFloorStorageType.DatabaseBlob && floor.ImageFileId.HasValue)
			{
				var file = await _fileService.GetFileByIdAsync(floor.ImageFileId.Value);
				if (file != null)
					await _fileService.DeleteFileAsync(file, cancellationToken);
			}
			else if (storageType == CustomMapFloorStorageType.TiledPyramid && !string.IsNullOrWhiteSpace(tileBasePath))
			{
				var floorTileDir = Path.Combine(tileBasePath, floor.CustomMapFloorId);
				if (Directory.Exists(floorTileDir))
				{
					try { Directory.Delete(floorTileDir, recursive: true); }
					catch (Exception ex) { Logging.LogException(ex); }
				}
			}

			floor.StorageType = (int)CustomMapFloorStorageType.None;
			floor.ImageFileId = null;
			floor.ImageUrl = null;
			floor.TileBaseUrl = null;
			floor.ImageWidthPx = null;
			floor.ImageHeightPx = null;
			floor.TileZoomLevels = null;
		}

		// ── Private helpers ──────────────────────────────────────────────────────

		private async Task SaveAsDatabaseBlobAsync(
			CustomMapFloor floor,
			IFormFile upload,
			Image image,
			int departmentId,
			CancellationToken cancellationToken)
		{
			byte[] data;
			using (var ms = new MemoryStream())
			{
				using var uploadStream = upload.OpenReadStream();
				await uploadStream.CopyToAsync(ms, cancellationToken);
				data = ms.ToArray();
			}

			// Delete any existing blob before saving the new one
			if (floor.ImageFileId.HasValue)
			{
				var existing = await _fileService.GetFileByIdAsync(floor.ImageFileId.Value);
				if (existing != null)
					await _fileService.DeleteFileAsync(existing, cancellationToken);
			}

			var fileRecord = new Resgrid.Model.File
			{
				DepartmentId = departmentId,
				FileName = upload.FileName,
				FileType = upload.ContentType,
				Data = data,
				Timestamp = DateTime.UtcNow
			};

			var saved = await _fileService.SaveFileAsync(fileRecord, cancellationToken);

			floor.StorageType = (int)CustomMapFloorStorageType.DatabaseBlob;
			floor.ImageFileId = saved.FileId;
			floor.TileBaseUrl = null;
			floor.TileZoomLevels = null;
			// ImageUrl will be set by the controller once it knows the route URL
		}

		private async Task SaveAsTilePyramidAsync(
			CustomMapFloor floor,
			Image sourceImage,
			string tileBasePath,
			string tileBaseUrlTemplate,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(tileBasePath))
				throw new InvalidOperationException("tileBasePath must be provided for tiled storage.");

			var floorId = floor.CustomMapFloorId;
			var floorTileDir = Path.Combine(tileBasePath, floorId);

			// Remove old tiles if present
			if (Directory.Exists(floorTileDir))
				Directory.Delete(floorTileDir, recursive: true);

			// Determine how many zoom levels are needed so the image fills
			// at least one tile at zoom 0 and can be shown at native resolution
			// at the highest zoom level.
			int maxDim = Math.Max(sourceImage.Width, sourceImage.Height);
			int zoomLevels = CalculateZoomLevels(maxDim);

			var encoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed };

			for (int z = 0; z < zoomLevels; z++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				// At zoom level z, the entire image is scaled to fit a grid of 2^z × 2^z tiles
				int gridSize = 1 << z; // 2^z
				int scaledWidth = gridSize * TileSize;
				int scaledHeight = gridSize * TileSize;

				using var scaled = sourceImage.Clone(ctx =>
					ctx.Resize(scaledWidth, scaledHeight));

				for (int x = 0; x < gridSize; x++)
				{
					for (int y = 0; y < gridSize; y++)
					{
						cancellationToken.ThrowIfCancellationRequested();

						var tileDir = Path.Combine(floorTileDir, z.ToString(), x.ToString());
						Directory.CreateDirectory(tileDir);

						var tilePath = Path.Combine(tileDir, $"{y}.png");

						using var tile = scaled.Clone(ctx => ctx.Crop(new Rectangle(
							x * TileSize, y * TileSize, TileSize, TileSize)));

						await using var fs = new FileStream(tilePath, FileMode.Create, FileAccess.Write, FileShare.None);
						await tile.SaveAsync(fs, encoder, cancellationToken);
					}
				}
			}

			floor.StorageType = (int)CustomMapFloorStorageType.TiledPyramid;
			floor.ImageFileId = null;
			floor.ImageUrl = null;
			floor.TileZoomLevels = zoomLevels;
			floor.TileBaseUrl = tileBaseUrlTemplate.Replace("{floorId}", floorId);
		}

		/// <summary>
		/// Calculates the minimum number of zoom levels required so that the image
		/// fits entirely within the tile pyramid at zoom 0, up to a sensible maximum.
		/// </summary>
		private static int CalculateZoomLevels(int maxDimension)
		{
			// Each zoom level doubles resolution. We want max zoom where
			// native pixel size ≈ tile size * 2^z. Cap at 6 levels (64×64 tiles
			// at highest zoom = 16 384 px effective — ample for any facility/district map).
			int levels = 1;
			while ((TileSize << levels) < maxDimension && levels < 6)
				levels++;
			return levels + 1; // include the base level
		}
	}
}





