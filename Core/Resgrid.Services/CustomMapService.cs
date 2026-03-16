using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using SharpKml.Engine;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace Resgrid.Services
{
	public class CustomMapService : ICustomMapService
	{
		private const int TileSize = 256;
		private const int TilingThreshold = 2048;

		private readonly IIndoorMapsRepository _indoorMapsRepository;
		private readonly IIndoorMapFloorsRepository _indoorMapFloorsRepository;
		private readonly IIndoorMapZonesRepository _indoorMapZonesRepository;
		private readonly ICustomMapTilesRepository _customMapTilesRepository;
		private readonly ICustomMapImportsRepository _customMapImportsRepository;

		public CustomMapService(
			IIndoorMapsRepository indoorMapsRepository,
			IIndoorMapFloorsRepository indoorMapFloorsRepository,
			IIndoorMapZonesRepository indoorMapZonesRepository,
			ICustomMapTilesRepository customMapTilesRepository,
			ICustomMapImportsRepository customMapImportsRepository)
		{
			_indoorMapsRepository = indoorMapsRepository;
			_indoorMapFloorsRepository = indoorMapFloorsRepository;
			_indoorMapZonesRepository = indoorMapZonesRepository;
			_customMapTilesRepository = customMapTilesRepository;
			_customMapImportsRepository = customMapImportsRepository;
		}

		#region Maps CRUD

		public async Task<IndoorMap> SaveCustomMapAsync(IndoorMap map, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _indoorMapsRepository.SaveOrUpdateAsync(map, cancellationToken);
		}

		public async Task<IndoorMap> GetCustomMapByIdAsync(string mapId)
		{
			return await _indoorMapsRepository.GetByIdAsync(mapId);
		}

		public async Task<List<IndoorMap>> GetCustomMapsForDepartmentAsync(int departmentId, CustomMapType? filterType = null)
		{
			var maps = await _indoorMapsRepository.GetIndoorMapsByDepartmentIdAsync(departmentId);

			if (maps == null)
				return new List<IndoorMap>();

			var result = maps.Where(x => !x.IsDeleted);

			if (filterType.HasValue)
				result = result.Where(x => x.MapType == (int)filterType.Value);

			return result.ToList();
		}

		public async Task<bool> DeleteCustomMapAsync(string mapId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var map = await _indoorMapsRepository.GetByIdAsync(mapId);

			if (map == null)
				return false;

			map.IsDeleted = true;
			await _indoorMapsRepository.SaveOrUpdateAsync(map, cancellationToken);

			return true;
		}

		#endregion Maps CRUD

		#region Layers CRUD

		public async Task<IndoorMapFloor> SaveLayerAsync(IndoorMapFloor layer, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _indoorMapFloorsRepository.SaveOrUpdateAsync(layer, cancellationToken);
		}

		public async Task<IndoorMapFloor> GetLayerByIdAsync(string layerId)
		{
			return await _indoorMapFloorsRepository.GetByIdAsync(layerId);
		}

		public async Task<List<IndoorMapFloor>> GetLayersForMapAsync(string mapId)
		{
			var layers = await _indoorMapFloorsRepository.GetFloorsByIndoorMapIdAsync(mapId);

			if (layers != null)
				return layers.Where(x => !x.IsDeleted).OrderBy(x => x.FloorOrder).ToList();

			return new List<IndoorMapFloor>();
		}

		public async Task<bool> DeleteLayerAsync(string layerId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var layer = await _indoorMapFloorsRepository.GetByIdAsync(layerId);

			if (layer == null)
				return false;

			layer.IsDeleted = true;
			await _indoorMapFloorsRepository.SaveOrUpdateAsync(layer, cancellationToken);

			return true;
		}

		#endregion Layers CRUD

		#region Regions CRUD

		public async Task<IndoorMapZone> SaveRegionAsync(IndoorMapZone region, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _indoorMapZonesRepository.SaveOrUpdateAsync(region, cancellationToken);
		}

		public async Task<IndoorMapZone> GetRegionByIdAsync(string regionId)
		{
			return await _indoorMapZonesRepository.GetByIdAsync(regionId);
		}

		public async Task<List<IndoorMapZone>> GetRegionsForLayerAsync(string layerId)
		{
			var regions = await _indoorMapZonesRepository.GetZonesByFloorIdAsync(layerId);

			if (regions != null)
				return regions.Where(x => !x.IsDeleted).ToList();

			return new List<IndoorMapZone>();
		}

		public async Task<bool> DeleteRegionAsync(string regionId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var region = await _indoorMapZonesRepository.GetByIdAsync(regionId);

			if (region == null)
				return false;

			region.IsDeleted = true;
			await _indoorMapZonesRepository.SaveOrUpdateAsync(region, cancellationToken);

			return true;
		}

		#endregion Regions CRUD

		#region Dispatch Integration

		public async Task<List<IndoorMapZone>> SearchRegionsAsync(int departmentId, string searchTerm)
		{
			var zones = await _indoorMapZonesRepository.SearchZonesAsync(departmentId, searchTerm);

			if (zones != null)
				return zones.ToList();

			return new List<IndoorMapZone>();
		}

		public async Task<string> GetRegionDisplayNameAsync(string regionId)
		{
			var region = await _indoorMapZonesRepository.GetByIdAsync(regionId);

			if (region == null)
				return null;

			var layer = await _indoorMapFloorsRepository.GetByIdAsync(region.IndoorMapFloorId);

			if (layer == null)
				return region.Name;

			var map = await _indoorMapsRepository.GetByIdAsync(layer.IndoorMapId);

			if (map == null)
				return $"{layer.Name} > {region.Name}";

			return $"{map.Name} > {layer.Name} > {region.Name}";
		}

		#endregion Dispatch Integration

		#region Tile Processing

		public async Task ProcessAndStoreTilesAsync(string layerId, byte[] imageData, CancellationToken cancellationToken = default(CancellationToken))
		{
			var layer = await _indoorMapFloorsRepository.GetByIdAsync(layerId);
			if (layer == null)
				return;

			using (var image = Image.Load(imageData))
			{
				int maxDimension = Math.Max(image.Width, image.Height);

				if (maxDimension <= TilingThreshold)
				{
					// Small image — store directly, no tiling
					layer.ImageData = imageData;
					layer.IsTiled = false;
					layer.TileMinZoom = null;
					layer.TileMaxZoom = null;
					layer.SourceFileSize = imageData.Length;
					await _indoorMapFloorsRepository.SaveOrUpdateAsync(layer, cancellationToken);
					return;
				}

				// Large image — generate tiles
				layer.ImageData = null;
				layer.IsTiled = true;
				layer.SourceFileSize = imageData.Length;

				// Calculate zoom levels
				int minZoom = 0;
				int maxZoom = (int)Math.Ceiling(Math.Log2(Math.Max(image.Width, image.Height) / (double)TileSize));
				if (maxZoom < 1) maxZoom = 1;

				layer.TileMinZoom = minZoom;
				layer.TileMaxZoom = maxZoom;
				await _indoorMapFloorsRepository.SaveOrUpdateAsync(layer, cancellationToken);

				// Delete existing tiles
				await _customMapTilesRepository.DeleteTilesForLayerAsync(layerId, cancellationToken);

				// Generate tiles at each zoom level
				for (int z = minZoom; z <= maxZoom; z++)
				{
					double scale = Math.Pow(2, z);
					int scaledWidth = (int)(image.Width * scale / Math.Pow(2, maxZoom));
					int scaledHeight = (int)(image.Height * scale / Math.Pow(2, maxZoom));

					if (scaledWidth < 1) scaledWidth = 1;
					if (scaledHeight < 1) scaledHeight = 1;

					using (var resized = image.Clone(ctx => ctx.Resize(scaledWidth, scaledHeight)))
					{
						int tilesX = (int)Math.Ceiling((double)scaledWidth / TileSize);
						int tilesY = (int)Math.Ceiling((double)scaledHeight / TileSize);

						for (int tx = 0; tx < tilesX; tx++)
						{
							for (int ty = 0; ty < tilesY; ty++)
							{
								int cropX = tx * TileSize;
								int cropY = ty * TileSize;
								int cropW = Math.Min(TileSize, scaledWidth - cropX);
								int cropH = Math.Min(TileSize, scaledHeight - cropY);

								using (var tile = resized.Clone(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropW, cropH))))
								{
									// Pad to 256x256 if needed
									if (cropW < TileSize || cropH < TileSize)
									{
										tile.Mutate(ctx => ctx.Resize(new ResizeOptions
										{
											Size = new Size(TileSize, TileSize),
											Mode = ResizeMode.BoxPad,
											PadColor = Color.Transparent
										}));
									}

									using (var ms = new MemoryStream())
									{
										await tile.SaveAsPngAsync(ms, cancellationToken);

										var tileEntity = new CustomMapTile
										{
																						CustomMapLayerId = layerId,
											ZoomLevel = z,
											TileX = tx,
											TileY = ty,
											TileData = ms.ToArray(),
											TileContentType = "image/png",
											AddedOn = DateTime.UtcNow
										};

										await _customMapTilesRepository.SaveOrUpdateAsync(tileEntity, cancellationToken);
									}
								}
							}
						}
					}
				}
			}
		}

		public async Task DeleteTilesForLayerAsync(string layerId, CancellationToken cancellationToken = default(CancellationToken))
		{
			await _customMapTilesRepository.DeleteTilesForLayerAsync(layerId, cancellationToken);
		}

		public async Task<CustomMapTile> GetTileAsync(string layerId, int z, int x, int y)
		{
			return await _customMapTilesRepository.GetTileAsync(layerId, z, x, y);
		}

		#endregion Tile Processing

		#region Geo Imports

		public async Task<CustomMapImport> ImportGeoJsonAsync(string mapId, string layerId, string geoJsonString, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var import = new CustomMapImport
			{
								CustomMapId = mapId,
				CustomMapLayerId = layerId,
				SourceFileName = "geojson-import.json",
				SourceFileType = (int)CustomMapImportFileType.GeoJSON,
				Status = (int)CustomMapImportStatus.Processing,
				ImportedById = userId,
				ImportedOn = DateTime.UtcNow
			};

			await _customMapImportsRepository.SaveOrUpdateAsync(import, cancellationToken);

			try
			{
				var featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(geoJsonString);

				if (featureCollection?.Features != null)
				{
					foreach (var feature in featureCollection.Features)
					{
						var zone = CreateZoneFromGeoJsonFeature(feature, layerId);
						if (zone != null)
							await _indoorMapZonesRepository.SaveOrUpdateAsync(zone, cancellationToken);
					}
				}

				import.Status = (int)CustomMapImportStatus.Complete;
				await _customMapImportsRepository.SaveOrUpdateAsync(import, cancellationToken);
			}
			catch (Exception ex)
			{
				import.Status = (int)CustomMapImportStatus.Failed;
				import.ErrorMessage = ex.Message;
				await _customMapImportsRepository.SaveOrUpdateAsync(import, cancellationToken);
			}

			return import;
		}

		public async Task<CustomMapImport> ImportKmlAsync(string mapId, string layerId, Stream kmlStream, bool isKmz, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var import = new CustomMapImport
			{
								CustomMapId = mapId,
				CustomMapLayerId = layerId,
				SourceFileName = isKmz ? "import.kmz" : "import.kml",
				SourceFileType = isKmz ? (int)CustomMapImportFileType.KMZ : (int)CustomMapImportFileType.KML,
				Status = (int)CustomMapImportStatus.Processing,
				ImportedById = userId,
				ImportedOn = DateTime.UtcNow
			};

			await _customMapImportsRepository.SaveOrUpdateAsync(import, cancellationToken);

			try
			{
				SharpKml.Engine.KmlFile kmlFile;

				if (isKmz)
				{
					using (var kmz = SharpKml.Engine.KmzFile.Open(kmlStream))
					{
						kmlFile = kmz.GetDefaultKmlFile();
					}
				}
				else
				{
					kmlFile = SharpKml.Engine.KmlFile.Load(kmlStream);
				}

				if (kmlFile?.Root != null)
				{
					var placemarks = kmlFile.Root.Flatten().OfType<SharpKml.Dom.Placemark>();

					foreach (var placemark in placemarks)
					{
						var zone = CreateZoneFromKmlPlacemark(placemark, layerId);
						if (zone != null)
							await _indoorMapZonesRepository.SaveOrUpdateAsync(zone, cancellationToken);
					}
				}

				import.Status = (int)CustomMapImportStatus.Complete;
				await _customMapImportsRepository.SaveOrUpdateAsync(import, cancellationToken);
			}
			catch (Exception ex)
			{
				import.Status = (int)CustomMapImportStatus.Failed;
				import.ErrorMessage = ex.Message;
				await _customMapImportsRepository.SaveOrUpdateAsync(import, cancellationToken);
			}

			return import;
		}

		public async Task<List<CustomMapImport>> GetImportsForMapAsync(string mapId)
		{
			var imports = await _customMapImportsRepository.GetImportsForMapAsync(mapId);

			if (imports != null)
				return imports.ToList();

			return new List<CustomMapImport>();
		}

		#endregion Geo Imports

		#region Private Helpers

		private IndoorMapZone CreateZoneFromGeoJsonFeature(Feature feature, string layerId)
		{
			if (feature?.Geometry == null)
				return null;

			string name = "Imported Region";
			if (feature.Properties != null && feature.Properties.ContainsKey("name"))
				name = feature.Properties["name"]?.ToString() ?? name;
			else if (feature.Properties != null && feature.Properties.ContainsKey("Name"))
				name = feature.Properties["Name"]?.ToString() ?? name;

			string geoJson = JsonConvert.SerializeObject(feature.Geometry);

			decimal centerLat = 0;
			decimal centerLon = 0;

			if (feature.Geometry is GeoJSON.Net.Geometry.Point point)
			{
				centerLat = (decimal)point.Coordinates.Latitude;
				centerLon = (decimal)point.Coordinates.Longitude;
			}
			else if (feature.Geometry is Polygon polygon && polygon.Coordinates.Any())
			{
				var ring = polygon.Coordinates.First().Coordinates;
				if (ring.Any())
				{
					centerLat = (decimal)ring.Average(c => c.Latitude);
					centerLon = (decimal)ring.Average(c => c.Longitude);
				}
			}
			else if (feature.Geometry is LineString line && line.Coordinates.Any())
			{
				centerLat = (decimal)line.Coordinates.Average(c => c.Latitude);
				centerLon = (decimal)line.Coordinates.Average(c => c.Longitude);
			}

			return new IndoorMapZone
			{
								IndoorMapFloorId = layerId,
				Name = name,
				Description = feature.Properties?.ContainsKey("description") == true
					? feature.Properties["description"]?.ToString()
					: null,
				ZoneType = (int)IndoorMapZoneType.Custom,
				GeoGeometry = geoJson,
				CenterLatitude = centerLat,
				CenterLongitude = centerLon,
				IsSearchable = true,
				IsDispatchable = true,
				IsDeleted = false,
				AddedOn = DateTime.UtcNow
			};
		}

		private IndoorMapZone CreateZoneFromKmlPlacemark(SharpKml.Dom.Placemark placemark, string layerId)
		{
			if (placemark?.Geometry == null)
				return null;

			decimal centerLat = 0;
			decimal centerLon = 0;
			string geoJson = null;

			if (placemark.Geometry is SharpKml.Dom.Point kmlPoint)
			{
				centerLat = (decimal)kmlPoint.Coordinate.Latitude;
				centerLon = (decimal)kmlPoint.Coordinate.Longitude;
				geoJson = JsonConvert.SerializeObject(new GeoJSON.Net.Geometry.Point(
					new Position(kmlPoint.Coordinate.Latitude, kmlPoint.Coordinate.Longitude)));
			}
			else if (placemark.Geometry is SharpKml.Dom.Polygon kmlPolygon)
			{
				var coords = kmlPolygon.OuterBoundary?.LinearRing?.Coordinates;
				if (coords != null && coords.Any())
				{
					centerLat = (decimal)coords.Average(c => c.Latitude);
					centerLon = (decimal)coords.Average(c => c.Longitude);

					var positions = coords.Select(c => new Position(c.Latitude, c.Longitude)).ToList();
					geoJson = JsonConvert.SerializeObject(new Polygon(
						new List<LineString> { new LineString(positions) }));
				}
			}
			else if (placemark.Geometry is SharpKml.Dom.LineString kmlLine)
			{
				var coords = kmlLine.Coordinates;
				if (coords != null && coords.Any())
				{
					centerLat = (decimal)coords.Average(c => c.Latitude);
					centerLon = (decimal)coords.Average(c => c.Longitude);

					var positions = coords.Select(c => new Position(c.Latitude, c.Longitude)).ToList();
					geoJson = JsonConvert.SerializeObject(new LineString(positions));
				}
			}

			if (geoJson == null)
				return null;

			return new IndoorMapZone
			{
								IndoorMapFloorId = layerId,
				Name = placemark.Name ?? "Imported Region",
				Description = placemark.Description?.Text,
				ZoneType = (int)IndoorMapZoneType.Custom,
				GeoGeometry = geoJson,
				CenterLatitude = centerLat,
				CenterLongitude = centerLon,
				IsSearchable = true,
				IsDispatchable = true,
				IsDeleted = false,
				AddedOn = DateTime.UtcNow
			};
		}

		#endregion Private Helpers
	}
}
