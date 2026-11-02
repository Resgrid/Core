using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Resgrid.Services
{
	/// <summary>
	/// Web Mercator XYZ tiles for georeferenced custom map layers (maplibre-mapping-routing-navigation-plan.md §6.7, D21).
	/// The stored custom map tiles are a pixel pyramid (zoom = image scale, x/y = image tile index), which no geographic
	/// renderer can place. A georeferenced layer is drawn by Leaflet as an image stretched between its corner bounds in
	/// Mercator space, so a Mercator tile is the matching rectangle of that image, cropped and scaled — rendered on
	/// demand here from the stored image, or from a reconstruction of the pyramid for large layers uploaded before the
	/// original image was kept. Pixel-only layers (no bounds) have no Mercator tiles.
	/// </summary>
	public class CustomMapMercatorTileRenderer : ICustomMapMercatorTileRenderer
	{
		private const int TileSize = 256;
		private const int MaxReconstructedSide = 4096;

		// Decoded sources are expensive (megabytes each); keep a handful of recently used layers in memory.
		private static readonly MemoryCache Sources = new MemoryCache(new MemoryCacheOptions { SizeLimit = 6 });

		private readonly ICustomMapService _customMapService;
		private readonly ICustomMapTilesRepository _customMapTilesRepository;

		public CustomMapMercatorTileRenderer(ICustomMapService customMapService, ICustomMapTilesRepository customMapTilesRepository)
		{
			_customMapService = customMapService;
			_customMapTilesRepository = customMapTilesRepository;
		}

		public async Task<MercatorLayerBounds> GetBoundsAsync(IndoorMapFloor layer)
		{
			if (layer == null)
				return null;

			if (layer.BoundsNELat.HasValue && layer.BoundsNELon.HasValue && layer.BoundsSWLat.HasValue && layer.BoundsSWLon.HasValue)
				return Create((double)layer.BoundsNELat.Value, (double)layer.BoundsNELon.Value, (double)layer.BoundsSWLat.Value, (double)layer.BoundsSWLon.Value);

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			return map == null ? null : Create((double)map.BoundsNELat, (double)map.BoundsNELon, (double)map.BoundsSWLat, (double)map.BoundsSWLon);
		}

		public async Task<byte[]> RenderTileAsync(IndoorMapFloor layer, int z, int x, int y)
		{
			if (layer == null || z < 0 || z > 24)
				return null;

			var bounds = await GetBoundsAsync(layer);
			if (bounds == null)
				return null;

			var tiles = Math.Pow(2, z);
			var tileLeft = x / tiles;
			var tileRight = (x + 1) / tiles;
			var tileTop = y / tiles;
			var tileBottom = (y + 1) / tiles;

			var left = Math.Max(tileLeft, bounds.West);
			var right = Math.Min(tileRight, bounds.East);
			var top = Math.Max(tileTop, bounds.North);
			var bottom = Math.Min(tileBottom, bounds.South);

			if (right <= left || bottom <= top)
				return null;

			var source = await GetSourceAsync(layer);
			if (source == null)
				return null;

			// Source rectangle (image pixels) and destination rectangle (tile pixels) of the overlap.
			var sx = (left - bounds.West) / (bounds.East - bounds.West) * source.Width;
			var sy = (top - bounds.North) / (bounds.South - bounds.North) * source.Height;
			var sw = (right - left) / (bounds.East - bounds.West) * source.Width;
			var sh = (bottom - top) / (bounds.South - bounds.North) * source.Height;

			var dx = (int)Math.Round((left - tileLeft) / (tileRight - tileLeft) * TileSize);
			var dy = (int)Math.Round((top - tileTop) / (tileBottom - tileTop) * TileSize);
			var dw = Math.Max(1, (int)Math.Round((right - left) / (tileRight - tileLeft) * TileSize));
			var dh = Math.Max(1, (int)Math.Round((bottom - top) / (tileBottom - tileTop) * TileSize));

			var crop = new Rectangle(
				Math.Clamp((int)Math.Floor(sx), 0, source.Width - 1),
				Math.Clamp((int)Math.Floor(sy), 0, source.Height - 1),
				Math.Max(1, (int)Math.Ceiling(sw)),
				Math.Max(1, (int)Math.Ceiling(sh)));
			crop.Width = Math.Min(crop.Width, source.Width - crop.X);
			crop.Height = Math.Min(crop.Height, source.Height - crop.Y);

			using var tile = new Image<Rgba32>(TileSize, TileSize, new Rgba32(0, 0, 0, 0));
			using (var part = source.Clone(context => context.Crop(crop).Resize(dw, dh)))
			{
				tile.Mutate(context => context.DrawImage(part, new Point(dx, dy), 1f));
			}

			using var output = new MemoryStream();
			await tile.SaveAsPngAsync(output);
			return output.ToArray();
		}

		private async Task<Image<Rgba32>> GetSourceAsync(IndoorMapFloor layer)
		{
			var key = $"{layer.IndoorMapFloorId}|{layer.SourceFileSize}|{layer.TileMaxZoom}|{layer.ImageData?.Length ?? 0}";
			if (Sources.TryGetValue(key, out Image<Rgba32> cached))
				return cached;

			Image<Rgba32> source = null;

			if (layer.ImageData != null && layer.ImageData.Length > 0)
			{
				source = Image.Load<Rgba32>(layer.ImageData);
			}
			else if (layer.IsTiled && layer.TileMaxZoom.HasValue)
			{
				source = await ReconstructFromPyramidAsync(layer.IndoorMapFloorId, layer.TileMaxZoom.Value);
			}

			if (source != null)
			{
				Sources.Set(key, source, new MemoryCacheEntryOptions
				{
					Size = 1,
					SlidingExpiration = TimeSpan.FromMinutes(30),
					PostEvictionCallbacks = { new PostEvictionCallbackRegistration { EvictionCallback = (_, value, _, _) => (value as IDisposable)?.Dispose() } }
				});
			}

			return source;
		}

		/// <summary>
		/// Rebuilds the layer image from its pixel pyramid at the largest level no wider than MaxReconstructedSide. Edge
		/// tiles were padded (centered) to 256 px, so their content is located by its non-transparent bounds.
		/// </summary>
		private async Task<Image<Rgba32>> ReconstructFromPyramidAsync(string layerId, int maxZoom)
		{
			var all = (await _customMapTilesRepository.GetTilesForLayerAsync(layerId))?.ToList() ?? new List<CustomMapTile>();
			if (all.Count == 0)
				return null;

			var level = maxZoom;
			while (level > 0)
			{
				var columns = all.Where(t => t.ZoomLevel == level).Select(t => t.TileX).DefaultIfEmpty(0).Max() + 1;
				if (columns * TileSize <= MaxReconstructedSide)
					break;
				level--;
			}

			var tiles = all.Where(t => t.ZoomLevel == level && t.TileData != null).ToList();
			if (tiles.Count == 0)
				return null;

			var lastColumn = tiles.Max(t => t.TileX);
			var lastRow = tiles.Max(t => t.TileY);
			var decoded = new Dictionary<(int, int), Image<Rgba32>>();

			try
			{
				foreach (var tile in tiles)
					decoded[(tile.TileX, tile.TileY)] = Image.Load<Rgba32>(tile.TileData);

				var lastWidth = decoded.TryGetValue((lastColumn, 0), out var corner) ? ContentBounds(corner).Width : TileSize;
				var lastHeight = decoded.TryGetValue((0, lastRow), out var bottom) ? ContentBounds(bottom).Height : TileSize;
				var width = lastColumn * TileSize + lastWidth;
				var height = lastRow * TileSize + lastHeight;

				var image = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 0));
				foreach (var ((column, row), tileImage) in decoded)
				{
					var isEdge = column == lastColumn || row == lastRow;
					var content = isEdge ? ContentBounds(tileImage) : new Rectangle(0, 0, TileSize, TileSize);
					if (content.Width <= 0 || content.Height <= 0)
						continue;

					using var piece = tileImage.Clone(context => context.Crop(content));
					image.Mutate(context => context.DrawImage(piece, new Point(column * TileSize, row * TileSize), 1f));
				}

				return image;
			}
			finally
			{
				foreach (var tileImage in decoded.Values)
					tileImage.Dispose();
			}
		}

		private static Rectangle ContentBounds(Image<Rgba32> image)
		{
			int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;

			image.ProcessPixelRows(rows =>
			{
				for (var y = 0; y < rows.Height; y++)
				{
					var row = rows.GetRowSpan(y);
					for (var x = 0; x < row.Length; x++)
					{
						if (row[x].A == 0)
							continue;

						if (x < minX) minX = x;
						if (x > maxX) maxX = x;
						if (y < minY) minY = y;
						if (y > maxY) maxY = y;
					}
				}
			});

			return maxX < 0 ? new Rectangle(0, 0, image.Width, image.Height) : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
		}

		private static MercatorLayerBounds Create(double northLat, double eastLon, double southLat, double westLon)
		{
			if (northLat <= southLat || eastLon <= westLon || Math.Abs(northLat) > 85.05 || Math.Abs(southLat) > 85.05)
				return null;

			return new MercatorLayerBounds
			{
				North = MercatorY(northLat),
				South = MercatorY(southLat),
				West = (westLon + 180.0) / 360.0,
				East = (eastLon + 180.0) / 360.0
			};
		}

		/// <summary>Latitude to normalized Web Mercator Y (0 north edge, 1 south edge).</summary>
		private static double MercatorY(double latitude)
		{
			var sin = Math.Sin(latitude * Math.PI / 180.0);
			return 0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI);
		}
	}
}
